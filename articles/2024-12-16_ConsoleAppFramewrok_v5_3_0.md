# ConsoleAppFramework v5.3.0 - NuGet参照状況からのメソッド自動生成によるDI統合の強化、など

[ConsoleAppFramework](https://github.com/Cysharp/ConsoleAppFramework) v5の比較的アップデートをしました！v5自体の詳細は以前に書いた[ConsoleAppFramework v5 - ゼロオーバーヘッド・Native AOT対応のC#用CLIフレームワーク](https://neue.cc/2024/06/13_ConsoleAppFramework_v5.html)を参照ください。v5はかなり面白いコンセプトになっていて、そして支持されたと思っているのですが、幾つか使い勝手を犠牲にした点があったので、今回それらをケアしました。というわけで使い勝手がかなり上がった、と思います……！

名前の自動変換を無効にする
---
コマンドネームとオプションネームは、デフォルトでは自動的にkebab-caseに変換されます。これはコマンドラインツールの標準的な命名規則に従うものですが、内部アプリケーションで使うバッチファイルの作成に使ったりする場合などには、変換されるほうが煩わしく感じるかもしれません。そこで、アセンブリ単位でオフにする機能を今回追加しました。

```csharp
using ConsoleAppFramework;

[assembly: ConsoleAppFrameworkGeneratorOptions(DisableNamingConversion = true)]

var app = ConsoleApp.Create();
app.Add<MyProjectCommand>();
app.Run(args);

public class MyProjectCommand
{
    public void Execute(string fooBarBaz)
    {
        Console.WriteLine(fooBarBaz);
    }
}
```

`[assembly: ConsoleAppFrameworkGeneratorOptions(DisableNamingConversion = true)]`によって自動変換が無効になります。この例では `ExecuteCommand --fooBarBaz` がコマンドとなります。

実装面でいうと、Source Generatorにコンフィグを与えるのはAdditionalFilesにjsonや独自書式のファイル(例えばBannedApiAnalyzersのBannedSymbols.txt)を置くパターンが多いですが、ファイルを使うのは結構手間が多くて面倒なんですよね。boolの1つや2つを設定するぐらいなら、アセンブリ属性を使うのが一番楽だと思います。

実装手法としては`CompilationProvider`から`Assembly.GetAttributes`で引っ張ってこれます。

```csharp
var generatorOptions = context.CompilationProvider.Select((compilation, token) =>
{
    foreach (var attr in compilation.Assembly.GetAttributes())
    {
        if (attr.AttributeClass?.Name == "ConsoleAppFrameworkGeneratorOptionsAttribute")
        {
            var args = attr.NamedArguments;
            var disableNamingConversion = args.FirstOrDefault(x => x.Key == "DisableNamingConversion").Value.Value as bool? ?? false;
            return new ConsoleAppFrameworkGeneratorOptions(disableNamingConversion);
        }
    }

    return new ConsoleAppFrameworkGeneratorOptions(DisableNamingConversion: false);
});
```

これを他のSyntaxProviderからのSourceとCombineしてやれば、生成時に属性の値を参照できるようになります。

ConfigureServices/ConfigureLogging/ConfigureConfiguration
---
DIとの統合時に自分でServiceProviderをビルドしなければならないなの、利用には一手間必要でした。そこで、`Microsoft.Extensions.DependencyInjection.Abstractions`が参照されていると、`ConfigureServices`メソッドが`ConsoleAppBuilder`から使えるようになりました。

```csharp
var app = ConsoleApp.Create()
    .ConfigureServices(service =>
    {
        service.AddTransient<MyService>();
    });

app.Add("", ([FromServices] MyService service, int x, int y) => Console.WriteLine(x + y));

app.Run(args);
```

NuGetの参照状況によってメソッドが増える！という新しいマジカル体験を提供します。これは`MetadataReferencesProvider`から引っ張ってきて生成処理に回しています。

```csharp
var hasDependencyInjection = context.MetadataReferencesProvider
    .Collect()
    .Select((xs, _) =>
    {
        var hasDependencyInjection = false;

        foreach (var x in xs)
        {
            var name = x.Display;
            if (name == null) continue;

            if (!hasDependencyInjection && name.EndsWith("Microsoft.Extensions.DependencyInjection.Abstractions.dll"))
            {
                hasDependencyInjection = true;
                continue;
            }

            // etc...
        }

        return new DllReference(hasDependencyInjection, hasLogging, hasConfiguration, hasJsonConfiguration, hasHostAbstraction, hasHost);
    });

context.RegisterSourceOutput(hasDependencyInjection, EmitConsoleAppConfigure);
```

`Microsoft.Extensions.Logging.Abstractions`が参照されていれば`ConfigureLogging`が使えるようになります。なので[ZLogger](https://github.com/Cysharp/ZLogger)と組み合わせれば


```csharp
// Package Import: ZLogger
var app = ConsoleApp.Create()
    .ConfigureLogging(x =>
    {
        x.ClearProviders();
        x.SetMinimumLevel(LogLevel.Trace);
        x.AddZLoggerConsole();
        x.AddZLoggerFile("log.txt");
    });

app.Add<MyCommand>();
app.Run(args);

// inject logger to constructor
public class MyCommand(ILogger<MyCommand> logger)
{
    public void Echo(string msg)
    {
        logger.ZLogInformation($"Message is {msg}");
    }
}
```

といったように、比較的すっきりと設定が統合できます。

`appsettings.json`から設定ファイルを引っ張ってくるというのも最近では定番パターンですが、これも`Microsoft.Extensions.Configuration.Json`を参照していると`ConfigureDefaultConfiguration`が使えるようになり、これは`SetBasePath(System.IO.Directory.GetCurrentDirectory())`と`AddJsonFile("appsettings.json", optional: true)`を自動的に行います（追加でActionでconfigureすることも可能、また、ConfigureEmptyConfigurationもあります）。

なのでコンフィグを読み込んでクラスにバインドしてコマンドにDIで渡す、などといった処理もシンプルに書けるようになりました。

```csharp
// Package Import: Microsoft.Extensions.Configuration.Json
var app = ConsoleApp.Create()
    .ConfigureDefaultConfiguration()
    .ConfigureServices((configuration, services) =>
    {
        // Package Import: Microsoft.Extensions.Options.ConfigurationExtensions
        services.Configure<PositionOptions>(configuration.GetSection("Position"));
    });

app.Add<MyCommand>();
app.Run(args);

// inject options
public class MyCommand(IOptions<PositionOptions> options)
{
    public void Echo(string msg)
    {
        ConsoleApp.Log($"Binded Option: {options.Value.Title} {options.Value.Name}");
    }
}
```

`Microsoft.Extensions.Hosting`でビルドしたい場合は、`ToConsoleAppBuilder`が、これも`Microsoft.Externsions.Hosting`を参照すると追加されるようになっています。

```csharp
// Package Import: Microsoft.Extensions.Hosting
var app = Host.CreateApplicationBuilder()
    .ToConsoleAppBuilder();
```

また、今回から設定されている`IServiceProvider`は`Run`または`RunAsync`終了後に自動的にDisposeするようになりました。

RegisterCommands from Attribute
---
コマンドの追加は`Add`または`Add<T>`が必要でしたが、クラスに属性を付与することで自動的に追加される機能をいれました。

```csharp
[RegisterCommands]
public class Foo
{
    public void Baz(int x)
    {
        Console.Write(x);
    }
}

[RegisterCommands("bar")]
public class Bar
{
    public void Baz(int x)
    {
        Console.Write(x);
    }
}
```

これらは自動で追加されています。

```
var app = ConsoleApp.Create();

// Commands:
//   baz
//   bar baz
app.Run(args);
```

これらとは別に追加で`Add`, `Add<T>`することも可能です。

なお、実装の当初予定では任意の属性を使えるようにする予定だったのですが、`IncrementalGenerator`のAPIの都合上難しくて、固定の`RegisterCommands`属性のみを対象としています。また、継承することもできません……。なので独自の処理用属性がある場合は、組み合わせてもらう必要があります。例えば以下のように。

```csharp
[RegisterCommands, Batch("0 10 * * *")]
public class MyCommands
{
}
```

この辺は[ConsoleAppFrameworkとAWS CDKで爆速バッチ開発](https://qiita.com/omt_teruki/items/dae315c7e86722fe05e6)を読んで、うーん、v5を使ってもらいたい！なんとかしたい！と思って色々考えたのですが、この辺が現状の限界でした……。名前変換オフりたいのもわかるー、とか今回の更新内容はこの記事での利用例を参考にさせていただきました、ありがとうございます！

まとめ
---
v5のリリース以降もフィルターを外部アセンブリに定義できるようになったり、Incremental Generatorの実装を見直して高速化するなど、Improvmentは続いています！非常に良いフレームワークに仕上がってきました！

ところで[System.CommandLine](https://github.com/dotnet/command-line-api/)、現状うまくいってないから[Resettting System.CommandLine](https://github.com/dotnet/command-line-api/issues/2338)だ！と言ったのが今年の3月。例によって想像通り進捗は無です。知ってた。そうなると思ってた。何も期待しないほうがいいし、普通にConsoleAppFramework使っていくで良いでしょう。