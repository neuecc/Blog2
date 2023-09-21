# .NET 8 UnsafeAccessor を活用したライブラリ PrivateProxy を公開しました

PrivateProxyというライブラリを公開しました。つまるところ、privateフィールド/プロパティ/メソッドにアクセスするライブラリなのですが、.NET 8 のUnsafeAccessorという新機能を活用することでNo Reflection、ハイパフォーマンス、AOTセーフになっています。

* [Cysharp/PrivateProxy](https://github.com/Cysharp/PrivateProxy)

もちろん .NET 8 でしか動きません！ので、.NET 8が正式リリースされた頃に思い出して使ってみてください。エクストリームな人は今すぐ試しましょう。

雰囲気としては、privateメンバーにアクセスしたい型があったとして、`[GeneratePrivateProxy(type)]`をつけた型を用意します。

```csharp
using PrivateProxy;

public class Sample
{
    int _field1;
    int PrivateAdd(int x, int y) => x + y;
}

[GeneratePrivateProxy(typeof(Sample))]
public partial struct SampleProxy;
```

すると、いい感じにアクセスできるようになります。

```csharp
// You can access like this.
var sample = new SampleProxy();
sample.AsPrivateProxy()._field1 = 10;
```

いいところとしては、 Source Generatorベースの生成なので、型がついていて入力補完も効くし、変数名を変更したらコンパイルエラーで検出可能です。

![](https://user-images.githubusercontent.com/46207/269376472-f6dd22e1-e82e-4acc-ba6e-8895c8c8734b.png)

ここまではSource Generatorベースで作れば、今まででもやれないことはなかったのですが、UnsafeAccessorのいいところとして、objectが一切出てこないで元のメソッドそのままの型がそのまま使えることです。生成されたコードを見ると、こうなっています。

```csharp
// Source Generator generate this type
partial struct SampleProxy(Sample target)
{
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_field1")]
    static extern ref int ___field1__(Sample target);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "PrivateAdd")]
    static extern int __PrivateAdd__(Sample target, int x, int y);

    public ref int _field1 => ref ___field1__(target);
    public int PrivateAdd(int x, int y) => __PrivateAdd__(target, x, y);
}

public static class SamplePrivateProxyExtensions
{
    public static SampleProxy AsPrivateProxy(this Sample target)
    {
        return new SampleProxy(target);
    }
}
```

これによって `ref` や `readonly` などの言語機能をそのまま反映できたり、mutableなstructの対応が自然にできたり、そして何よりパフォーマンスの低下も一切ありません。

使い道は、主にユニットテスト用になるとは思いますので、なのでパフォーマンスはそこまで重要ではないといえばないのですが、性能的にはアプリケーションの実行時に使っても問題ないものとなっています。

私は昔[Chaining Assertion](https://github.com/neuecc/ChainingAssertion)というユニットテスト用のライブラリを作っていたのですが、現在は[Fluent Assertions](https://fluentassertions.com/)という別のライブラリを使っています。ほとんどの機能は概ねなんとかなっているのですが、`AsDynamic()`を呼ぶと、移行はprivateフィールドやメソッドにアクセスし放題という機能は結構便利で、そしてそれがFluent Assertionsにないのは若干不便と思ってたんですね。今回、やっとそれの進化系を作れたのでメデタシメデタシです。

C# 12
---
ところで、冒頭のSampleProxyの書き方、地味にこれはC# 12の記法を使っています。どこだかわかりますか？

```csharp
[GeneratePrivateProxy(typeof(Sample))]
public partial struct SampleProxy;
```

`SampleProxy;` の部分で、空のクラスを作る際に `{ }` じゃなくて `;` だけで済ませられるようになりました。これは地味ですがかなりいい機能で、というのもSource Generatorだと空クラスに割り当てることが多かったんですよね。そして、たった2文字が1文字に変わっただけ、ではあるのですが、 `{ }` だとコードフォーマットに影響があります。改行して3行で表現するのか、後ろにつけるのか。そうした判断のブレが `;` だとなくなります。だから2文字が1文字に変わっただけ、以上のインパクトがある、良い機能追加だと思います。

ref field
---
PrivateProxyはstaticメソッドにも対応していますし、そしてmutable structにも対応しています。

```csharp
using PrivateProxy;

public struct MutableStructSample
{
    int _counter;
    void Increment() => _counter++;

    // static and ref sample
    static ref int GetInstanceCounter(ref MutableStructSample sample) => ref sample._counter;
}

// use ref partial struct
[GeneratePrivateProxy(typeof(MutableStructSample))]
public ref partial struct MutableStructSampleProxy;
```

```csharp
var sample = new MutableStructSample();
var proxy = sample.AsPrivateProxy();
proxy.Increment();
proxy.Increment();
proxy.Increment();

// call private static method.
ref var counter = ref MutableStructSampleProxy.GetInstanceCounter(ref sample);

Console.WriteLine(counter); // 3
counter = 9999;
Console.WriteLine(proxy._counter); // 9999
```

mutable structの対応って結構難しい話で、というのもフィールドにstructを保持するとコピーが渡されることになるので、普通に書いていると変更が元のstructに反映されないんですね。この問題をPrivateProxyではC# 11 ref fieldで解決しました。

MutableStructSampleProxyはSource Generatorによって以下のようなコードが生成されます。

```csharp
ref partial struct MutableStructSampleProxy
{
    ref MutableStructSample target;

    public MutableStructSampleProxy(ref MutableStructSample target)
    {
        this.target = ref target;
    }

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "Increment")]
    static extern void __Increment__(ref MutableStructSample target);
    
    public void Increment() => __Increment__(ref this.target);
}

public static class MutableStructSamplePrivateProxyExtensions
{
    public static MutableStructSampleProxy AsPrivateProxy(this ref MutableStructSample target)
    {
        return new MutableStructSampleProxy(ref target);
    }
}
```

AsPrivateProxy(これはthis refですが、拡張メソッドの場合、予備側はrefを書かなくていいので自然に使えます)で渡されたstructは、そのままずっとrefのまま保持されています。これにより、メソッド呼び出しでstructの状態に変更があった場合も問題なく変更が共有されています。

privateメソッドの単体テスト
---
[プライベートメソッドのテストは書かない](https://t-wada.hatenablog.jp/entry/should-we-test-private-methods)みたいな流儀も世の中にはありますが、私は何言ってんの？と思ってます。パブリックメソッドのテストでprivateメソッドの確認が内包されるから不要というなら、はぁー？だったら全部E2Eテストでいいんじゃないですかー？真の振る舞いがテストされますよー？

もちろんそれは非現実的で、E2Eでパブリックメソッドのロジックを全て通過させるのは手間とコストがかかりすぎる。というのと同じ話で、privateメソッドのエッジケースを全てチェックする時に、public経由だとやりにくいことは往々にある。場合によってはコード通すためにモックを仕込まなければならないかもしれない。そこまでいくとアホらしいですよね、privateメソッドを直接テストすればすむだけの話なのに？メソッドは小さければ小さいほど良いし、テストもしやすい。そしてprivateを内包したpublicよりもprivateそのもののほうが小さく、テストしやすい。

ようするにコスト面を考えて境界をどこに置くかというだけの話で。そして、privateメソッドのテストそのものは、C#の場合reflection呼び出しだと、変更コストがかかるなど、コスパは悪い部類に入ってしまう。でもそれはただの言語からの制約であって、だからプライベートメソッドのテストは不要みたいなしょーもない理屈をこねて絶対視するほどのことでもない。リフレクション使うとコスパ感が合わないから原則publicで済ませること、ぐらいだったらいいけど、変な教義立てるのはおかしいでしょ。

チェックしたいがためにprivateであるべきものをinternalにする（たまによくやる、internalならInternalsVisibleToをテストプロジェクトに指定することで、ユニットテストプロジェクトで参照できるようになる）こともありますが、あまりお行儀の良いことではない。そもそもinternalなので同一アセンブリ内では不要に可視レベル上がっちゃってるし。

と、いうわけで、PrivateProxyは比較的低コストでprivateメソッドをテスト対象にすることができるので、全然使っちゃっていいし、テストも書いちゃって良い、わけです。

UnsafeAccessor for InternalCall
---
UnsafeAccessor の使い道として、corelibの InternalCall を強引に呼べることはかなりいいこと（？）だと思ってます。例えば string の生成には大元に [FastAllocateString](https://source.dot.net/#System.Private.CoreLib/src/System/String.CoreCLR.cs,12) というのがあって、通常ユーザーはこれを呼ぶことはできないのですが

```csharp
[UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "FastAllocateString")]
static extern string FastAllocateString(string _, int length);

var rawString = FastAllocateString(null!, 10);
var mutableSpan = MemoryMarshal.CreateSpan(ref MemoryMarshal.GetReference(rawString.AsSpan()), rawString.Length);

"abcde".CopyTo(mutableSpan);
"fghij".CopyTo(mutableSpan.Slice(5));

Console.WriteLine(rawString); // abcdefghij
```

といったようにUnsafeAccessorを通せばやりたい放題できます。

Stringに関しては、やりたい放題させないために、[String.Create](https://learn.microsoft.com/ja-jp/dotnet/api/system.string.create)というメソッドが用意されていて、Actionのコールバックで変更して、不変のStringを返すというものが用意されてはいるのですが、ActionだとTStateに`Span<T>`を渡せないとか、使えないケースがそれなりにあります、というか使いたいシチュエーションに限って使えないことが多い……。

なお、こういうFastAllocateStringを呼んでStringに変更をかけるというのは、[dotnet/runtime#36989 Make string.FastAllocateString public](https://github.com/dotnet/runtime/issues/36989)で見事に却下されています。つまり、やるな、ということです。

> It is never safe or supported to mutate the contents of a returned string instance.
> If you mutate a string instance within your own library or application, you are entering unsupported territory.
> A future framework update could break you. Or - more likely - you'll encounter memory corruption that will be very painful for you or your customers to diagnose.

お怒りはご尤もです。しかしString.CreateでAction渡す口あるんだからそれでいいだろーというのはお粗末すぎだと思うんですよねー、どうせやってること一緒なんだから弄るの許可してよ、というのもそれはそれで理解してもらいたいです（私は弄りたいほうの人間なので！）。というわけで、自己責任で、やっていきましょう、つまりやっていくということです……！

まとめ
---
.NET 8でしか動きません！11月に .NET 8 がリリースされるので、その時まで忘れないでください！