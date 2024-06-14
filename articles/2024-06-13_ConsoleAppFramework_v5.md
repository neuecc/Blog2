# ConsoleAppFramework v5 - ゼロオーバーヘッド・Native AOT対応のC#用CLIフレームワーク

[ConsoleAppFramework](https://github.com/Cysharp/ConsoleAppFramework)の完全に新しいバージョンをリリースしました。完全に設計しなおして実装も完全に作り直された、何もかもが新しいフレームワークになっています。設計指針として「Zero Dependency, Zero Overhead, Zero Reflection, Zero Allocation, AOT Safe」を掲げ、もちろん、他を圧倒的に引き離すパフォーマンスを実現しています。

![image](https://github.com/Cysharp/ConsoleAppFramework/assets/46207/db4bf599-9fe0-4ce4-801f-0003f44d5628)

これはコールドスタートアップ・ウォームアップなしでのベンチマークとなっていて、CLIアプリケーションでの実際での利用に最も即したものだと考えています。System.CommandLineと比較すれば280倍！メモリアロケーション量もほかのフレームワークの100~1000倍少なくなっています(表示されている400Bはほぼシステム自体のallocなのでフレームワーク自体は0です)。

このパフォーマンスは、全てをSource Generatorで生成することで実現しました。例えば以下のようなコード。

```csharp
using ConsoleAppFramework;

// args: ./cmd --foo 10 --bar 20
ConsoleApp.Run(args, (int foo, int bar) => Console.WriteLine($"Sum: {foo + bar}"));
```

ConsoleAppFrameworkはSource GeneratorがRunで与えられているラムダ式の引数を解析して、Runメソッドそのものを生成します。

```csharp
internal static partial class ConsoleApp
{
    // Generate the Run method itself with arguments and body to match the lambda expression
    public static void Run(string[] args, Action<int, int> command)
    {
        // code body
    }
}
```

通常C#のSource Generatorは属性をクラスかメソッドに与えて、それを元に生成されますが、ConsoleAppFrameworkはメソッドの呼び出しを監視して生成のキーにしています。これはRustのマクロから発想を得ていて、Rustには[Attribute-like macros and Function-like macros](https://doc.rust-lang.org/book/ch19-06-macros.html)といったような分類がありますが、今回のやりかたはFunction-likeなスタイルと言えるでしょう。

実際の生成されるコード全体は以下のようなものになります。

```csharp
internal static partial class ConsoleApp
{
    public static void Run(string[] args, Action<int, int> command)
    {
        if (TryShowHelpOrVersion(args, 2, -1)) return;

        var arg0 = default(int);
        var arg0Parsed = false;
        var arg1 = default(int);
        var arg1Parsed = false;

        try
        {
            for (int i = 0; i < args.Length; i++)
            {
                var name = args[i];

                switch (name)
                {
                    case "--foo":
                    {
                        if (!TryIncrementIndex(ref i, args.Length) || !int.TryParse(args[i], out arg0)) { ThrowArgumentParseFailed("foo", args[i]); }
                        arg0Parsed = true;
                        break;
                    }
                    case "--bar":
                    {
                        if (!TryIncrementIndex(ref i, args.Length) || !int.TryParse(args[i], out arg1)) { ThrowArgumentParseFailed("bar", args[i]); }
                        arg1Parsed = true;
                        break;
                    }
                    default:
                        // omit...(case-insensitive compare codes)
                        ThrowArgumentNameNotFound(name);
                        break;
                }
            }
            if (!arg0Parsed) ThrowRequiredArgumentNotParsed("foo");
            if (!arg1Parsed) ThrowRequiredArgumentNotParsed("bar");

            command(arg0!, arg1!);
        }
        catch (Exception ex)
        {
            Environment.ExitCode = 1;
            if (ex is ValidationException or ArgumentParseFailedException)
            {
                LogError(ex.Message);
            }
            else
            {
                LogError(ex.ToString());
            }
        }
    }

    static partial void ShowHelp(int helpId)
    {
        Log("""
Usage: [options...] [-h|--help] [--version]

Options:
  --foo <int>     (Required)
  --bar <int>     (Required)
""");
    }
}
```

特にひねりもなさそうなド直球ドシンプルなコードに見えるのではないでしょうか。それが大事です！単純なコードであればあるほど速い！フレームワークなのに単純、だから速い。というのが目指している姿です。余計なコードはいっさいなく、メソッド本体に全ての処理が集約されているので、フレームワークとしてゼロ・オーバーヘッド、最適化した手書きコードと同等の速度を実現しました。

CLIアプリケーションは通常、コールドスタートからの単発の実行になるため、動的コード生成(IL.EmitやExpression.Compile)やキャッシュ(ArrayPoolやDictionary生成による以降のマッチング高速化)が効きにくい分野です。それらを作ったほうがオーバーヘッドが大きいですから。かといってリフレクションなどをそのまま使うのは、それはそれで低速です。ConsoleAppFrameworkは全ての必要な処理をインライン生成することによって、単発実行での速度が圧倒的に高速化されています。

リフレクションもないのでNative AOTとの親和性も圧倒的に高く、コールドスタートアップ速度におけるC#の欠点は一切なくなります。

もう一つ特徴として、`ConsoleApp`クラスを含めて、全てがSource Generatorによって生成されるために、ConsoleAppFramework自体も含めて依存が全くありません。

コンソールアプリケーションを作るシチュエーションは多用です。多数の依存を持った大きなバッチアプリケーションの場合もあれば、超単機能の小さなコマンドの場合もあります。小さなコマンドを作りたい時には、少しも追加の依存を入れたくはないでしょう。それこそ `Microsoft.Extensions.Hosting` を参照すると、それだけで数十個の依存DLLが追加されてしまいます！ConsoleAppFrameworkなら、自身も含めて依存ゼロです。

依存ゼロの良いところは明らかにバイナリサイズが小さくなることです。特にNative AOTではバイナリサイズは気になるところですが、ConsoleAppFrameworkなら追加のコストはほぼゼロです。

そしてもちろん、単機能ではフレームワークとしては物足りない、ということで以下のような機能が実現されています。十分に充実した機能群は、他のフレームワークと比べても全く見劣りしないはずです。

* SIGINT/SIGTERM(Ctrl+C) handling with gracefully shutdown via `CancellationToken`
* Filter(middleware) pipeline to intercept before/after execution
* Exit code management
* Support for async commands
* Registration of multiple commands
* Registration of nested commands
* Setting option aliases and descriptions from code document comment
* `System.ComponentModel.DataAnnotations` attribute-based Validation
* Dependency Injection for command registration by type and public methods
* `Microsoft.Extensions`(Logging, Configuration, etc...) integration
* High performance value parsing via `ISpanParsable<T>`
* Parsing of params arrays
* Parsing of JSON arguments
* Help(`-h|--help`) option builder
* Default show version(`--version`) option

生成されるコードはモジュール化されていて、コードが使用する機能によって変化し、常にその機能の実現において最小のコードが生成されるようになっています。それにより多機能と高速さを両立しています。また、どの機能も最速で実行できるよう念入りに調整してあるため、全機能が有効化されてもなお、他とは比較にならないほどに高速です。

余談ですが、デリゲートはデリゲート生成というアロケーションがあります。つまり真のゼロアロケーション・ゼロオーバーヘッドじゃないじゃん、と言うことができます。しかし、ちゃんとConsoleAppFrameworkは真のゼロアロケーションを実現する仕組みもちゃんと用意されています。以下のように静的関数をfunction pointerとして渡してください。

```csharp
unsafe
{
    ConsoleApp.Run(args, &Sum);
}

static void Sum(int x, int y) => Console.Write(x + y);
```

すると、以下のような `delegate* managed<>` （あまり見慣れないと思いますが、managed function pointerという言語機能がC#には追加されているのです）の引数を持ったメソッドの実体を生成します。

```csharp
public static unsafe void Run(string[] args, delegate* managed<int, int, void> command)
```

これならもう完全に文句なくゼロアロケーション・ゼロオーバーヘッドです！

実用的には別にデリゲートでも全く関係ないレベルですが、完全に完璧を目指す執拗な姿勢により、対応を入れました。これでどの角度からも絶対に文句は付けられないでしょう。

高速な値変換
---
文字列からC#の値に変換する最速の手段はなんでしょうか？intだったら `int.TryParse` ですよね。では、他は？intは決め打ちだからいいとして、string -> T(あるいはobject)を汎用的にするには？というと少し難しい話になってきて、昔は[TypeConverter](https://learn.microsoft.com/ja-jp/dotnet/api/system.componentmodel.typeconverter?view=net-8.0)というものが使われてきました。もちろん、パフォーマンスは悪いです。

あるいは最近はJsonSerializerが標準搭載されているから、それに丸投げしてみるというのもアリでしょう。もちろん、パフォーマンスは決して良くはありません。特にコールドスタートアップで考えるとJsonSerializerのキャッシュ処理が必要になってきて、単発実行においてはかなりのオーバーヘッドが足されてしまいます。

ConsoleAppFrameworkでは[IParsable](https://learn.microsoft.com/en-us/dotnet/api/system.iparsable-1?view=net-8.0), [ISpanParsable](https://learn.microsoft.com/en-us/dotnet/api/system.ispanparsable-1?view=net-8.0)を採用しています。これは .NET 7から追加され、C# 11で追加されたstatic abstract interfaceが使用されています。

```csharp
public interface IParsable<TSelf> where TSelf : IParsable<TSelf>?
{
	static abstract TSelf Parse(string s, IFormatProvider? provider);
	static abstract bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(returnValue: false)] out TSelf result);
}
```

C# 11になってようやく汎用的な 「文字列 -> 値」変換処理が実現するようになったのです！ ConsoleAppFrameworkでは .NET 8/C# 12 を最小実行可能環境としているため、問答無用で採用しました。HalfやInt128などの .NET 8で登場した新しい型や、自分で定義する方も`IParsable<T>`を実装すればそれを使って高速に処理されます！

とはいえ、intなどの基本型はそもそもSource Generatorがintであることを知っているので、直接int.TryParseのように直接実行されるようになっていたりはします。

なお、値のバインディングに関してはparams arrayやデフォルト値にも対応しています。

```csharp
ConsoleApp.Run(args, (
    [Argument]DateTime dateTime,  // Argument
    [Argument]Guid guidvalue,     // 
    int intVar,                   // required
    bool boolFlag,                // flag
    MyEnum enumValue,             // enum
    int[] array,                  // array
    MyClass obj,                  // object
    string optional = "abcde",    // optional
    double? nullableValue = null, // nullable
    params string[] paramsArray 
    ) => { });
```

ちょうどC# 12から[ラムダ式にデフォルト値やparamsが使用できるようになりました](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/operators/lambda-expressions#input-parameters-of-a-lambda-expression)、ということが反映されています。

ドキュメントコメントによる定義
---
DescriptionやAliasの追加は、今までは、あるいは他のフレームワークでは属性を使って記述していました。しかし、それは少しメソッドの各パラメーターに属性、更にかなり長めの文字列を付与するのは、メソッドとしてかなり読みづらくなります。

そこでConsoleAppFrameworkではドキュメントコメントを活用することにしました。

```csharp
class Commands
{
    /// <summary>
    /// Display Hello.
    /// </summary>
    /// <param name="message">-m, Message to show.</param>
    public static void Hello(string message) => Console.Write($"Hello, {message}");
}
```

これは

```txt
Usage: [options...] [-h|--help] [--version]

Display Hello.

Options:
  -m|--message <string>    Message to show. (Required)
```

というコマンドになります。ドキュメントコメントであれば、多くの引数があっても自然な見た目を保つことが可能です。この手法が取れるのはSource Generatorで生成するため.xmlは不要でコードから直接読み取れることの強みでもありますね。（ただしSource Generatorでドキュメントコメントをあらゆる環境で読み取れるようにするには若干のハックが必要でした）

複数コマンドの追加
---
`ConsoleApp.Run`は単独コマンドのためのショートカットでしたが、複数のコマンドやネストされているサブコマンドの追加も可能です。例えば以下のような設定を行った場合の生成を例を見ていきます。

```csharp
var app = ConsoleApp.Create();

app.Add("foo", () => { });
app.Add("foo bar", (int x, int y) => { });
app.Add("foo bar barbaz", (DateTime dateTime) => { });
app.Add("foo baz", async (string foo = "test", CancellationToken cancellationToken = default) => { });

app.Run(args);
```

このコードのAddは、まず以下のように展開されます。Source Generatorが全てのAddされるラムダ式の型を知っているので、それぞれ固有の型を持ったフィールドに割り当てます。

```csharp
partial struct ConsoleAppBuilder
{
    Action command0 = default!;
    Action<int, int> command1 = default!;
    Action<global::System.DateTime> command2 = default!;
    Func<string, global::System.Threading.CancellationToken, Task> command3 = default!;

    partial void AddCore(string commandName, Delegate command)
    {
        switch (commandName)
        {
            case "foo":
                this.command0 = Unsafe.As<Action>(command);
                break;
            case "foo bar":
                this.command1 = Unsafe.As<Action<int, int>>(command);
                break;
            case "foo bar barbaz":
                this.command2 = Unsafe.As<Action<global::System.DateTime>>(command);
                break;
            case "foo baz":
                this.command3 = Unsafe.As<Func<string, global::System.Threading.CancellationToken, Task>>(command);
                break;
            default:
                break;
        }
    }
}
```

これによりDelegateを保持しておくための配列や、DelegateのままInvokeするリフレクション/ボクシングが防げています。

Runでは、`string[] args`からコマンドを選択するために定数文字列のswitchが埋め込まれます。

```csharp
partial void RunCore(string[] args)
{
    if (args.Length == 0)
    {
        ShowHelp(-1);
        return;
    }
    switch (args[0])
    {
        case "foo":
            if (args.Length == 1)
            {
                RunCommand0(args, args.AsSpan(1), command0);
                return;
            }
            switch (args[1])
            {
                case "bar":
                    if (args.Length == 2)
                    {
                        RunCommand1(args, args.AsSpan(2), command1);
                        return;
                    }
                    switch (args[2])
                    {
                        case "barbaz":
                            RunCommand2(args, args.AsSpan(3), command2);
                            break;
                        default:
                            RunCommand1(args, args.AsSpan(2), command1);
                            break;
                    }
                    break;
                case "baz":
                    RunCommand3(args, args.AsSpan(2), command3);
                    break;
                default:
                    RunCommand0(args, args.AsSpan(1), command0);
                    break;
            }
            break;
        default:
            ShowHelp(-1);
            break;
    }
}
```

C#で文字列から特定のコードにジャンプする最速の手段は、switchで文字列定数を使うことです。展開されるアルゴリズムは何度か修正されていて、C# 12では[Performance: faster switch over string objects · Issue #56374 · dotnet/roslyn](https://github.com/dotnet/roslyn/issues/56374)として、まず長さをチェックした後に、差が存在する1文字だけを絞るといった形でマッチさせます。

`Dictionary<string, T>`からのマッチなどよりも高速で初期化時間もアロケーションもないのが、C#コンパイラの助けを借りれる強みであり、そうした処理ができるのはC#コードそのものを出力するSource Generator方式だけです。なので絶対に最速なわけです。

DIとCancellationTokenとライフタイム
---
引数にはコマンドのパラメーターとして有効になるもの以外に、DI経由で渡したいもの(例えば`ILogger<T>`や`Option<T>`など)や、特別扱いする型として`ConsoleAppContext`と`CancellationToken`を定義することができます。

DIによる受取は、コンソールアプリケーションがASP.NETのプロジェクトなどと設定ファイルを共有したいようなシチュエーションで有効でしょう。そうした場合のために、 Microsoft.Extensions.Hosting と連動させることが可能です。

また、`CancellationToken`を渡した場合は、SIGINT/SIGTERM/SIGKILL(Ctrl+C)をフックするコンソールアプリケーションとしてのライフタイム管理が働くようになります。

```csharp
await ConsoleApp.RunAsync(args, async (int foo, CancellationToken cancellationToken) =>
{
    await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
    Console.WriteLine($"Foo: {foo}");
});
```

上記のコードは以下のように展開されます。

```csharp
using var posixSignalHandler = PosixSignalHandler.Register(ConsoleApp.Timeout);
var arg0 = posixSignalHandler.Token;

await Task.Run(() => command(arg0!)).WaitAsync(posixSignalHandler.TimeoutToken);
```

.NET 6から追加された[PosixSignalRegistration](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.posixsignalregistration?view=net-8.0)を使って、SIGINT/SIGTERM/SIGKILLがフックし、CancellationTokenをキャンセルの状態にします。と同時に、即時終了を抑制します(通常Ctrl + Cを押すと即座にAbortされますが、Abortされなくなります)。

それによりアプリケーションがCancellationTokenを正常にハンドリングする余地を残しています。

ただしCancellationTokenをハンドリングしないと終了命令を無視するだけになってしまい、それはそれで困るので、強制的に終了するタイムアウト時間が設けられています。デフォルトでは5秒に設定されていますが、これは `ConsoleApp.Timeout` プロパティで自由に変更できます。もし強制終了をオフにしたい場合は `ConsoleApp.Timeout = Timeout.InfiniteTimeSpan` を指定すると良いでしょう。

[Task.WaitAsync](https://learn.microsoft.com/ja-jp/dotnet/api/system.threading.tasks.task.waitasync?view=net-8.0)は .NET 6 からです。TimeSpanを渡す以外に、CancellationTokenを渡すことも可能なので、単純な数秒後ではなく、WaitAsyncの発火するタイミングをPosixSignalRegistrationが発火した後にTimeout後、といった条件を作ることができました。

フィルターパイプライン
---
実行の前後をフックする仕組みとしてConsoleAppFrameworkではFilterを採用しています。ミドルウェアパターンとも呼ばれて、特にasync/awaitが使える言語ではよく見かけるパターンだと思います。

```csharp
internal class NopFilter(ConsoleAppFilter next) : ConsoleAppFilter(next) // ctor needs `ConsoleAppFilter next` and call base(next)
{
    // implement InvokeAsync as filter body
    public override async Task InvokeAsync(ConsoleAppContext context, CancellationToken cancellationToken)
    {
        try
        {
            /* on before */
            await Next.InvokeAsync(context, cancellationToken); // invoke next filter or command body
            /* on after */
        }
        catch
        {
            /* on error */
            throw;
        }
        finally
        {
            /* on finally */
        }
    }
}
```

この設計パターンは本当に優れていて、実行をフックしたいような仕組みを用意したい場合は、このパターンを採用することを絶対にお薦めします。GoFの時代にasync/awaitがあったら、重要なデザインパターンとして載っていたことでしょう。

ReadMeにはフィルターでできることとして、実行時間のロギング・ExitCodeのカスタマイズ・多重実行禁止・認証処理などを紹介しています。`Task InvokeAsync`一つで様々な処理を実現できる素晴らしさ。誰がこのパターンを最初に発見したんでしょうね？

フィルターの設計にも色々な手法があるのですが、ConsoleAppFrameworkでは最もパフォーマンスの出る方法を選びました。コンストラクターでNextを受け取ることと、コードジェネレート時に静的に全ての利用するフィルターが決定するので（動的な追加は許可していません）、全てを埋め込んで組み立てています。

```csharp
app.UseFilter<NopFilter>();
app.UseFilter<NopFilter>();
app.UseFilter<NopFilter>();
app.UseFilter<NopFilter>();
app.UseFilter<NopFilter>();

// The above code will generate the following code:

sealed class Command0Invoker(string[] args, Action command) : ConsoleAppFilter(null!)
{
    public ConsoleAppFilter BuildFilter()
    {
        var filter0 = new NopFilter(this);
        var filter1 = new NopFilter(filter0);
        var filter2 = new NopFilter(filter1);
        var filter3 = new NopFilter(filter2);
        var filter4 = new NopFilter(filter3);
        return filter4;
    }

    public override Task InvokeAsync(ConsoleAppContext context, CancellationToken cancellationToken)
    {
        return RunCommand0Async(context.Arguments, args, command, context, cancellationToken);
    }
}
```

これにより、中間の配列のアロケーションや、ラムダ式のキャプチャのアロケーションは発生せず、フィルターの個数 + 1(メソッド本体のラップ)の追加のアロケーションのみが追加のコストとなります。また、戻り値のTaskは、同期的に完了する場合はTask.Completed相当のものが使われることになるため、これをValueTaskにする必要はありません。

コンストラクターでNextを受け取ってbaseに渡すだけのコードも、primary constructorのお陰で簡単に書けるようになりました。

コマンドライン引数の構文について
---
コマンドライン引数はスペース区切りで`string[] args`に渡されるということ以外は、完全に自由です。なんとなく `--`や`-`がパラメーター識別子だと思われていますが、実際はなんでもいいし、なんだったらWindowsは`/`が使われることも多かった。

とはいえ、ある程度標準的なルールは存在します。代表的なものは[POSIX規格](https://pubs.opengroup.org/onlinepubs/9699919799/basedefs/V1_chap12.html)と、その拡張である[GNU Coding Standards](https://www.gnu.org/prep/standards/html_node/Command_002dLine-Interfaces.html)でしょうか。ConsoleAppFrameworkでも、POSIX規格にある程度は従いつつ、GNU Coding Stadardsで定義されている `--version` と `--help` を組み込みのオプションとしています。名前も `--lower-kebab-case` がデフォルトです。

「ある程度」というのは、つまり、完全に従っているというわけではありません。規格にせよ伝統的な慣習にせよ、古いルールは現代的な観点から許容すべきでないルールも少なくありません。例えば`-x`と`-X`が区別されて異なる挙動をするというのは絶対にナシでしょう。あるいは広く使われているものでもバンドリング、`-fdx`は`-f`, `-d`, `-x`と解釈されるといったものも、あまり良いとは思えません。バンドリングに関しては、パフォーマンス上でも、パース処理を複雑化させるため問題があります。

ConsoleAppFrameworkで優先しているのはパフォーマンスであるため、パフォーマンス上問題を引き起こす可能性のあるルールに関しては採用していません。大文字小文字の区別はしないようにしていますが、これは小文字のマッチングを先に行った後、フォールバックとしてcase-insensitiveのマッチングを行うため、実用上のパフォーマンスの低下は起こらないと考えています。

[System.CommandLine のコマンド ライン構文の概要 - .NET | Microsoft Learn](https://learn.microsoft.com/ja-jp/dotnet/standard/commandline/syntax)を見ると、System.CommandLineがかなり柔軟な構文解釈を可能にしていることがわかるでしょう。それはとても良いことです！良いことではあるのですが、パフォーマンス劣化を引き起こしているなら問題です。そして実際、System.CommandLineの性能はベンチマーク結果から明らかなとおり、非常に悪い。これはちょっといただけません。

迷走を続けている[System.CommandLine](https://github.com/dotnet/command-line-api)は、どうやら再度分解されて実装を変更するようです。[Resetting System.CommandLine](https://github.com/dotnet/command-line-api/issues/2338)ということで、POSIX規格のパーサーとしての小さなコアを.NET 9 あるいは .NET 10で標準採用されることを目指している、ようです。

もしそれらが標準採用されたとしても、パフォーマンスの観点からは、ConsoleAppFrameworkを超えることは絶対にないでしょう。

v4からの互換性について
---
破壊的変更！破壊的変更を厭わないことはいいことです、イノベーションを妨げない、常に先端的であり続けるために必要なことです。C#の先端を走り続けるのはCysharpのアイデンティティでもあります。と、同時に、もちろん大迷惑なことです。今回の v4 -> v5 に関しては .NET Frameworkから.NET Coreに変わったような、 ASP.NET から ASP.NET Coreに変わったような、そんな変革なのでしょうがない、どうしても必要な変化だったのだ……。

ただし、実際のところは別にそこまで大きく変わっているわけではなかったりもします。名前変換処理(lower-kebab-case)のロジックは同じものを使っているため、名前がズレてしまうといったこともないので、コンパイルエラー出たメソッド名をマッピングするだけ、ではあります。そのぐらいのことはよくある、よね？

```csharp
var app = ConsoleApp.Create(args); app.Run(); -> var app = ConsoleApp.Create(); app.Run(args);
app.AddCommand/AddSubCommand -> app.Add(string commandName)
app.AddRootCommand -> app.Add("")
app.AddCommands<T> -> app.Add<T>
app.AddSubCommands<T> -> app.Add<T>(string commandPath)
app.AddAllCommandType -> NotSupported(use Add<T> manually)
[Option(int index)] -> [Argument]
[Option(string shortName, string description)] -> Xml Document Comment
ConsoleAppFilter.Order -> NotSupported(global -> class -> method declrative order)
ConsoleAppOptions.GlobalFilters -> app.UseFilter<T>
```

全体的には、より単純化された、ようするに「良くなった」と思ってもらえる仕様変更だとは思います。

また、標準で `Microsoft.Extensions.Hosting` に乗っからなくなったというのは大きな違いですが、これは一行追加するだけで解決します。Hostingの上に乗っかるというのは、つまりはHostingで生成するServiceProviderを使う、それだけのことなのだ、と。実際はLifetime管理もありますが、それはConsoleAppFrameworkが自前でやっているので、DIのためのServiceProviderだけ渡してやれば実用上の違いはありません。

```csharp
using var host = Host.CreateDefaultBuilder().Build(); // use using for host lifetime
ConsoleApp.ServiceProvider = host.ServiceProvider;
```

v4では`ConsoleAppBase`を継承させていましたが、v5ではPOCOでよくなりました。代わりに`ConsoleAppContext`や`CancellationToken`に関してはコンストラクタインジェクションで受け取ってください。これも、C# 12のprimary constructorのお陰でそんなに手間じゃなくなりました。これもベースクラスを必要とする仕組みをやめた理由の一つになります。

真のIncremental Generator
---
Incremental Generatorって、ただたんに何も考えずに作るとIncrementalにならないのです。というのは知識として知ってはいたのですが、今まで見て見ぬふりをしていました！ありがたいことに指摘が入ったので、重い腰を上げてちゃんと抜本的な対応を取ることにしました。

まず最初にやらなければならないのは、Incrementalであるかどうかを視認できるようにすることです。普通に動かしていても内部状態は全く見えないので、ユニットテストで状態をチェックできるようにすることが大事です。例えばこんなユニットテストが書かれています。

```csharp
    [Fact]
    public void RunLambda()
    {
        var step1 = """
using ConsoleAppFramework;

ConsoleApp.Run(args, int () => 0);
""";

        var step2 = """
using ConsoleAppFramework;

ConsoleApp.Run(args, int () => 100); // body change

Console.WriteLine("foo"); // unrelated line
""";

        var step3 = """
using ConsoleAppFramework;

ConsoleApp.Run(args, int (int x, int y) => 100); // change signature

Console.WriteLine("foo");
""";

        var reasons = CSharpGeneratorRunner.GetIncrementalGeneratorTrackedStepsReasons("ConsoleApp.Run.", step1, step2, step3);

        reasons[0][0].Reasons.Should().Be("New");
        reasons[1][0].Reasons.Should().Be("Unchanged");
        reasons[2][0].Reasons.Should().Be("Modified");

        VerifySourceOutputReasonIsCached(reasons[1]);
        VerifySourceOutputReasonIsNotCached(reasons[2]);
    }
```

Incremental Generatorは `trackIncrementalGeneratorSteps: true` というオプションを渡してDriverを動かすと、各ステップの状態の結果が見えるようになります。`IncrementalStepRunReason`には`New`, `Unchanged`, `Modified`, `Cached`, `Removed` という状態があり、最終出力の手前が`Unchanged`か`Cached`なら、出力処理がスキップされます。

上のユニットテストではstep2では出力コードに変更のない箇所に変更が加わっただけなので、Unchangedです。なので最終段ではCachedになっていました。step3は再生成が必要な変更が加わっているのでModifiedとなり、ソースコード生成処理まで走ります。

`IncrementalStepRunReason`は`TrackedSteps`から取り出すことが出来るのですが、そのままだとちょっと読みづらすぎるので、確認しやすいように整形しています、というのが`GetIncrementalGeneratorTrackedStepsReasons`というユーティリティメソッドです。

```csharp
public static (string Key, string Reasons)[][] GetIncrementalGeneratorTrackedStepsReasons(string keyPrefixFilter, params string[] sources)
{
    var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp12); // 12
    var driver = CSharpGeneratorDriver.Create(
        [new ConsoleAppGenerator().AsSourceGenerator()],
        driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true))
        .WithUpdatedParseOptions(parseOptions);

    var generatorResults = sources
        .Select(source =>
        {
            var compilation = baseCompilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(source, parseOptions));
            driver = driver.RunGenerators(compilation);
            return driver.GetRunResult().Results[0];
        })
        .ToArray();

    var reasons = generatorResults
        .Select(x => x.TrackedSteps
            .Where(x => x.Key.StartsWith(keyPrefixFilter) || x.Key == "SourceOutput")
            .Select(x =>
            {
                if (x.Key == "SourceOutput")
                {
                    var values = x.Value.Where(x => x.Inputs[0].Source.Name?.StartsWith(keyPrefixFilter) ?? false);
                    return (
                        x.Key,
                        Reasons: string.Join(", ", values.SelectMany(x => x.Outputs).Select(x => x.Reason).ToArray())
                    );
                }
                else
                {
                    return (
                        Key: x.Key.Substring(keyPrefixFilter.Length),
                        Reasons: string.Join(", ", x.Value.SelectMany(x => x.Outputs).Select(x => x.Reason).ToArray())
                    );
                }
            })
            .OrderBy(x => x.Key)
            .ToArray())
        .ToArray();

    return reasons;
}
```

ごちゃごちゃしてよくわからないという感じですが、つまりそのままだと本当によくわからない代物ということで。Keyに関しては各ステップで `.WithTrackingName("ConsoleApp.Run.0_CreateSyntaxProvider")` のような命名規則で付与しています。TrackedStepsが`ImmutableDictionary`のため列挙の順番が順不同でイマイチ確認しづらいので、番号振ってソートするようにしました。また、複数のRegisterSourceOutputが走っていると(ConsoleAppFrameworkではRun系とBuilder系の2種が動いてる)混線してわかりづらくなるため、keyPrefixとしてフィルタリングするようにしています。

注意すべき点とか、いい感じに作る方法とか、色々説明しておかなければならないことが多いのですが、めちゃくちゃ長くなるので、それはまたの機会ということで……！

まとめ
---
もともとConsoleAppFrameworkはCysharpの製品ラインでは珍しく、パフォーマンスを重視していたわけではない、という成り立ちがあります。どちらかというと機能面、当時それなりに珍しかったHostingと融合してCLIフレームワークを作るといったコンセプトの立証を主軸に作り上げ、そして一定の成果を挙げました。何回かの改修でHelpがリッチになったりMinimal APIっぽく書けるようになったりもしましたが、どうしても古くささが目立ってきました。

特に[Cocona](https://github.com/mayuki/Cocona)は、ConsoleAppFrameworkの影響を受けつつも、より柔軟で、より強力な機能を備えていてとても素晴らしいライブラリです。このままではConsoleAppFrameworkはただの劣化版ではないか、という意識もありました。自信をもってベストであると薦められないのは心苦しい。というかCoconaを作っているのはCysharpの同僚ですしですの。

なので、今回APIの幾つかは逆にCoconaからの影響を受けつつ(`[Argument]`など)、全く異なるキャラクターを持ったフレームワークとなるように腐心しました。パースについての項目で説明したように、ConsoleAppFramework v5は柔軟性をある程度犠牲にしているため、豊富な機能が必要ならば、System.CommandLineやCoconaを使用することをお薦めします。

また、パフォーマンスの観点から言うと、本体の実行時間が長ければ長いほどフレームワークのオーバーヘッドなんてどうでもよくはなります。10分、1分、いや、10秒ぐらいかかる処理であるなら、フレームワーク部分が1msだろうと50msだろうと誤差みたいなものでしょう。それはそもそもJITコンパイルにも言えることではありますが。とはいえ、Native AOTだのコールドスタートアップ速度だのがやいやい言われる昨今では、別にそんなもの無視できる程度の話だろう、と一刀両断できるわけでもなく、早いに越したことはないのは間違いないとも言えます。

パフォーマンスや依存性なしといったメリットはもちろんですが、アプローチや設計面でも特異で面白いものになっていると思いますので、是非お試しください！もちろん、実用性もめちゃくちゃ高く、文句なしに必須ライブラリと考えてもらってもいいのではないでしょうか！