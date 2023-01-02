# 2022年のC# (Incremental) Source Generator開発手法

このブログでもSource GeneratorやAnalyzerの開発手法に関しては定期的に触れてきていて、新しめだと

* [2020/12/15 - UnitGenerator - C# 9.0 SourceGeneratorによるValueObjectパターンの自動実装とSourceGenerator実装Tips](https://neue.cc/2020/12/15_597.html)
* [2021/05/07 - 2021年のC# Roslyn Analyzerの開発手法、或いはUnityでの利用法](https://neue.cc/2021/05/08_600.html)

という記事を出していますが、今回 [MemoryPack](https://github.com/Cysharp/MemoryPack/) の実装で比較的大規模にSource Generatorを使ってみたことで、より実践的なノウハウが手に入りました。また、開発環境も年々良くなっていることや、Unityのサポート状況も強化されているので、状況を一通りまとめてみようと思いました。Source Generatorは非常に強力で、今後必須の開発技法になるので（少なくとも私はもうIL書きません！）是非、この機会に手を出して頂ければです。

Microsoft.CodeAnalysis.CSharpのバージョン問題
---
Source Generatorを作成するには [Microsoft.CodeAnalysis.CSharp](https://www.nuget.org/packages/Microsoft.CodeAnalysis.CSharp/)を参照したライブラリを作ればいい、のですが、ここで大事なのはバージョンです。何も考えずに最新を入れると動かないという罠が待ってます。Source Generatorは、インストールされている .NET のバージョンや IDEのコンパイラバージョンと深く紐づいています。.NETのバージョンだけ上げてもダメで、特にVisual Studioの場合は.NETのバージョンと独立して、同梱されているコンパイラのバージョンがあり、それと合わせる必要があります。Unityの場合も同じく、Unityに含まれるC#コンパイラのバージョン(/Editor/Data/DotNetSdkRoslyn/Microsoft.CodeAnalysis.CSharp.dll)を精査する必要があります。使わているバージョンよりも高いバージョンのものを参照すると、動かないという理屈です。

Visual Studioのバージョンとの紐づきは [.NET コンパイラ プラットフォーム パッケージ バージョン リファレンス](https://learn.microsoft.com/ja-jp/visualstudio/extensibility/roslyn-version-support)を見れば分かりますが、現状の私のオススメは `4.3.1` です（現時点での最新は `4.4.0` ）。これは最小サポートバージョンがVisual Studio 2022 Version 17.3ということで、VS2019は切り捨てでいいでしょう。VS2022使ってるなら、とりあえずそこまでアップデートしてくれ、ということで。古ければ古いほどカバーできる範囲が広がっていい！ようでいて、古ければ古いほど、新しい言語機能の解析ができないなどの問題があるので、お薦めはできません、むしろ何も問題がなければ新しければ新しいほどいいぐらいです。4.3.1がおすすめな最大の理由としては、`SyntaxValueProvider.ForAttributeWithMetadataName` という、後で説明しますが、Source Generator作成の際に必須とも言える便利メソッドが追加されていることです。`4.4.0` だとC# 11解析サポートが追加されている、はずなのですが公式ドキュメントのほうにVisual Studioとの対応関係がまだ追加されていないというのもあり手を出しにくい……。

Unityの場合は公式にC#コンパイラのバージョンが何であるかのリストはないので、自分で調べていく必要がありますが、とりあえず[Roslyn analyzers and source generators](https://docs.unity3d.com/Manual/roslyn-analyzers.html)という公式ドキュメントによると「must use Microsoft.CodeAnalysis 3.8」、というわけで3.8じゃないと動かないぞ、と脅しをかけてきてます。が、実際は現状のLTS環境では3.9が搭載されているようなので、3.9を使ったほうがいいでしょう。例えばUnity 2021.3は3.9が入っていて、実際ちゃんと3.9でも動きますし、APIが3.8と3.9でかなり変わっているので、3.9で作ったほうが楽です。ドキュメントは更新が遅れて最新の話が反映されていない場合が往々にあるので、正しい現状把握は重要ですね。

Microsoft.CodeAnalysis.CSharpのバージョンは大きく分けて 3.* と 4.* があり、3.* はv1の `ISourceGenerator`、4.* はv2である `IIncrementalGenerator` が使えます。

[Incremental Generators](https://github.com/dotnet/roslyn/blob/main/docs/features/incremental-generators.md)は、性能面で大きく改善されている他、作りやすさも大きく上がっているため、現状は Incremental Generators で作ることを最優先で考えたほうがいいでしょう。登場の黎明期では、IDEのバージョン問題があったために、3.* と 4.* の両方のSource Generatorを作って一緒にNuGetパッケージングする、という（かなりややこしい）手法が取られたこともありましたが、もう .NET 7も登場した2022年、も終わろうとしている現在ですので、 3.* は切り捨ててしまってもいいと考えています。

ただしUnityは除く。調べたところUnityでは Unity 2022.2, Unity 2023.1 から、4.1.0のコンパイラが搭載されているようなので、そこを最小ターゲットにすればIncremental Generatorを動かすこともできなくはないのですが、さすがに攻めすぎなので、Unityをターゲットにする場合のみ 3.* で生成したものを配布する、といった形がいいのではないかと思っています。 3.* と 4.* 版の両方を作るという手間はありますが、NuGetパッケージングのややこしさには手を出さなくてもいい。ぐらいが現状の落としどころじゃないでしょうか。

最小プロジェクトとデバッグ実行
---
Source Generator開発は、デバッグ環境をきっちり構築できていないとかなり大変です。なので環境構築をしっかりやってから挑みましょう。ここではWindowsのVisual Studio 2022を使った場合の説明のみしますが、他の環境でも、同等のことができるようにしておかないとめちゃくちゃ大変です。

まず「.NET Compiler Platform SDK」を入れましょう。標準では入ってないので。入れておかなくても開発はできるのですが、デバッグ起動ができなくなるため、ほぼ必須と思ってください。

![image](https://user-images.githubusercontent.com/46207/207808216-9b65a422-5cd5-4a74-99a8-8635c65437c6.png)

次に、「netstandard2.0」のクラスライブラリプロジェクトを作成します。え、2022年にもなってnetstandard2.0なの？なんで？standard2.1やnet7じゃダメなの？という感じですが、そもそもVisual Studioが .NET Frameworkで動いているというしょっぱい事情があり、Source Generatorプロジェクトはnetstandard2.0で作る必要があるという制限があります。使えるクラスライブラリが少なくて辛い感もありますが我慢です。

```xml
<Project Sdk="Microsoft.NET.Sdk">

	<PropertyGroup>
		<TargetFramework>netstandard2.0</TargetFramework>
		<ImplicitUsings>enable</ImplicitUsings>
		<Nullable>enable</Nullable>

		<!-- LangVersionは明示的に書いておこう -->
		<LangVersion>11</LangVersion>
		<!-- Analyzer(Source Generator)ですという設定 -->
		<IsRoslynComponent>true</IsRoslynComponent>
		<AnalyzerLanguage>cs</AnalyzerLanguage>
	</PropertyGroup>

	<ItemGroup>
		<PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.3.1" />
	</ItemGroup>

</Project>
```

```csharp
using Microsoft.CodeAnalysis;

namespace SourceGeneratorSample;

[Generator(LanguageNames.CSharp)]
public partial class SampleGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Providerシリーズ
        // context.AdditionalTextsProvider
        // context.AnalyzerConfigOptionsProvider
        // context.CompilationProvider
        // context.MetadataReferencesProvider
        // context.ParseOptionsProvider
        // context.SyntaxProvider

        // Registerシリーズ
        // context.RegisterImplementationSourceOutput
        // context.RegisterPostInitializationOutput
        // context.RegisterSourceOutput
    }
}
```

これで無のSource Generatorができたので（contextの解説は準備が一通り終わったらします）、次に、このGeneratorを参照するConsoleAppを適当に作成します。

```xml
<Project Sdk="Microsoft.NET.Sdk">

	<PropertyGroup>
		<OutputType>Exe</OutputType>
		<TargetFramework>net7.0</TargetFramework>
		<ImplicitUsings>enable</ImplicitUsings>
		<Nullable>enable</Nullable>
	</PropertyGroup>

	<ItemGroup>
		<ProjectReference Include="..\SourceGeneratorSample\SourceGeneratorSample.csproj">
			<OutputItemType>Analyzer</OutputItemType>
			<ReferenceOutputAssembly>false</ReferenceOutputAssembly>
		</ProjectReference>
	</ItemGroup>

</Project>
```

Source Generatorのプロジェクト参照では、OutputItemTypeとReferenceOutputAssemblyの設定を追加で手書きしてください。

次にまたSource Generator側のプロジェクトに戻って、プロジェクトのプロパティから「デバッグ起動プロファイルUIを開く」を選んでください。

![image](https://user-images.githubusercontent.com/46207/208009373-a5f32edb-998b-4fde-844b-f67b52da8747.png)

既にあるプロファイルは削除した上で、左上の「新しいプロファイルの作成」から「Roslyn Component」を選択。ここでRoslyn Componentが出てこない場合は、「.NET Compiler Platform SDK」を入れているかどうかの確認と、csprojに`<IsRoslynComponent>true</IsRoslynComponent>`を追加しているかどうかの確認をしてください。

![image](https://user-images.githubusercontent.com/46207/208009505-a6e86403-c42c-4f0b-bf53-97c5e42d367d.png)

そしてTarget Projectに、先ほど作成したSource Generatorを参照しているコンソールアプリプロジェクトを選びます。プロジェクトが選べない場合は、対象プロジェクトがSource GeneratorをAnalyzerとしてのプロジェクト参照をしているかどうかを確認してください。

![image](https://user-images.githubusercontent.com/46207/208009562-74d267c6-584e-43fd-93a5-c180b1c4de1e.png)

これで準備が完了で、Source Generatorをデバッグ実行(F5)すると、対象コンソールアプリプロジェクトを引っ掛けた状態で起動するようになります。

![image](https://user-images.githubusercontent.com/46207/208011024-62d3cae7-08f7-45d3-b910-312b3137d663.png)

あとは、ひたすら、Generatorのコードを書いていくだけです、めでたし。

ForAttributeWithMetadataName
---
細かい説明に行く前に、基本的な流れの説明を。Source Generatorは、通常、なにか適当な属性がついているpartial classやpartial methodを探して、それに対して追加のpartial class/methodを生成する、という流れになります。原理的には属性がついていなくてもいいですが、勝手に何かを生成されるとわけわかんなくて困るので、ユーザーに明示的に生成を指示させるような流れにすべき、ということで、起点は属性付与だけと考えていいでしょう。

そんなわけでSource Generatorでまずやることは、属性が付与されてるclass/methodを探し出すことなのですが、Roslyn 4.3.1からは `SyntaxValueProvider.ForAttributeWithMetadataName` というメソッドで一発で探し出すことができるようになりました。

というわけで、小さなサンプル用ジェネレーターとして、classのToStringをrecordのように自動実装するジェネレーターを作ってみます。

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SourceGeneratorSample;

[Generator(LanguageNames.CSharp)]
public partial class SampleGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // PostInitializationOutputでSource Generatorでしか使わない属性を出力
        context.RegisterPostInitializationOutput(static context =>
        {
            // C# 11のRaw String Literal便利
            context.AddSource("SampleGeneratorAttribute.cs", """
namespace SourceGeneratorSample;

using System;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
internal sealed class GenerateToStringAttribute : Attribute
{
}
""");
        });

        var source = context.SyntaxProvider.ForAttributeWithMetadataName(
            "SourceGeneratorSample.GenerateToStringAttribute", // 引っ掛ける属性のフルネーム
            static (node, token) => true, // predicate, 属性で既に絞れてるので特別何かやりたいことがなければ基本true
            static (context, token) => context); // GeneratorAttributeSyntaxContextにはNode, SemanticModel(Compilation), Symbolが入ってて便利

        // 出力コード部分はちょっとごちゃつくので別メソッドに隔離
        context.RegisterSourceOutput(source, Emit);
    }
```

Initializeメソッドの行数の短さ！というわけで、Source Generator作り自体はかなり簡単になりました。ここまでがSourceGeneratorとして属性を引っ掛けて何かするための準備部分の全てであり、過去の諸々に比べると明らかに改善されています。

ただし、そうして抽出したところを加工して何かする部分は特に変わりないので、気合で頑張っていきましょう。↑のコードの続きは以下のものになります。

```csharp
    static void Emit(SourceProductionContext context, GeneratorAttributeSyntaxContext source)
    {
        // classで引っ掛けてるのでTypeSymbol/Syntaxとして使えるように。
        // SemaintiModelが欲しい場合は source.SemanticModel
        // Compilationが欲しい場合は source.SemanticModel.Compilation から
        var typeSymbol = (INamedTypeSymbol)source.TargetSymbol;
        var typeNode = (TypeDeclarationSyntax)source.TargetNode;

        // ToStringがoverride済みならエラー出す
        if (typeSymbol.GetMembers("ToString").Length != 0)
        {
            context.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.ExistsOverrideToString, typeNode.Identifier.GetLocation(), typeSymbol.Name));
            return;
        }

        // グローバルネームスペース対応漏れするとたまによく泣くので気をつける
        var ns = typeSymbol.ContainingNamespace.IsGlobalNamespace
            ? ""
            : $"namespace {typeSymbol.ContainingNamespace};";

        // 出力ファイル名として使うので雑エスケープ
        var fullType = typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            .Replace("global::", "")
            .Replace("<", "_")
            .Replace(">", "_");

        // Field/Propertyを抽出する
        var publicMembers = typeSymbol.GetMembers() // MethodがほしければOfType<IMethodSymbol>()などで絞る
            .Where(x => x is (IFieldSymbol or IPropertySymbol)
                         and { IsStatic: false, DeclaredAccessibility: Accessibility.Public, IsImplicitlyDeclared: false, CanBeReferencedByName: true })
            .Select(x => $"{x.Name}:{{{x.Name}}}"); // MyProperty:{MyProperty}

        var toString = string.Join(", ", publicMembers);

        // C# 11のRaw String Literalを使ってText Template的な置換(便利)
        // ファイルとして書き出される時対策として <auto-generated/> を入れたり
        // nullable enableしつつ、nullable系のwarningがウザいのでdisableして回ったりなどをテンプレコードとして入れておいたりする
        var code = $$"""
// <auto-generated/>
#nullable enable
#pragma warning disable CS8600
#pragma warning disable CS8601
#pragma warning disable CS8602
#pragma warning disable CS8603
#pragma warning disable CS8604

{{ns}}

partial class {{typeSymbol.Name}}
{
    public override string ToString()
    {
        return $"{{toString}}";
    }
}
""";

        // AddSourceで出力
        context.AddSource($"{fullType}.SampleGenerator.g.cs", code);
    }
}

// DiagnosticDescriptorは大量に作るので一覧性のためにもまとめておいたほうが良い
public static class DiagnosticDescriptors
{
    const string Category = "SampleGenerator";

    public static readonly DiagnosticDescriptor ExistsOverrideToString = new(
        id: "SAMPLE001",
        title: "ToString override",
        messageFormat: "The GenerateToString class '{0}' has ToString override but it is not allowed.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
```

作り方のポイントとしては、Source Generator(Analyzer)で使うものにはSyntaxNodeとISymbolの二系統があって、SyntaxNodeは文字列としてのソースコードの構造を指していて、ISymbolはコンパイルされた状態での型の中間状態を指します。情報を取ったりするにはISymbolのほうが圧倒的にやりやすいので、基本的にはSymbolを辿って処理していきます。SyntaxNodeは、エラーの波線表示の位置を示したりする時のみに使うという感じですね。

では、これをビルドして、Visual Studioを、再起動します……！というのも、ConsoleApp1側ではSource Generatorを掴みっぱなしになってしまうので、プロジェクト参照でのSource Generatorの更新ができないからです。今回AttributeをGenerator側で追加しているので、再起動してそれの生成を含めてあげる必要があります。今後もConsoleApp1側での動作確認が必要な際は、定期的に再起動する羽目になります。ただしデバッグ起動では更新されたコードで動くので、大きな変動がなければそのまま作業を進められます。といった、IDEを再起動しなきゃいけないシチュエーションなのかしなくてもいいのか、の切り分けが求められます……。

ConsoleApp1側で以下のようなテスト型を用意して

```csharp
using SourceGeneratorSample;

var mc = new MyClass() { Hoge = 10, Bar = "tako" };
Console.WriteLine(mc);

[GenerateToString]
public partial class MyClass
{
    public int Hoge { get; set; }
    public string? Bar { get; set; }
}
```

Source Generator側でデバッグ実行です。いったんの出力の確認でお薦めなのは、AddSourceの直前あたりにブレークポイント貼って見ることですかね。

![image](https://user-images.githubusercontent.com/46207/208027141-ae09c996-a7a4-4780-bce0-8a9e22727a5e.png)

そうして何度かデバッグ実行を繰り返して、理想となるコードが吐けるように調整していって、そして、最終的にそれで大丈夫かどうかはコンパイラ通さないとわからんので、Visual Studioを再起動してConsoleApp1側でコンパイル走らせて、みたいなことになりますね。この段階で問題が出ると、Visual Studio再起動祭りになるのでダルい！

問題なく吐けていれば、ソリューションエクスプローラーで生成コードを確認することができます。

![image](https://user-images.githubusercontent.com/46207/208027544-81eb7279-aef7-48ff-8241-fa6fa2b4efa3.png)

以上、基本的な流れでした！C# 11のRaw String Literalsのお陰で別途テンプレートエンジンを用いなくても、テンプレート的な処理をC#のコード中に埋め込めるようになったのが、かなり楽になりました。（ただしif や for が埋め込めるわけではないので、複雑なものを書く場合はそれなりの工夫は必要）。

Source Generatorの良いところはAnalyzerも兼ねているところで、今回はToStringが既に定義されている場合はエラーにするという処理を入れているのですが

![image](https://user-images.githubusercontent.com/46207/208030486-1baf9e07-c22e-4c40-8c0c-7b968180ee58.png)

属性でどうこうする系ってどうしても今までは実行時エラーになりがちだったのですが、エディット時に間違って定義をばんばん教えてあげられるようになったのは親切度が相当上がっています。

IncrementalGeneratorInitializationContext詳解
---
Incremental Generatorの強みは複数のProviderを繋げてパイプラインを作れるところ、ではあるのですが、基本的なことは SyntaxProvider.ForAttributeWithMetadataName がほとんど全部やってくれるから、特に考えなくてもいいかな……。

ではあるんですが、細かい処理をしたい場合にはいくつか必要になりますので、Provider見ていきましょう。

* AdditionalTextsProvider

AdditionalTextsProviderは、AdditionalFilesを読み取るのに使います。[BannedApiAnalyzers](https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.BannedApiAnalyzers/BannedApiAnalyzers.Help.md)などでも活用されていますが、例えばコンフィグを渡したいケースなどに有用です。

例えば `sampleGenerator.config.json` を読み取りたい、といったケースを考えますと、ConsoleApp1側ではこういったcsprojとファイルを用意するとして

```xml
<ItemGroup>
	<AdditionalFiles Include="sampleGenerator.config.json" />
</ItemGroup>
```

AdditionalTextsProviderを使ってこんな風に読み取っていきます。

```csharp
var configuration = context.AdditionalTextsProvider.Select((text, token) =>
    {
        if (text.Path.EndsWith("sampleGenerator.config.json")) return text.GetText(token);
        return null;
    })
    .Where(x => x != null)
    .Collect(); //雑Collect

// sampleにあったやつ
var types = context.SyntaxProvider.ForAttributeWithMetadataName(
      "SourceGeneratorSample.GenerateToStringAttribute",
      static (node, token) => true,
      static (context, token) => context)
    .Collect(); //雑Collect

var source = configuration.Combine(types);  // くっつける

context.RegisterSourceOutput(source, static (context, source) =>
{
    var configJson = source.Left.FirstOrDefault();
    var types = source.Right;
    foreach (var type in types)
    {
        // よしなに処理
    }
});
```

なるほどコードが増えた？

まず、Providerが触った直後のやつは `IncrementalValuesProvider<T>` になります。そしてCollectすると `IncrementalValueProvider<ImmutableArray<T>>` になります。違いはImmutableArray、ではなくて、 ValueProvider と ValuesProvider のほうです。ValueProviderの状態だと(IObservableみたいに)複数値が流れてくるのですが、ValuesProviderの状態だと、ImmutableArrayとして一塊になったものが一発流れてきます。

で、複数ProviderをCombineで繋いで、RegsiterSourceOutputに流し込むという流れになるわけですが、ValueとValuesが混在してるとCombineの型合わせがめちゃくちゃ大変です……！なんかよくわからんがCombineできない！の原因は型が合わないせいなのですね。というわけで雑にCollectしておくと合わせやすくなるので良いです。

というわけで、こんな感じで次のProvider行きましょう。

* AnalyzerConfigOptionsProvider

GlobalOptionsと、AdditionalTextやSyntaxTreeに紐付けられたオプションを引っ張るGetOptionsがあります。例えばMemoryPackではcsprojのオプションから取り出すために使いました。

こういう記述をして

```xml
<ItemGroup>
    <CompilerVisibleProperty Include="MemoryPackGenerator_SerializationInfoOutputDirectory" />
</ItemGroup>
<PropertyGroup>
    <MemoryPackGenerator_SerializationInfoOutputDirectory>$(MSBuildProjectDirectory)\MemoryPackLogs</MemoryPackGenerator_SerializationInfoOutputDirectory>
</PropertyGroup>
```

こんな風に取り出すことができる( `build_property.` が接頭辞に必要)みたいな。

```csharp
var outputDirProvider = context.AnalyzerConfigOptionsProvider
    .Select((configOptions, token) =>
    {
        if (configOptions.GlobalOptions.TryGetValue("build_property.MemoryPackGenerator_SerializationInfoOutputDirectory", out var path))
        {
            return path;
        }

        return (string?)null;
    });
```

csproj側があんま書きやすい感じじゃないので、AdditionalFilesでjsonを渡すのとどちらがいいのか、みたいなのは考えどころですね。こちらだとcsproj内のマクロが使える（出力パスとか）のはいいところかもしれません。

* CompilationProvider

Compilationが拾える最重要Provider、のはずが `ForAttributeWithMetadataName` がくっつけてくれるので用無し。

* MetadataReferencesProvider

読み込んでるDLLの情報が拾えます。

![image](https://user-images.githubusercontent.com/46207/208037687-47a65304-e8d9-42cb-bdd6-f88ea622bc03.png)

そんな使わないかも。

* ParseOptionsProvider

csprojを解析した情報が取れます。例えば言語バージョンやプリプロセッサシンボルから、.NETのバージョンを取り出したりできます。

```csharp
var parseOptions = context.ParseOptionsProvider.Select((parseOptions, token) =>
{
    var csOptions = (CSharpParseOptions)parseOptions;
    var langVersion = csOptions.LanguageVersion;
    var net7 = csOptions.PreprocessorSymbolNames.Contains("NET7_0_OR_GREATER");
    return (langVersion, net7);
});
```

つまり、言語バージョンや.NETのバージョン別の出し分けに使える、ということですね。細かくやると面倒くさいのであんまギチギチにやらないほうがいいとは思いますが、どうしてもそういう処理が必要なシチュエーションでは使えます。というか実際MemoryPackではこれで出し分けしています。scoped ref(C# 11)やfile scoped namespace(C# 10)、static abstract method(.NET 7)という切り分けですねー。

* SyntaxProvider

`ForAttributeWithMetadataName` を叩くためのやつ。

* RegisterPostInitializationOutput

ここからはRegisterシリーズですが、PostInitializeationOutputは、Source Generatorのためのマーカーとしてしか使わない属性をinternal classとして解析走らせる前に出力しておきたい、というやつですね。[UnitGenerator](https://github.com/Cysharp/UnitGenerator/)では `UnitOfAttribute` をそういった形で吐き出しています（なので結果としてUnitGeneratorを使ったプロジェクトはUnitGeneratorへの依存DLLはなし、ということになる）。一方でMemoryPackで使ってる属性 `MemoryPackableAttribute` は、`MemoryPack.Core.dll`に含めているので、RegisterPostInitializationOutputは使っていません。どうせReader/Writerとかの他の依存が必要になるので、属性だけ依存なしにしてもしょーがないですからね。

* RegisterSourceOutput

Providerを繋げて、実際にSource Generateさせるやつ。大事というか必須。

* RegisterImplementationSourceOutput

ドキュメントが一切ない上に、なんか想定通りの動きをしていないような私の想定が悪いのか、まぁよくわからないけどよくわからないのでよくわからないです。ドキュメントも無なので、とりあえず無視しておきましょう。

ユニットテスト
---
厳密にやるとキリがないので、そこそこゆるふわ感覚でやるようにしてます。もちろんTDDなんてしません。基本的な考え方としては、ユニットテストプロジェクトがAnalyzerとして開発中のSource Generatorプロジェクトをプロジェクト参照して、ソース生成されるようにしておいて、ユニットテストでは、その生成されたコードが期待通り動いているかのテストをする、みたいな雰囲気で良いんじゃないかと思います。生成ソースコードの中身をチェックして一致するか、みたいなのはちょっと手間が無駄にかかりすぎるので……。

テストプロジェクトはxUnitと、補助ライブラリとして[FluentAssertion](https://fluentassertions.com/)を好んで使っています。また、GlobalUsingにテスト系の名前空間を突っ込んでおくと気持ち楽です。

```xml
<Project Sdk="Microsoft.NET.Sdk">

	<PropertyGroup>
		<TargetFramework>net7.0</TargetFramework>
		<ImplicitUsings>enable</ImplicitUsings>
		<Nullable>enable</Nullable>
		<IsPackable>false</IsPackable>
	</PropertyGroup>

	<ItemGroup>
		<PackageReference Include="FluentAssertions" Version="6.7.0" />
		<PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.4.0" />
		<PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.3.1" />
		<PackageReference Include="xunit" Version="2.4.2" />
		<PackageReference Include="xunit.runner.visualstudio" Version="2.4.5">
			<IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
			<PrivateAssets>all</PrivateAssets>
		</PackageReference>
	</ItemGroup>

	<ItemGroup>
		<ProjectReference Include="..\SourceGeneratorSample\SourceGeneratorSample.csproj">
			<OutputItemType>Analyzer</OutputItemType>
            <!-- ReferenceOutputAssemblyをtrueにする! -->
			<ReferenceOutputAssembly>true</ReferenceOutputAssembly>
		</ProjectReference>
	</ItemGroup>

	<ItemGroup>
		<Using Include="Xunit" />
		<Using Include="Xunit.Abstractions" />
		<Using Include="FluentAssertions" />
	</ItemGroup>

</Project>
```

後述しますが C# 11の内部コンパイルを行うために参照する Microsoft.CodeAnalysis.CSharp は 4.4.0 です。

```csharp
namespace SourceGeneratorSample.Tests;

public class ToStringTest
{
    [Fact]
    public void Basic()
    {
        var mc = new MyClass() { Hoge = 33, Huga = 99 };
        mc.ToString().Should().Be("Hoge:33, Huga:99");
    }
}

[GenerateToString]
public partial class MyClass
{
    public int Hoge { get; set; }
    public int Huga { get; set; }
}
```

とりあえずこれをテストすればOK、と。なんか生成結果が更新されてない気がして無限にTestがこけるんだが？という時は、例によってVisual Studio再起動です。

Source Generatorのいいところとして、生成コードへのステップ実行も可能ということで、なんかよーわからん挙動だわーという時はデバッガでどんどん突っ込んでいくといいでしょう。

![image](https://user-images.githubusercontent.com/46207/208041286-54162ad8-fe1a-41d6-a1f2-37e0dd19c533.png)

正常に動くケースはこれで概ねいいんですが、Analyzerとしてコンパイルエラーを出すようなケースをテストしたい場合は、もう一捻り必要です。対応としては `CSharpGeneratorDriver` というのが標準で用意されていて、それにソースコード渡せばいい、という話なのですが、少し手間なのは、元になるCSharpCompilationを作らなければいけない、というところで。この辺もよしなに見てくれる便利ジェネレーターユニットテストヘルパーライブラリみたいなのもありますが、原理原則を知るためにも、ここは手で書いてみましょう。

というわけで、こういうヘルパーを用意してみます。

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;

namespace MemoryPack.Tests.Utils;

public static class CSharpGeneratorRunner
{
    static Compilation baseCompilation = default!;

    [ModuleInitializer]
    public static void InitializeCompilation()
    {
        // running .NET Core system assemblies dir path
        var baseAssemblyPath = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var systemAssemblies = Directory.GetFiles(baseAssemblyPath)
            .Where(x =>
            {
                var fileName = Path.GetFileName(x);
                if (fileName.EndsWith("Native.dll")) return false;
                return fileName.StartsWith("System") || (fileName is "mscorlib.dll" or "netstandard.dll");
            });

        var references = systemAssemblies
            // .Append(typeof(Foo).Assembly.Location) // 依存DLLがある場合はそれも追加しておく
            .Select(x => MetadataReference.CreateFromFile(x))
            .ToArray();

        var compilation = CSharpCompilation.Create("generatortest",
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        baseCompilation = compilation;
    }

    public static Diagnostic[] RunGenerator(string source, string[]? preprocessorSymbols = null, AnalyzerConfigOptionsProvider? options = null)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp11, preprocessorSymbols: preprocessorSymbols);
        var driver = CSharpGeneratorDriver.Create(new SourceGeneratorSample.SampleGenerator()).WithUpdatedParseOptions(parseOptions);
        if (options != null)
        {
            driver = (Microsoft.CodeAnalysis.CSharp.CSharpGeneratorDriver)driver.WithUpdatedAnalyzerConfigOptions(options);
        }

        var compilation = baseCompilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(source, parseOptions));

        driver.RunGeneratorsAndUpdateCompilation(compilation, out var newCompilation, out var diagnostics);

        // combine diagnostics as result.
        var compilationDiagnostics = newCompilation.GetDiagnostics();
        return diagnostics.Concat(compilationDiagnostics).ToArray();
    }
}
```

CSharpGeneratorDriver.Create して AddSyntaxTrees して RunGeneratorsAndUpdateCompilation して diagnostics を取り出す。というだけなのですが、Compilationを作るところに癖があります、というか Compilation に渡すDLLをかき集めるのが微妙に面倒くさいです。net7の依存関係のDLLを全部持ってくる、とかが一発でできないんですね。素直に typeof().Assembly.Location だけだと全然持ってこれないため、ディレクトリから漁ってくるという処理をいれています。

これを使ってテスト書くと、こんな感じでしょうか。

```csharp
    [Fact]
    public void ERROR_SAMPLE001()
    {
        // C#11のRaw String Literals本当に便利
        var result = CSharpGeneratorRunner.RunGenerator("""
using SourceGeneratorSample;

[GenerateToString]
public partial class MyClass
{
    public int Hoge { get; set; }
    public int Huga { get; set; }

    public override string ToString()
    {
        return "hogemoge";
    }
}
""");

        result.Length.Should().Be(1);
        result[0].Id.Should().Be("SAMPLE001");
    }
```

厳密にやるなら、エラーの波線をどこに敷いているかのチェックをすべし、みたいな話もあるのですが、私的にはまぁ面倒くさいのでちゃんと狙ったエラーが出せてるかどうかをDiangnositcsのIdを拾うぐらいでいいかな、みたいな感じでやってます。

NuGetパッケージング
---
というわけで `dotnet pack` するわけですが、 追加でコンフィグ仕込む必要があります。

```csharp
<Project Sdk="Microsoft.NET.Sdk">

	<PropertyGroup>
		<TargetFramework>netstandard2.0</TargetFramework>
		<ImplicitUsings>enable</ImplicitUsings>
		<Nullable>enable</Nullable>
		<LangVersion>11</LangVersion>

		<!-- NuGetPackのための追加をもりもり -->
		<IsRoslynComponent>true</IsRoslynComponent>
		<AnalyzerLanguage>cs</AnalyzerLanguage>
		<IncludeBuildOutput>false</IncludeBuildOutput>
		<DevelopmentDependency>true</DevelopmentDependency>
		<IncludeSymbols>false</IncludeSymbols>
		<SuppressDependenciesWhenPacking>true</SuppressDependenciesWhenPacking>
	</PropertyGroup>

	<ItemGroup>
		<PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.3.1" />
	</ItemGroup>

	<!-- 出力先を analyzers/dotnet/cs にする -->
	<ItemGroup>
		<None Include="$(OutputPath)\$(AssemblyName).dll" Pack="true" PackagePath="analyzers/dotnet/cs" Visible="false" />
	</ItemGroup>
</Project>
```

外部依存DLLがいたりすると(例えばJSON .NET使いたいとか！)もう少し面倒くさくなるので、外部依存DLLは使わないようにしましょう！というのが第一原則になります。どうしても使いたい場合は頑張ってください。

Unity対応
---
まずIncremental Source Generator使えないし ForAttributeWithMetadataName も使えないしちくしょーって感じですが、とはいえそこまで差分が多いわけでもないのでやってきましょう。

まず、簡易的な ForAttributeWithMetadataName っぽいものを用意します。MemoryPackでは以下のコードを使ってます。

```csharp
class SyntaxContextReceiver : ISyntaxContextReceiver
{
    internal static ISyntaxContextReceiver Create()
    {
        return new SyntaxContextReceiver();
    }

    public HashSet<TypeDeclarationSyntax> ClassDeclarations { get; } = new();

    public void OnVisitSyntaxNode(GeneratorSyntaxContext context)
    {
        var node = context.Node;
        if (node is ClassDeclarationSyntax
                    or StructDeclarationSyntax
                    or RecordDeclarationSyntax
                    or InterfaceDeclarationSyntax)
        {
            var typeSyntax = (TypeDeclarationSyntax)node;
            if (typeSyntax.AttributeLists.Count > 0)
            {
                var attr = typeSyntax.AttributeLists.SelectMany(x => x.Attributes)
                    .FirstOrDefault(x =>
                    {
                        var packable = x.Name.ToString() is "MemoryPackable" or "MemoryPackableAttribute" or "MemoryPack.MemoryPackable" or "MemoryPack.MemoryPackableAttribute";
                        if (packable) return true;
                        return false;
                    });
                if (attr != null)
                {
                    ClassDeclarations.Add(typeSyntax);
                }
            }
        }
    }
}
```

ざっくりしてる感じですが、機能はするのでよしとしておきましょう。次にSourceGenerator と IncrementalGenerator を共通化するContextを用意しておきます。

```csharp
// share context for SourceGenerator and IncrementalGenerator
public interface IGeneratorContext
{
    CancellationToken CancellationToken { get; }
    void ReportDiagnostic(Diagnostic diagnostic);
    void AddSource(string hintName, string source);
}
```

そして、最初のサンプルコードでいうところのEmit部分を↑のIGeneratorContextを使うようにしてファイル分離、そしてCompile Includeで.csを参照するようにします。MemoryPackでは以下のようにしています。

```xml
<ItemGroup>
    <Compile Include="../MemoryPack.Generator/**/*.cs" Exclude="**/obj/**;**/MemoryPackGenerator.cs;**/*TypeScript*.cs" />
</ItemGroup>
```

大事なのはプロジェクト分離しないこと、ですね！NuGetパッケージングのところでも書きましたがAnalyzer(Source Generator)でごちゃごちゃした依存作ると面倒臭さが跳ね上がるので、シングルアセンブリに収まるように作るべし、ということです。

あとは[Unityのマニュアル](https://docs.unity3d.com/Manual/roslyn-analyzers.html)通りにビルド済みdllを配置してRoslynAnalyzerとしてLabel設定したmetaを置いておけば、UPMのgit参照とかでも、特に何もせずに自動で認識されます。dllの配置場所はUnityの公式のジェネレーター(例えば com.unity.properties とか)がRuntime配下にいるので、Editorではなく、Runtime側に配置することとしています。

なお、Unity用限定のSource Generatorを作る場合でも、通常の .NET のライブラリとして扱い、普通に .NET ライブラリとしての開発環境やユニットテストプロジェクトを作ったほうが良いでしょう。普通に作るにもかなり環境をしっかり作らないと大変なので、Unity限定だから！みたいな気持ちで挑むとしんどみが爆発します。

まとめ
---
C#に最初にこの手の機構が登場したのは2014年、 [VS2015のRoslynでCode Analyzerを自作する(ついでにUnityコードも解析する)](https://neue.cc/2014/11/20_485.html) といった記事も書いていたのですが、まぁ正直めっっっちゃくちゃ作りづらかったんですね。

で、現代、この2022年のSource Generator開発はめっっっっちゃくちゃ作りやすくなってます。もちろん、Roslyn自体の知識が必要で、そしてRoslynはドキュメントが無なので、どちらかというとIntelliSenseから勘をどう働かせるかという勝負になっていて、それはそれで大変ではあるのですが、しかし本当に作りやすくなったな、と思います。もちろんそしてIL.Emitよりも遥かに作りやすいし、パフォーマンスも良い。もうEmitの時代は終わりです。もはや黒魔術を誇る時代でもないのです！動的コード生成の民主化！

というわけで、どしどしコード生成していきましょう……！私も今温めてるアイディアが3つぐらいあるので、どんどんリリースしていきたいと思ってます。