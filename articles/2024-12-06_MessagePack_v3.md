# SourceGenerator対応のMessagePack for C# v3リリースと今後について

先月[MessagePack for C#プロジェクト](https://github.com/MessagePack-CSharp/MessagePack-CSharp)は [.NET Foundation](https://dotnetfoundation.org/)に参加しました！より安定した視点で利用していただけるという一助になればいいと思っています。

そして、長く開発を続けていたメジャーバージョンアップ、v3がリリースされました。コア部分はv2とはほぼ変わらずですが、Source Generatorを全面的に導入しています。引き続きIL動的生成も存在するため、IL動的生成とSource Generatorのハイブリッドなシリアライザーとなります。v3にはSource GeneratorとAnalyzerがビルトインで同梱されていて、今までのコードはv3でコンパイルするだけで自動的にSource Generator化されます。v2 -> v3アップデートでSource Generator対応するために追加でユーザーがコードを記述する必要はありません！

挙動を詳しく見ていきましょう。例えば、

```csharp
[MessagePackObject]
public class MyTestClass
{
    [Key(0)]
    public int MyProperty { get; set; }
}
```

というコードを書くと、自動的に以下のコードがSource Generatorによって内部的に生成されます。

```csharp
partial class GeneratedMessagePackResolver
{
    internal sealed class MyTestClassFormatter : IMessagePackFormatter<MyTestClass>
    {
        public void Serialize(ref MessagePackWriter writer, MyTestClass value, MessagePackSerializerOptions options)
        {
            if (value == null)
            {
                writer.WriteNil();
                return;
            }

            writer.WriteArrayHeader(1);
            writer.Write(value.MyProperty);
        }

        public MyTestClass Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
        {
            if (reader.TryReadNil())
            {
                return null;
            }

            options.Security.DepthStep(ref reader);
            var length = reader.ReadArrayHeader();
            var ____result = new MyTestClass();

            for (int i = 0; i < length; i++)
            {
                switch (i)
                {
                    case 0:
                        ____result.MyProperty = reader.ReadInt32();
                        break;
                    default:
                        reader.Skip();
                        break;
                }
            }

            reader.Depth--;
            return ____result;
        }
    }
}
```

また、このGeneratedMessagePackResolverはデフォルトのオプション(StandardResolverなど)に最初から登録されているため、

```csharp
public static readonly IFormatterResolver[] DefaultResolvers = [
    BuiltinResolver.Instance,
    AttributeFormatterResolver.Instance,
    SourceGeneratedFormatterResolver.Instance, // here
    ImmutableCollection.ImmutableCollectionResolver.Instance,
    CompositeResolver.Create(ExpandoObjectFormatter.Instance),
    DynamicGenericResolver.Instance, // only enable for RuntimeFeature.IsDynamicCodeSupported
    DynamicUnionResolver.Instance];
```

ユーザーコードのアセンブリに含まれているシリアライズ対象クラスは、Source Generatorによって生成されたコードが優先的に使われることになります。GeneratedMessagePackResolverは既定の名前空間や名前を変えたり、生成フォーマッターをmapベースに変更するなど、幾つかのカスタマイズポイントも用意されています。より詳しくは新しいドキュメントを見てください。また、v2 -> v3の変更箇所の詳細を知りたい人は[Migration Guide v2 -> v3](https://github.com/MessagePack-CSharp/MessagePack-CSharp/blob/develop/doc/migrating_v2-v3.md)をチェックしてください。

Unityにおいては導入方法が大きく変わりました。コアライブラリは .NET 版と共通になりNuGetからのインストールが必要となります。そのうえでUPMでUnity用の追加コードをダウンロードする必要があります。詳しくは[MessagePack-CSharp#unity-support](https://github.com/MessagePack-CSharp/MessagePack-CSharp/#unity-support)のセクションを確認してください。

.unitypackageの提供は廃止されています。また、IL2CPP対応のために要求していたmpcはなくなりました。完全にSource Generatorに移行されます。そのため、Unityのサポートバージョンは `2022.3.12f1` からとなります。Source Generatorに関してはNuGetForUnityでのコアライブラリインストール時に自動的に有効化されるため、追加の作業は必要ありません。

History and Next
---
MessagePack for C#のオリジナル(v1)は私(Yoshifumi Kawai/@neuecc)によって、2017年にリリースしました。当時開発していたゲームのパフォーマンス問題を解決するために、2016年時点で存在していた(バイナリ)シリアライザーでは需要を満たせなかったため、パフォーマンスを最重要視したバイナリシリアライザーとして作成しました。合わせて、同じくネットワークシステムとして作成したgRPCベースのRPCフレームワーク[MagicOnion](https://github.com/Cysharp/MagicOnion)もリリースしています。

v1リリース当時は`byte[]`のみを対象としていましたが、`Span<T>`や`IBufferWriter<T>`など、.NETには次々と新しいI/O系のAPIが追加されていったため、v2ではそれらに焦点を当てた新しいデザインが導入されました。この実装はMicrosoftのEngineerである[Andrew Arnott / @AArnott](https://github.com/AArnott)氏によって主導され、リリースしています。

以降、共同のメンテナンス体制として、そして私の個人リポジトリ(neuecc/MessagePack-CSharp)からオーガナイゼーション(MessagePack-CSharp/MessagePack-CSharp)して今に至ります。Visual Studio内部での利用や[SignalRのバイナリープロトコル](https://learn.microsoft.com/en-us/aspnet/core/signalr/messagepackhubprotocol)、Blazor Serverのプロトコルなど大きなMicrosoftのプロダクトでも使用され、GitHubでのスター数は.NETのバイナリーシリアライザーとしては最も大きなスターを集めています。[.NET 9で廃止されたBinaryFormatter](https://learn.microsoft.com/en-us/dotnet/standard/serialization/binaryformatter-migration-guide/)の移行先の一つとしても推奨されています。

v3ではSource Generatorに対応することで、より高いパフォーマンスと柔軟性、AOT対応への第一段階に踏み出すことができました。

MessagePack for C#プロジェクトは大きな成功を収めたと考えていますが、しかし現在、AArnott氏は個人の新しいMessagePackプロジェクトの開発を開始しています。私もその間、[MemoryPack](https://github.com/Cysharp/MemoryPack)という異なるフォーマットのシリアライザーをリリースしています。そのため、MessagePack for C#の今後と、その特性について、ある程度説明する必要があると思います。

引き続きメンテナンス体制は2人だと考えていますが、アクティブな活動に関しては、再び私が担うことになるかもしれません。私はMessagePackとMemoryPackとでは異なる性質を持ったフォーマットであるため、どちらも重要であるという認識で動いています。オリジナルの実装であるMessagePack for C#も気に入ってますし、現在においても決して引けを取ることのないものだと思っています。

AArnott氏の別のMessagePackシリアライザーとは根本的な哲学が若干異なります。その点で、私はそれはより良く改善されたシリアライザーではなく、別の個性のシリアライザーだと認識しています。そこで、違いについて説明させてください。

Binary spec, default settings and performance
---
シリアライザーのパフォーマンスに重要なのは、「仕様と実装」の両方です。例えばテキストフォーマットのJSONよりもバイナリフォーマットのほうが一般的には速いでしょう。しかし、よくできたJSONシリアライザーは、中途半端な実装のバイナリシリアライザーよりも高速です（私はそれを[Utf8Json](https://github.com/neuecc/Utf8Json)というシリアライザーを作成することで実証したことがあります）。なので、仕様も大事だし、実装も大事です。どちらも兼ねることができれば、それがベストなパフォーマンスのシリアライザーとなります。

[MessagePackのバイナリ仕様](https://msgpack.org/)は "It's like JSON. but fast and small." を標語にしている通り、JSONのバイナリ化としてあらわされています。ところが、MemoryPack for C#のデフォルトは必ずしもJSON likeを狙っているわけではありません。

```csharp
[MessagePackObject]
public class MsgPackSchema
{
    [Key(0)]
    public bool Compact { get; set; }
    [Key(1)]
    public int Schema { get; set; }
}
```

このクラスをシリアライズした場合は、JSONで表現すると`[true, 0]`のようになります。これはオブジェクトをarrayベースでシリアライズしているからで、mapベースでシリアライズすると`{"Compact":true,"Schema":0}`のような表現になります。

arrayベースの利点は見た通りに、バイナリ容量として、よりコンパクトになります。容量がコンパクトなことは処理量が少なくなるためシリアライズの速度にも良い影響を与えます。また、デシリアライズにおいては、文字列を比較してデシリアライズするプロパティを探索する必要がなくなるため、より高速なデシリアライズ速度が期待できます。

なお、arrayベースのシリアライズはMessagePackの仕様策定者である Sadayuki Furuhashi 氏によるリファレンス実装であるmsgpack-javaなどでも採用されているため、決して異端のやり方というわけではありません。

MessagePack-CSharpではJSONライクなmapベースでシリアライズしたい場合は`[MessagePackObject(true)]`と記述することができます。また、Source Generatorの場合はResolver単位でオーバーライドして強制的にmapベースにすることも可能です。

```csharp
[MessagePackObject(keyAsPropertyName: true)]
public class MsgPackSchema
{
    public bool Compact { get; set; }
    public int Schema { get; set; }
}
```

mapの利点は、柔軟なスキーマエボリューションの実現と、他言語との疎通する際にコミュニケーションが取りやすいこと、バイナリそのものの自己記述性が高いことです。デメリットは容量とパフォーマンスへの悪影響、特にオブジェクトの配列においては一要素毎にプロパティ名が含まれることになってしまい、かなりの無駄となります。

デフォルトをarrayにしているのは、コンパクトさとパフォーマンスの追求のためです。私はMessagePackをJSON likeの前に、高いパフォーマンスを実現可能なバイナリ仕様として考えました。もちろん、mapも重要なので、その上で比較的簡単にmapモードを実現するために属性に`(true)`を追加するだけで可能にしました。

arrayモードの場合はKey属性を全てのプロパティに付与する必要があります。これは、例えばProtocol Buffersなどでも数値タグを必要とするように、プロパティ名そのものをキーとするわけではなければ、必須だと考えています。もちろん、連番で自動採番させることも可能ですが、バイナリフォーマットのキーを暗黙的に処理するのはリスクが大きすぎる(順番を弄ったりするだけでバイナリ互換性が壊れることになる)と判断しています。つまり、明示的がデフォルト、ということです。大きなプロジェクト開発ではシニアメンバーからジュニアメンバーまでコードを触ることになるでしょう、全てを理解している人だけがコードを触るわけではありません。なので、暗黙的な挙動は避けるべきで、明示的にすべきだという強い意志で、この設計を選んでいます。

ただしKeyを全てのプロパティに付与する作業はとても苦痛です(私はMessagePack-CSharp開発以前には、DataContractやprotobuf-netで辛い思いをしました)。そこで、Analyzer + Code Fixによって、自動的に付与する機能を用意しました。これにより明示的であることの苦痛は和らげられ、良いとこどりができているのだと考えています。

別のMessagePackシリアライザーのデフォルトはmapのようです。これは[PolyType](https://github.com/eiriktsarpalis/PolyType)というSource Generatorベースのライブラリ作成のための抽象化ライブラリがベースとしているためでもあり、また、そちらのほうを好んでいるという明示的な判断でもあるようです。

「デフォルト」はライブラリで一つしか選べません。どちらのモードで処理することができたとしても、「デフォルト」はただ一つです。改めて言うと、私はバイナリフォーマットとしての「コンパクトとパフォーマンス」を好み、優先しています。

皆さんはPolyTypeについて初めて知ったかもしれません。私はPolyTypeはあまり好意的には考えていません。ちょっとしたものを作るには非常に便利だとは思いますが、ベストなパフォーマンスを狙ったり、ベストなアイディアを表現するには、抽象層であることの制限が大きすぎると考えています。なので、MessagePack for C#で採用することはありませんし、他の何かを作る際にも採用することはないでしょう。

Unity(multiplatform) Support
---
MessagePack for C#ではv1の時代からゲームエンジンUnityの1st classのサポートを実行してきました。これは私が[Cygames](https://en.wikipedia.org/wiki/Cygames)という日本のゲーム会社の関連会社([Cysharp](https://cysharp.com/))のCEOを務めていて、ビデオゲームインダストリーと関係性が深いという都合もあります。自分たちで実際にUnityで動くものを作り、使ってきました。もちろん、サーバーサイドやデスクトップアプリケーションでも使っています。

UnityにはIL2CPPという独自のAOTシステムがあり、特にiOSなどモバイルプラットフォームでのリリースには必須なのですが、それもSource Generatorが存在しなかった時代から、mpcというRoslynを使ったコードジェネレートツールを作り、提供してきました。数百のモバイルゲームでMessagePackが使われているのは、これら私の熱心なサポートのお陰といっても過言ではないでしょう。v3ではついにSource Generatorベースになったことにより、ワークフローが大きく簡易化されることとなります！

一般的に、.NETコミュニティにおいてはUnityサポートはかなり軽視されていました。また、外から見ているとMicrosoftやMicrosoftの従業員もそのようで、自社のプラットフォーム以外への関心は薄そうです。こうした態度は、あまり好ましいとは思っていませんし、せっかくの .NET の可能性を狭めていることにもなっています。Xamarinがうまく成長軌道に乗らなかったのも、そのようなMicrosoft自体の冷たい視線のせいだとも思っています。

私は、私の作るライブラリはなるべくUnityにもしっかり対応できるように気を付けて作っています（最新は新しいReactive Extensionsライブラリーである[Cysharp/R3](https://github.com/Cysharp/R3)）。別のMessagePackシリアライザーに関しては、あまりしっかりした対応はされなさそうですが……。

Beyond v3
---
v3のNative AOT Supportは完全ではありません。Source Generatorにするだけでは完全なNative AOT対応とはならないのは難しいところです。これはUnityのAOTであるIL2CPPでは完璧に動作しているだけに、正直不可解なことでもあり、また、Microsoftのよくない癖が出ているな、とも思っています。つまり、完璧な対応をするために、複雑なものを提供している。それが現在のNative AOTです。複雑怪奇な属性やフローは、理解できるところもありますが、もう少し簡略化すべきだったと思います。まぁ、もう修正されることもないのでしょうが……。

パフォーマンス面でもv1からv2で退化してしまった点もあるので、最新の知見を元に、実装面での改善を施す必要があります。特にReadOnlySequenceの利用幅が大きいことは、かなりの制約を生み出していて、不満があります。

.NET 9でPipeReader/PipeWriterが標準化されたことによる、より良い非同期APIや、パフォーマンスを両立したストリーミング対応というのも、大きなトピックとなるかもしれません。

MessagePack for C#は広く使われているが故に、破壊的変更はしづらいし、互換性の維持は最重要トピックスです。しかし、世の中が変わっていく以上、進化しないことを選んだら、それは滅びる道でしかありません。やれることはまだまだあると思っていますので、.NETにおける最先端の、最高のバイナリシリアライザーであり続けたいと思っています（[MemoryPack](https://github.com/Cysharp/MemoryPack)もね……！)

まずは、v3のSource Generatorをぜひ試してみてください。皆の力でより良いものを作っていけるというのも、OSSの良さだと思っています。