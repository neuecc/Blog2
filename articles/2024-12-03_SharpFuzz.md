# Fuzzing in .NET: Introducing SharpFuzz

この記事は[C# Advent Calendar 2024](https://qiita.com/advent-calendar/2024/csharplang)に参加しています。また、先月開催された[dotnet new](https://dotnetnew.connpass.com/event/335955/)というイベントでの発表のフォローアップ、のつもりだったのですがコロナ感染につき登壇断念……。というわけで、セッション資料はないので普通にブログ記事とします！

dotnet/runtime と Fuzzing
---
今年に入ってからdotnet/runtimeにFuzzingテストが追加されています。[dotnet/runtime/Fuzzing](https://github.com/dotnet/runtime/tree/main/src/libraries/Fuzzing)。というわけで、実はfuzzingは非常に最近のトピックスなのです……！

[ファジング](https://ja.wikipedia.org/wiki/%E3%83%95%E3%82%A1%E3%82%B8%E3%83%B3%E3%82%B0)とはなんなのか、ザックリとはランダムな入力値を大量に投げつけることによって不具合や脆弱性を発見するためのテストツールです。エッジケースのテスト、やはりどうしても抜けちゃいがちだし、ましてや脆弱性になりうる絶妙な不正データを人為的に作るのも難しいので、ここはツール頼みで行きましょう。

Goでは1.18(2022年)から標準でgo fuzzコマンドとして追加されたらしいので、
[Go1.18から追加されたFuzzingとは](https://future-architect.github.io/articles/20220214a/)のような解説記事を読むのもイメージを掴みやすいです。

さて、dotnet/runtimeのFuzzingでは現状

* AssemblyNameInfoFuzzer 
* Base64Fuzzer
* Base64UrlFuzzer 
* HttpHeadersFuzzer 
* JsonDocumentFuzzer 
* NrbfDecoderFuzzer 
* SearchValuesByteCharFuzzer
* SearchValuesStringFuzzer 
* TextEncodingFuzzer
* TypeNameFuzzer 
* UTF8Fuzzer 

というのものが用意されてます。わかるようなわからないような。だいたいデータのパース系によく使われるものなので、その通りのところに用意されています。一番わかりやすいJsonDocumentFuzzerを見てみましょう。

```csharp
internal sealed class JsonDocumentFuzzer : IFuzzer
{
    public string[] TargetAssemblies { get; } = ["System.Text.Json"];
    public string[] TargetCoreLibPrefixes => [];
    public string Dictionary => "json.dict";

    // fuzzerからのランダムなバイト列が入力
    public void FuzzTarget(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
        {
            return;
        }

        // The first byte is used to select various options.
        // The rest of the input is used as the UTF-8 JSON payload.
        byte optionsByte = bytes[0];
        bytes = bytes.Slice(1);

        var options = new JsonDocumentOptions
        {
            AllowTrailingCommas = (optionsByte & 1) != 0,
            CommentHandling = (optionsByte & 2) != 0 ? JsonCommentHandling.Skip : JsonCommentHandling.Disallow,
        };

        using var poisonAfter = PooledBoundedMemory<byte>.Rent(bytes, PoisonPagePlacement.After);

        try
        {
            // それをParseに投げて、もし不正な例外が来たらなんかバグっていたということで
            JsonDocument.Parse(poisonAfter.Memory, options);
        }
        catch (JsonException) { }
    }
}
```

ようは想定外のデータ入力で`JsonDocument.Parse`が失敗しないことを祈る、といったものですね。正常に認識しているinvalidな値なら`JsonException`をthrowするはずですが、`ArgumentException`とか`StackOverflowException`とかが出てきちゃった場合は認識できていない不正パターンなので、ちゃんとしたハンドリングが必要になってきます。

では、これを参考にやっていきましょう、とはなりません。えー。まず、dotnet/runtimeのFuzzingではSharpFuzz, libFuzzer, そしてOneFuzzが使用されていると書いてあるのですが、OneFuzzはMicrosoft内部ツールなので外部では使用できません。正確には[2020年にオープンソース公開](https://www.publickey1.jp/blog/20/project_onefuzzwindowsmicorosoft_edge.html)したものの、[2023年にはクローズドに戻している](https://github.com/microsoft/onefuzz)状態です。まぁ事情は色々ある。しょーがない。

というわけで、これはMicrosoft内部で動かすためのOneFuzzや、dotnet/runtimeで動かすために調整してある`IFuzzer`といったフレームワーク部分が含まれているので、小規模な自分たちのコードをfuzzingするにあたっては、不要ですし、ぶっちゃけあまり参考にはなりません！解散！

Introducing SharpFuzz
---
そんなわけでdotnet/runtimeのFuzzingでも使われている[Metalnem/sharpfuzz: AFL-based fuzz testing for .NET](https://github.com/Metalnem/sharpfuzz)を直接使っていきます。sharpfuzzは[afl-fuzz](https://lcamtuf.coredump.cx/afl/)と連動して動くように作られている .NETライブラリです。3rd Partyライブラリですが作者はMicrosoftの人です（dotnet/runtimeで採用されている理由でもあるでしょう）。ReadMeのTrophiesでは色々なもののバグを見つけてやったぜ、と書いてあります。AngleSharpとかGoogle.ProtobufとかGraphQL-ParserとかMarkdigとかMessagePack for C#とImageSharpとか。まぁ、やはり用途としてはパーサーのバグを見つけるのには適切、という感じです。

AFL(American Fuzzy Lop)ってなに？ということなのですが、そもそもファジングの「ランダムな入力値を大量に投げつける」行為は、完全なランダムデータを投げつけていくわけではありません。完全ランダムだとあまりにも時間がかかりすぎるため、脆弱性発見において実用的とは言えない。そこでAFLはシード値からのミューテーションと、カバレッジをトレースしながら効率よくデータを生成していきます。Wikipediaから引用すると

> テスト対象のプログラム（テスト項目）のソースコードをインストルメント化することにより、afl-fuzzは、ソフトウェアのどのブロックが特定のテスト刺激で実行されたかを後で確認できる。そのため、AFLはグレーボックステストに使用することができる。遺伝的手法による検査データの生成に関連して、ファザーはテストデータをより適切に生成できるため、このメソッドを使用しない他のファザーよりも、処理中に以前は使用されていなかったコードブロックが実行される。その結果、コードカバレッジは比較的短い時間で比較的高い結果が得られる。この方法は、生成されたデータ内の構造を独立して（つまり、事前の情報なしで）生成することができる。このプロパティは、テストカバレッジの高いテストコーパス（テストケースのコレクション）を生成するためにも使用される。

というわけでdotnet testのようにテストコードを渡したら全自動でやってくれる、というほど甘くはなくて、多少の下準備が必要になってきます。SharpFuzzは一連の処理をある程度やってくれるようにはなっていますが、そもそもに実行までに二段階の処理が必要になっています。

* sharpfazzコマンド(dotnet tool)でdllにトレースポイントを注入する
* その注入されたdll(とexe)をネイティブのfuzzing実行プロセス(afl-fuzzなど)に渡す

dllにトレースポイントを注入はお馴染みの[Cecil](https://github.com/jbevain/cecil)でビルド済みのDLLのILを弄ってトレースポイントを仕込みます。

![image](https://github.com/user-attachments/assets/c3b43b60-8526-44cd-8482-6f1185206b65)

これは注入済みのdllですが、Trace.SharedMemとかTrace.PrevLocationとか、分岐点に対して明らかに注入している様が見えます。そうしたトレースポイントとの通信や実行データ生成などは外部プロセスが行うので、SharpFuzzというライブラリは、それ自体は実行ツールではなくて、それらとの橋渡しをするためのシステムということです。

ではやっていきましょう！色々なシステムが絡んでくる分、ちょっとややこしく面倒くさいのと、ReadMeの例をそのままやると罠が多いので、少しアレンジしていきます。

まずはRequirementsですが、実行機であるAFLがWindowsでは動きません(Linux, macOSでは動く)。なのでWSL上で動かしましょうという話になってくるのですが、それはあんまりにもやりづらいので、[libFuzzer](https://llvm.org/docs/LibFuzzer.html)というLLVMが開発しているAFL互換のFuzzingツールを使っていくことにします。これはWindowsでビルドできます。

自分でビルドする必要はなく、SharpFuzzの作者が連携して使うことを意識して用意してくれている[libfuzzer-dotnetのReleasesページ](https://github.com/Metalnem/libfuzzer-dotnet/releases)から、バイナリを直接落としてきましょう。`libfuzzer-dotnet-windows.exe`です。

次に、IL書き換えを行うツール`SharpFuzz.CommandLine`を .NET toolで入れていきましょう。これはglobalでいいかな、と思います。

```
dotnet tool install --global SharpFuzz.CommandLine
```

次に、今回は[Jil](https://github.com/kevin-montrose/Jil)という、今はもうあまり使われることもないJsonシリアライザーをターゲットとしてやっていこうということなので、JilとSharpFuzzをインストールします。

```
dotnet add package Jil --version 2.15.4
dotnet add package SharpFuzz
```

ここで注意が必要なのは、Jilの最新バージョンはSharpFuzzにより発見されたバグが修正されているので、最新版を入れるとチュートリアルにはなりません！というわけでここは必ずバージョン下げて入れましょう。

新規のConsoleApplicationで、コードは以下のようにします。

```csharp
using Jil;
using SharpFuzz;

// 実行機としてlibFuzzerを使う(引数はReadOnlySpan<byte>)
Fuzzer.LibFuzzer.Run(span =>
{
    try
    {
        using var stream = new MemoryStream(span.ToArray());
        using var reader = new StreamReader(stream);
        JSON.DeserializeDynamic(reader); // このメソッドが正しく動作してくれるかをテスト
    }
    catch (Jil.DeserializationException)
    {
        // Jil.DeserializationExceptionは既知の例外（正しくハンドリングできてる）なので握り潰し
        // それ以外の例外が発生したらルート側にthrowされて問題が検知される
    }
});
```

今度はベースになるテストデータを用意します。名前とかはなんでもいいんですが、`Testcases`フォルダに`Test.json`を追加しました。

![image](https://github.com/user-attachments/assets/606cede7-9a20-4efe-8e58-642330ced8d5)

```json
{"menu":{"id":1,"val":"X","pop":{"a":[{"click":"Open()"},{"click":"Close()"}]}}}
```

このデータを元にしてfuzzerは値を変形させていくことになります。

では実行しましょう！実行するためには、ビルドしてILポストプロセスしてlibFuzzer経由で動かす……。という一連の定型の流れが必要になるため、作者の用意してくれているPowerShellスクリプト[fuzz-libfuzzer.ps1](https://raw.githubusercontent.com/Metalnem/sharpfuzz/master/scripts/fuzz-libfuzzer.ps1)をダウンロードしてきて使いましょう。

とりあえず`fuzz-libfuzzer.ps1`と`libfuzzer-dotnet-windows.exe`をcsprojと同じディレクトリに配置して、以下のコマンドを実行します。`ConsoleApp24.csproj`の部分だけ適当に変えてください。

```cmd
PowerShell -ExecutionPolicy Bypass ./fuzz-libfuzzer.ps1 -libFuzzer "./libfuzzer-dotnet-windows.exe" -project "ConsoleApp24.csproj" -corpus "Testcases"
```

動かすと、見つかった場合はいい感じに止まってくれます。

![image](https://github.com/user-attachments/assets/1ce45aa1-2d50-46f2-8f86-947db39406d6)

なお、見つからなかった場合は無限に探し続けるので、なんとなくもう見つかりそうにないなあ、と思ったら途中で自分でとめる(Ctrl+C)必要があります。

Testcasesには途中の残骸と、クラッシュした場合は`crash-id`でクラッシュ時のデータが拾えます。

![image](https://github.com/user-attachments/assets/d90f5bb1-4509-41b6-a139-16789a5a501c)

今回見つかったクラッシュデータは

```json
{"menu":{"id":1,"val":"X","popid":1,"val":"X","pop":{"a":[{"click":"Open()"},{"c
```

でした。実際このデータを使って再現できます。

```csharp
using Jil;

//  クラッシュファイルのプロパティでデータはCopy to Output Directoryしてしまう
//  <None Update="crash-c57462e70fb60e86e8c41cd18b70624bd1e89822">
//    <CopyToOutputDirectory>Always</CopyToOutputDirectory>
//  </None>
var crash = File.ReadAllBytes("crash-c57462e70fb60e86e8c41cd18b70624bd1e89822");
var span = crash.AsSpan();

// Fuzzing時と同じコード
using var stream = new MemoryStream(span.ToArray());
using var reader = new StreamReader(stream);
JSON.DeserializeDynamic(reader);
```

以上！完璧！便利！一度手順を理解してしまえば、そこまで難しいことではないので、是非ハンズオンでやってみることをお薦めします。なお、ps1のスクリプトは実行対象自身へのインジェクトは除外されるようになっているので、小規模な自分のコードでfuzzingを試してみたいと思った場合は、対象コードはexeとは異なるプロジェクトに分離しておく必要があります。

ところで、AFLにはdictionaryという仕組みがあり、既知のキーワード集がある場合は生成速度を大幅に上昇させることが可能です。例えば[json.dict](https://github.com/AFLplusplus/AFLplusplus/blob/stable/dictionaries/json.dict)を使う場合は

```cmd
PowerShell -ExecutionPolicy Bypass ./fuzz-libfuzzer.ps1 -libFuzzer "./libfuzzer-dotnet-windows.exe" -project "ConsoleApp24.csproj" -corpus "Testcases" -dict ./json.dict
```

のように指定します。JSONとかYAMLとかXMLとかZipとか、一般的な形式は[AFLplusplus/dictionaries](https://github.com/AFLplusplus/AFLplusplus/tree/stable/dictionaries)などに沢山転がっています。独自に作ることも可能で、例えばdotnet/runtimeのFuzzingではBinaryFormatterのテストが置いてありますが、これは[NRBF(.NET Remoting Binary Format)](https://learn.microsoft.com/ja-jp/dotnet/standard/serialization/binaryformatter-migration-guide/read-nrbf-payloads)の辞書、[nrbfdecoder.dict](https://github.com/dotnet/runtime/blob/main/src/libraries/Fuzzing/DotnetFuzzing/Dictionaries/nrbfdecoder.dict)を用意しているようでした。

もちろん、なしでも動かすことはできますが、用意できそうなら用意しておくとよいでしょう。

まとめ
---
[MemoryPack](https://github.com/Cysharp/MemoryPack)でも実際バグ見つかってたりするので、この手のライブラリを作る人だったら覚えておいて損はないです。シリアライザーに限らずパーサーに関わるものだったらネットワークプロトコルでも、なんでも適用可能です。ただし現状、入力が`byte[]`に制限されているので、応用性自体はあるようで、なかったりはします。これがintとか受け入れてくれると、様々なメソッドに対してカジュアルに使えて、より便利な気もしますが……(実際go fuzzは`byte[]`だけじゃなくて基本的なプリミティブの生成に対応している)

`byte[]`列から適当に切り出してintとして使う、といったような処理だと、ミューテーションやカバレッジの関係上、適切な値を取得しにくいので、あまりうまくやれません。libFuzzerでは[Structure-Aware Fuzzing with libFuzzer](https://github.com/google/fuzzing/blob/master/docs/structure-aware-fuzzing.md)といったような手法が考案されていて、protocol buffersの構造を与えるとか、gRPCの構造を与えるとかでうまく活用している事例はあるようです。この辺はSharpFuzzの対応次第となります(いつかやりたい、とは書いてありましたが、現実的にいつ来るかというと、あまり期待しないほうが良いでしょう)

Rustにも[cargo fuzz](https://github.com/rust-fuzz/cargo-fuzz)といったcrateがあり、それなりに使われているようです。

Fuzzingは適用範囲が限定的であることと下準備の手間などがあり、一般的なアプリケーション開発者においては、あまりメジャーなテスト手法ではないというのが現状だと思いますが、使えるところはないようで意外とあるとも思うので、ぜひぜひ試してみてください。