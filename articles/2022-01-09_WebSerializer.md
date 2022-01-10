# WebSerializer - オブジェクトからクエリストリングに変換するHttpClientリクエスト用シリアライザ

`T value`から URLエンコードされたクエリストリング、または`x-www-form-urlencoded`なHttpContentを生成する、つまりはウェブ(HTTP/1)リクエスト用のシリアライザを作りました。

* [github.com/Cysharp/WebSerializer](https://github.com/Cysharp/WebSerializer/)

クエリストリングの生成、意外と面倒くさいな！と。(C#用の)専用のSDKが存在しないWeb APIの場合は、自分でURL組み立てたり`FormUrlEncodedContent`を組み立てたりしますが、数が多いとまぁ面倒くさい。リクエストのパラメーター数が多いと、null抜いたりも面倒くさい。

レスポンス側は[ReadFromJsonAsync](https://docs.microsoft.com/ja-jp/dotnet/api/system.net.http.json.httpcontentjsonextensions.readfromjsonasync?view=net-6.0)などでダイレクトに変換できるようになって特に問題はないのですが、リクエスト側は、かなりの手作業が要求されます。そのへんを全部やってくれる[refit](https://github.com/reactiveui/refit)というライブラリもありますが(Androidの[retrofit](https://github.com/square/retrofit)にインスパイアされたもの)、導入するにはちょっと大仰だな、と思うときも多々あります、というか私は今まで一度も使ってません。

HttpClient用にURLを組み立てるのを簡略化してくれるぐらいでいいな、と思って考えていたら、そういえばそもそもそれってT valueから何かに変換する、つまりシリアライザじゃん、ということに気づきました。T -> msgpack byte[]に変換すればMessagePackシリアライザだし、T -> Json stringに変換すればJSONシリアライザだし、これはT -> UrlEncoded stringに変換するということなのだと。シリアライザ脳なので、そう理解すれば話が早い。

```csharp
using Cysharp.Web;

var req = new Request(sortBy: "id", direction: SortDirection.Desc, currentPage: 3)

// sortBy=id&direction=Desc&currentPage=3
var q = WebSerializer.ToQueryString(req);

await httpClient.GetAsync("/sort?"+ q);

// data...
public record Request(string? sortBy, SortDirection direction, int currentPage);

public enum SortDirection
{
    Default,
    Asc,
    Desc
}
```

基本的に使うメソッドは `WebSerializer.ToQueryString` か `WebSerializer.ToHttpContent` だけです。URLエンコードされてname=valueで&連結された文字列が取り出せます。メソッドとして叩いたりする場合は、そのまま匿名型で渡してあげればちょうど良い。urlも一緒に渡してあげれば全て同時に組み立ててくれます。値が`null`のものは文字列化対象から自動で外されます。

```csharp
const string UrlBase = "https://foo.com/search";

// null, SortDirection.Asc, 0
async Task SearchAsync(string? sortBy, SortDirection direction, int currentPage)
{
    // "https://foo.com/search?direction=Asc&currentPage=0"
    var url = WebSerializer.ToQueryString(UrlBase, new { sortBy, direction, currentPage });
    await httpClient.GetAsync(url);
}
```

動的に組み立てる場合は、`Dictionary<string, object>` も渡せます。

```csharp
var req = new Dictionary<string, object>
{
    { "sortBy", "id" },
    { "direction", SortDirection.Desc },
    { "currentPage", 10 }
};
var q = WebSerializer.ToQueryString(req);
```

POST用には、`ToHttpContent`を使います。

```csharp
async Task PostMessage(string name, string email, string message)
{
    var content = WebSerializer.ToHttpContent(new { name, email, message });
    await httpClient.PostAsync("/postmsg", content);
}
```

内部的には`FormUrlEncodedContent`は使わずに、専用のHttpContentを通しているため、`byte[]`変換のオーバーヘッドがありません。

シリアライザ設計
---
ただたんにクエリストリング組み立てるだけっしょ！というと軽く見られてしまうかもしれないのですが、中身はかなりガチめに作ってあって、構成としては[MessagePack for C#](https://github.com/neuecc/MessagePack-CSharp/)と同様です。パフォーマンスに関しても超ギチギチに詰めているわけではないですが、かなり気を配って作られているので、手で組み立てるよりもむしろ高速になるケースも多いはずです。拡張性もかなり高く作れているはずです。

シリアライザのデザインに関してはMessagePack for C#の次期バージョン(v3)をどうしていこうかなあ、と考えているタイミングでもあるので、そのプロトタイプ的な意識もありますね。なので設計としてはむしろ最新型で、かなり洗練されています。.NET 5/6のみにしているので、レガシーも徹底的に切り捨てていますし。最初は .NET 6のみだったのですが、さすがにそれはやりすぎかと思い .NET 5は足しました。

例えばコンフィグ(`WebSerializerOptions`)はイミュータブルなのですが、これ自体はrecordで作ってあってwith式でカスタムのコンフィグを作れます。

```csharp
// CultureInfo: 数値型やDateTimeの文字列化変換に渡すCultureInfo、デフォルトはnull
// CollectionSeparator: 配列などを変換する場合のセパレーター、デフォルトは","
// Provider: 対象の型をどのように変換するか(`IWebSerialzier<T>`)の変更
var newConfig = WebSerializerOptions.Default with
{
    CultureInfo = CultureInfo.InvariantCulture,
    CollectionSeparator = "  ",
    Provider = WebSerializerProvider.Create(
        new[] { new BoolZeroOneSerializer() },
        new[] { WebSerializerProvider.Default })
};

// Bool値を0, 1に変換する（こういうの求めてくるWeb APIあるんですよねー！）
public class BoolZeroOneSerializer : IWebSerializer<bool>
{
    public void Serialize(ref WebSerializerWriter writer, bool value, WebSerializerOptions options)
    {
        // true => 0, false => 1
        writer.AppendPrimitive(value ? 0 : 1);
    }
}
```

`IWebSerializer<T>`のインターフェイスについて、`ref T value`にしようか検討したのですが、最終的にやめました。

```csharp
public interface IWebSerializer<T> : IWebSerializer
{
    void Serialize(ref WebSerializerWriter writer, T value, WebSerializerOptions options);
}
```

`ref T value`にすると、プロパティをそのまま渡せなくて、かなり面倒くさくなってね。理屈的にはlarge structに対するコピーコスト削減、ではあるけれど、まぁこのままだと99%効力ないかなあ、という感じがあり。入り口だけinにして一回分コピーを消すぐらいを落とし所にしました、とりあえず今回は。

```csharp
public static string ToQueryString<T>(in T value, WebSerializerOptions? options = default)
```

それとSource Generator対応についても考えましたが、まぁ一旦今回は見送って、後でやるかもという感じでしょうか。アイディアは色々ありますが、まずは作ってみないとうまくハマるか見えないところがあるし、MessagePack for C#のような大きなものでドカンとやるよりは、最初は小さなものでテストしていくのが良いものを作る正攻法でもありますね。

まとめ
---
手で組み立てている人は結構多いと思うので、使えるシチュエーションはかなりあると思ってます。ただまあ、こんぐらいなら手でやるよ！と思う人は多いと思うので、その点ではニッチかなあ、というところですね。Web APIの仕様によってはリクエストパラメーターが微妙にデカくてイライラすることがあったり、まぁあとは数を作るときにはやっぱダルいので、ハマるシチュエーションも少なくはないかな、と。

とりあえずは試してみてもらえればと思います。