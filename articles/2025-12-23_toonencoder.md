# ToonEncoder - C#とLLMのためのJSON互換フォーマットエンコーダー

[Token-Oriented Object Notation(TOON)](https://github.com/toon-format/toon)というJSON互換のフォーマットのシリアライザー（エンコードのみ）を作りました。TOONは、適切に活用することで、LLMとの対話時に、トークンを大きく節約できる可能性を秘めています。コンパクトなライブラリーではありますが、内部的には全てUTF8ベースで処理していて、`IBufferWriter<byte>`対応やSource Generatorによるシリアライザー生成など、現代的なライブラリとしての基本機能は十分備えています。

* [GitHub - Cysharp/ToonEncoder](https://github.com/Cysharp/ToonEncoder)

もちろん、競合と比べてもパフォーマンスやメモリ効率は圧倒的に良いです。

![](https://raw.githubusercontent.com/Cysharp/ToonEncoder/main/img/image.png)

この辺はとにかく私がシリアライザーの設計に慣れすぎていて([MessagePack-CSharp](https://github.com/MessagePack-CSharp/MessagePack-CSharp), [MemoryPack](https://github.com/Cysharp/MemoryPack), [Utf8Json](https://github.com/neuecc/Utf8Json), etc...)実績もノウハウもありまくりなので……！ジャンルがジャンルなのでAIでとりあえず動くものにしましたっぽいライブラリも多い感じですが、全然勝負になりません。なんせこちらは温かみのある手作りコードですから……！ハイパーハンドメイドクラフトコーディング。現状のAI生成のコードレベルは、トップレベルからは、ほど遠いと実際思ってます。動くものはできるし、それは凄いんですけど、ね。

さて、まずTOONについて軽く説明すると、以下のJSONのデータが

```json
{
  "context": {
    "task": "Our favorite hikes together",
    "location": "Boulder",
    "season": "spring_2025"
  },
  "friends": ["ana", "luis", "sam"],
  "hikes": [
    {
      "id": 1,
      "name": "Blue Lake Trail",
      "distanceKm": 7.5,
      "elevationGain": 320,
      "companion": "ana",
      "wasSunny": true
    },
    {
      "id": 2,
      "name": "Ridge Overlook",
      "distanceKm": 9.2,
      "elevationGain": 540,
      "companion": "luis",
      "wasSunny": false
    },
    {
      "id": 3,
      "name": "Wildflower Loop",
      "distanceKm": 5.1,
      "elevationGain": 180,
      "companion": "sam",
      "wasSunny": true
    }
  ]
}
```

TOONで表現すると以下のように小さくなります。

```yaml
context:
  task: Our favorite hikes together
  location: Boulder
  season: spring_2025
  friends[3]: ana,luis,sam
  hikes[3]{id,name,distanceKm,elevationGain,companion,wasSunny}:
    1,Blue Lake Trail,7.5,320,ana,true
    2,Ridge Overlook,9.2,540,luis,false
    3,Wildflower Loop,5.1,180,sam,true
```

JSONというよりかは、YAMLとCSVのハイブリッドのようなもので、特に、テーブルとして(CSVとして)表現できる、プリミティブ要素のみを含むオブジェクトの配列が、CSV的に出力されるのでデータが大きく縮みます。この縮み幅がLLMにおけるトークンの節約に繋がるということでちょっとだけ脚光を浴びました。ならよくわからんフォーマットじゃなくてCSVでいいじゃん、というと、CSVだけだとテーブルのみで付随情報がつけられなくて実用には厳しいので、こちらのほうが使い勝手は良い印象です。また、JSONと相互互換のある仕様にしていることで、JSONからのDrop-in replacementが可能というのもセールスポイントにはなっています。

個人的な所感としてはTOONはヒューマンリーダブルではないです。TOONは効率性に寄せているため、配列の表現方法が3種類あります。ToonEncoderではTabularArray、InlineArray、NonUniformArrayと呼んでいますが、3種類あると正直読みづらいよね。また、TabularArrayとNonUniformArrayがオブジェクトのネストと合わさると、インデントがわけわからなくなります。LLMは、よくわからん形式とはいえ、ヒューマンリーダブルなら、なんとなくちゃんと読み取ってくれている雰囲気がありますが、そうした破綻した状態で解釈を正しく持ってくれるかどうかには不安があります。

というわけで、JSONを全て置き換えるのではなく、ピンポイントにCSV的なテーブル(TabularArrya)か、フラットなオブジェクトにTabularArrayを末尾に足したぐらいのものに適用するのが、トークン効率的にもLLMの理解力的にも人間のリーダビリティ的にもちょうど良いのではないかと思っています。実際ToonEncoderではそうした運用で最高なパフォーマンスが出るように調整してありますし、Microsoft.Extensions.AIとの組み合わせで、一部の型のみToon化する、といった連携ができるようになっています。

Microsoft.Extensions.AIと一緒に使う
---
[NuGet/ToonEncoder](https://www.nuget.org/packages/ToonEncoder)からダウンロードしてもらうとコアライブラリ―とSource Generatorが同梱でついてきます。なお最小ターゲットプラットフォームは .NET 10 です。

基本的には`Encode`で`JsonElement`、または`T value`を変換できます。

```csharp
using Cysharp.AI;

var users = new User[]
{
    new (1, "Alice", "admin"),
    new (2, "Bob", "user"),
};

// simply encode
string toon = ToonEncoder.Encode(users);

// [2]{Id,Name,Role}:
//   1,Alice,admin
//   2,Bob,user
Console.WriteLine(toon);

public record User(int Id, string Name, string Role);
```

今回はプリミティブ要素のみのオブジェクト配列のため、表形式レイアウト(TabularArray)としてシリアライズされています。

具体的な利用法としては[Microsoft.Extensions.AI](https://learn.microsoft.com/en-us/dotnet/ai/microsoft-extensions-ai)のFunction Callingに適用する場合は、対応する型のコンバーターを設定した `JsonSerializerOptions` を用意し、オプションに渡してあげると良いでしょう。また、Source Generatorを使うと、効率的なJsonConverterを生成してくれます。使用方法は対象の型に`[GenerateToonTabularArrayConverter]`するだけです！

```csharp
public IEnumerable<AIFunction> GetAIFunctions()
{
    var jsonSerializerOptions = new JsonSerializerOptions
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters =
        {
            // setup generated converter
            new Cysharp.AI.Converters.CodeDiagnosticTabularArrayConverter(),
        }
    };
    jsonSerializerOptions.MakeReadOnly(true); // need MakeReadOnly(true) or setup converter to TypeInfoResolver

    var factoryOptions = new AIFunctionFactoryOptions
    {
        SerializerOptions = jsonSerializerOptions
    };

    yield return AIFunctionFactory.Create(GetDiagnostics, factoryOptions);
}

[Description("Get error diagnostics of the target project.")]
public CodeDiagnostic[] GetDiagnostics(string projectName)
{
    // ...
}

// Trigger of Source Generator
[GenerateToonTabularArrayConverter]
public class CodeDiagnostic
{
    public string Code { get; set; }
    public string Description { get; set; }
    public string FilePath { get; set; }
    public int LocationStart { get; set; }
    public int LocationLength { get; set; }
}
```

この例の場合、`CodeDiagnostic[]`の件数が多いと、JsonとToonでトークン消費量にかなりの差が出てToonの優位度が高まります。ただし、Toonには得手不得手があるので、特性を見てToonを適用するか(Converterを追加するか)そのままにする(Json)かを選んでいくといいと思っています。


フラットな階層のオブジェクト（プリミティブ, プリミティブ要素の配列, プリミティブ要素のみで構成されたオブジェクトの配列）の生成の場合は、別の属性`[GenerateToonSimpleObjectConverter]`によりTabularArray + 追加のメタデータといったシナリオに対応できます。

```csharp
var item = new Item
{
    Status = "active",
    Users = [new(1, "Alice", "Admin"), new(2, "Bob", "User")]
};

var toon = Cysharp.AI.Converters.ItemSimpleObjectConverter.Encode(item);

// Status: active
// Users[2]{Id,Name,Role}:
//   1,Alice,Admin
//   2,Bob,User
Console.WriteLine(toon);

[GenerateToonSimpleObjectConverter]
public record Item
{
    public required string Status { get; init; }
    public required User[] Users { get; init; }
}
```

Json to Toon
---

`ToonEncoder.Encode`は `JsonElement` から `string`, `byte[]` への変換、 `IBufferWriter<byte>`, `ToonWriter`への書き込みをサポートします。

```csharp
namespace Cysharp.AI;

public static class ToonEncoder
{
    public static string Encode(JsonElement element);

    public static void Encode<TBufferWriter>(ref TBufferWriter bufferWriter, JsonElement element)
        where TBufferWriter : IBufferWriter<byte>;

    public static void Encode<TBufferWriter>(ref ToonWriter<TBufferWriter> toonWriter, JsonElement element)
        where TBufferWriter : IBufferWriter<byte>;

    public static byte[] EncodeToUtf8Bytes(JsonElement element);

    public static async ValueTask EncodeAsync(Stream utf8Stream, JsonElement element, CancellationToken cancellationToken = default);
}
```

`IBufferWriter<byte>`のオーバーロードを用いるとUTF8で直接データを書き込むため、`string`変換を介すよりもパフォーマンスが高くなります。

EncodeではJsonElementがarrayの際に、TabularArrayかInlineArrayかNonUniformArrayかどうかを全件チェックしてから書き込みしますが、`JsonElement`が`array`かつ、全ての要素の出現順序が等しく、全てがプリミティブ(Array, Objectではない)であることを保証できる場合は `EncodeAsTabularArray` メソッドを用いると検査を省くため、より高いパフォーマンスで変換できます。

```csharp
namespace Cysharp.AI;

public static class ToonEncoder
{
    public static string EncodeAsTabularArray(JsonElement array);

    public static void EncodeAsTabularArray<TBufferWriter>(ref TBufferWriter bufferWriter, JsonElement array)
        where TBufferWriter : IBufferWriter<byte>;

    public static byte[] EncodeAsTabularArrayToUtf8Bytes(JsonElement array);

    public static async ValueTask EncodeAsTabularArrayAsync(Stream utf8Stream, JsonElement array, CancellationToken cancellationToken = default);

    public static void EncodeAsTabularArray<TBufferWriter>(ref ToonWriter<TBufferWriter> toonWriter, JsonElement array)
        where TBufferWriter : IBufferWriter<byte>;
}
```

というのが基本的な変換の仕様になっています。

まとめ
---
この記事は、C# Advent Calendar 2025に特にエントリーしていない記事ですが、時期的にはだいたいそんな感じです。

このToonEncoderは、[Cysharp/CompilerBrain](https://github.com/Cysharp/CompilerBrain)という全然まだできてないC# Coding Agentのパーツとして用意しました。結構データ大量にドカドカするので節約したいなあ、と思い……。そんなわけで来年初頭はCompilerBrainやっていきます、多分……！

ところで改めて正直なところTOON自体は別に全然いいフォーマットとは思えません。というかどちらかといえば相当厳しい……。が、まぁマーケティング的にJSON互換でDrop-in replacementというのが響いたのはありそうだし、実際CSVだと厳しいっちゃあ厳しいので、とりあえず仕様があるという点で妥協として悪くないといえば悪くない選択かもしれません。

複雑なデータをシリアライズする気はない、ということが`[GenerateToonTabularArrayConverter]`と`[GenerateToonSimpleObjectConverter]`に現れています。これはAnalyzerも兼ねていて非対応なネストしたプロパティとか持たせようとするとコンパイルエラーにするという、ようはToonのサブセットみたいなものを疑似的に作り出しているんですね。もちろんJsonElement経由のメソッドを呼べば、ちゃんとネストしたプロパティとかはシリアライズできます。一応用意されている公式のテストスイートには（意図的にサポートしていない機能を除いて）全件合格しています。

またライブラリ名の通り、Encodeしかサポートしていません。Decodeはできません。LLMに送信するためのものなのでだから、デコードは別にいらないでしょう。

といった感じで色々と手を抜いたコンパクトさもあるのですが、それなりに実用的にはなっているので、興味ある方は是非是非試してみてください！