# .NETプロジェクトとUnityプロジェクトのソースコード共有最新手法

[MagicOnionのv6](https://github.com/Cysharp/MagicOnion/)が先日リリースされました。

メジャーバージョンアップとして大きな違いは、[Cysharp/YetAnotherHttpHandler](https://github.com/Cysharp/YetAnotherHttpHandler)を正式リリースし、これを通信層の標準ライブラリ化しました。インストール手順も複雑で、サポートも切れていたgRPC C-Coreとはさようならです。正式リリースにあたってプレビューに存在していたクラッシュ問題などが解消されています。

もう一つはクライアント生成においてコマンドラインツールが削除され、Source Generatorベースになりました。

```csharp
[MagicOnionClientGeneration(typeof(MyApp.Shared.Services.IGreeterService))]
partial class MagicOnionGeneratedClientInitializer {}
```

これだけでコンパイル時にジェネレートされます。コマンドラインツールには、インストールしている.NETのバージョンによって動作したりしなかったりや、生成ファイルの管理をどうするかや、ビルドプロセスの複雑化など、問題が多くありましたがSource Generator化によって全て解決しました。

残念ながらまだMessagePack for C#がコマンドラインツールを必要としているため、完全なコマンドラインツール不要化には至っていませんが、そちらの改善も着手中のため、近いうちにはアプリケーション全体の完全なSource Generator化が果たせるのではないかと思います。それに合わせて[Cysharp/MasterMemory](https://github.com/Cysharp/MasterMemory/)のSource Generator化も行いたいと思っています。


.NETプロジェクトとUnityプロジェクト間でのコード共有
---
MagicOnionに限らずですが、.NETとUnityとの間でソースコードをどのように共有すればいいのか問題があります。昔のやり方では、Unity側で実態を持っていて.NET側で参照を拾ってくるとか、.NET側のビルド時にUnity側にコピーをばらまく、シンボリックリンクで参照する、などといった方法を提案していたのですが、すべて正直イマイチでした。

というわけで令和最新版の方法を紹介します。先に結論をいうと、.NET側に普通の共有用クラスライブラリプロジェクトを作って、Unity側ではUPMのローカルパッケージ参照でソースコードを引っ張ってくるのが現状のベストだと考えています。ただしそのままやると幾つか面倒なことが発生するので、しっかりした手順をここに書いておきます。

まずは.NET側のプロジェクトとして、.NET Standard 2.0/2.1, LangVersion 9のクラスライブラリプロジェクトを作ります。

![image](https://github.com/Cysharp/MagicOnion/assets/46207/0019a2b0-ec2c-4786-9d1d-0078e8dc0295)

そして`Directory.Build.props`を配置します。これは複数のcsprojにまたがって共有した設定が行えるやつなのですが、今回は単独のcsprojに適用する場合にも使います。そんな`Directory.Build.props`の中身はこれです。

```xml
<?xml version="1.0" encoding="utf-8"?>
<Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <PropertyGroup>
    <!-- Unity ignores . prefix folder -->
    <ArtifactsPath>$(MSBuildThisFileDirectory).artifacts</ArtifactsPath>
  </PropertyGroup>
</Project>
```

最新手法と銘打った理由として.NET 8(以降に同梱されてるコンパイラ)は[成果物の出力レイアウトを変更する](https://learn.microsoft.com/en-us/dotnet/core/sdk/artifacts-output)ことができるようになりました。なぜこれが必要かというと、通常、ビルドするとbin, objがcsprojのディレクトリに吐かれるわけですが、Unityでパッケージ参照するとそのbin, objまで取り込んでしまって大問題なんですね。ArtifactsPathを設定することでbin, objの出力場所を変更できます、そして[Unityのアセットインポートにおける命名規則](https://docs.unity3d.com/Manual/SpecialFolders.html)のうち`.`か`~`で始まってるファイルまたはフォルダは無視されます。というわけで、bin, objの出力場所を`.artifacts`に変えることで、Unityから参照しても問題ない構成になりました。

もう少し作業が必要で、次にcsprojを開いて、以下の行を追加しておきます。

```csharp
<ItemGroup>
  <None Remove="**\package.json" />
  <None Remove="**\*.asmdef" />
  <None Remove="**\*.meta" />
</ItemGroup>
```

これは、Unityからパッケージ参照すると.metaが大量にばらまかれてウザいので、少なくともcsprojの見た目からは消しておきます。package.jsonとasmdefも同様に.NETプロジェクトとしては不要なので管理外へ。

というわけで最後に、package.jsonとasmdefをこのディレクトリに置いておきましょう。これがないとUnity側から正しく参照できないので。

![image](https://github.com/Cysharp/MagicOnion/assets/46207/54c9564d-c6f2-44ec-b86c-bec19ecfb040)

```json
{
  "name": "com.cysharp.magiconion.samples.chatapp.shared.unity",
  "version": "1.0.0",
  "displayName": "ChatApp.Shared.Unity",
  "description": "ChatApp.Shared.Unity",
  "unity": "2019.1"
}
```

```json
{
    "name": "ChatApp.Shared.Unity",
    "references": [
        "MessagePack",
        "MagicOnion.Abstractions"
    ],
    "optionalUnityReferences": [],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": true,
    "defineConstraints": []
}
```

referencesとかはお好きな感じで。

これでほぼ準備は完了です！とはもうUnity側ではPackage Managerを開いてAdd package from diskで先ほどの共有プロジェクトのディレクトリを指定すればOK。

![image](https://github.com/Cysharp/MagicOnion/assets/46207/a46813ab-72fb-44b3-ac8e-241451f9128f)

ただし、これで参照すると絶対パスが書かれているので、`manifest.json`を開いて相対パスに手動で書き換えましょう。

```json
{
  "dependencies": {
    "com.cysharp.magiconion.samples.chatapp.shared.unity": "file:../../ChatApp.Shared",
  }
}
```

これでいい具合に取り扱うことができました！

さらに一歩進んで、サーバー側のslnでUnity側のcsprojも一緒に管理したいんだよなあ、とかやりたい場合は[Cysharp/SlnMerge](https://github.com/Cysharp/SlnMerge/)を使うとよいでしょう。

![image](https://github.com/Cysharp/SlnMerge/assets/46207/6b70bfda-5f80-42c0-9acc-ca3922f22c52)

単一slnで管理すると、Unity側での作業時に共有プロジェクトのコードを弄りやすくなりますし、サーバー/クライアントを超えたデバッグのステップ実行ができるようになるなど、かなり作りやすくなるので、あわせて是非設定しておくことをお薦めします。

Unity用ライブラリのNuGet配布のための開発時環境設定
---
先日[R3](https://github.com/Cysharp/R3/)というUniRxの進化版みたいなのをリリースしましたが、これはコアライブラリはNuGetで配布するようにしました。ちょっと前まで私はNuGet配布に関して否定的で、Unity向けにはソースコードをちゃんと配らないと、みたいに思ってたんですが、今はNuGet配布にたいして超ポジティブです。というか、逆にNuGet配布じゃないとマズいような状況もあるので、今後のものは全てNuGet配布にするほか、既存のものも随時NuGet配布に切り替えると思います。まずはMessagePack for C#が近いうちにそうなります……！

それはいいんですが、Unity用に開発している際に.NETライブラリとして作られているコードを参照したい、んですよね、というか参照できないとUnity向け拡張(R3.Unity)が作れないし。

で、じゃあ上のやり方みたいローカルパッケージ参照でソースコードを持ってきてやろう、と思ったんですが、ダメでした。というのもR3の本体はC# 12で書かれていたのだ……！DLLとして配布するので別に言語バージョンは問題ない(コンパイルしてIL化すると.NETのバージョンは関係ありますが言語バージョンは関係なくなる)ので、Unityで使うことが前提ながら普通にC# 12で書いていたので、ソースコードとしての参照はできない。

ビルド時の成果物をUnity側にコピーするようにしても、まぁいいっちゃあいいんですが、作業中のちょっと書き換える度にコミットされるのでリポジトリが無駄に膨らむから嫌だなー、と。

で、そこで、やはりローカルパッケージ参照です。ただし今回は`package.json`のみで、asmdefは配りません。そして`bin/Debug/netstandard2.0`(2.1でもいい)にpackage.jsonを置いて、package.jsonとpackage.json.metaのみgitの管理下に置きます。

実際のリポジトリ: [https://github.com/Cysharp/R3/tree/main/src/R3/bin/Debug/netstandard2.0](https://github.com/Cysharp/R3/tree/main/src/R3/bin/Debug/netstandard2.0)

手元のフォルダの状況:  
![image](https://github.com/Cysharp/MagicOnion/assets/46207/2c8f7cb4-08ea-459c-abcc-6a251a063cb2)

これを同じようにローカルパッケージ参照すると、開発用のdllだけをUnityに引っ張ってくることができました。別にパッケージの中にソースコードがなくてもいいわけですね……！

なお、普通のゲーム開発でもC# 12で書きたいんだよー、という人は、ソースコード参照じゃなくてこっちのやり方を使っても成立はします。全然、アリです。ただし、.NET側でビルドしないと反映されないとか、デバッグビルドとリリースビルドどっち参照させます？とかいうところを考えなきゃいけないので、まぁお好みで、というところでしょうか。

まとめ
---
というわけで、2024年になってようやく満足いく共有手法にたどり着けました。これはC#大統一理論元年……！