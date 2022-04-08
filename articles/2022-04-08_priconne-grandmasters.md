# プリコネ！グランドマスターズのサーバー開発をCysharpが開発協力しました

Cygamesから4/1にリリースされた[プリコネ！グランドマスターズ](https://priconne-grandmasters.jp/)のサーバーサイドとインフラ開発をCysharpが開発協力しました。リアルタイム通信を含むオートバトラー系のゲームです。

![image](https://user-images.githubusercontent.com/46207/162343388-734840a1-4b7d-467b-902c-1e06e527d208.png)

![image](https://user-images.githubusercontent.com/46207/162401207-d9e2bceb-6b94-435c-8e63-d96ce62cf97b.png)

リアルタイム通信部分だけではなくてAPIサーバーからマッチメイキング、インフラまで、構成されるあらゆる要素がC#で作られています！

* クライアント (Unity)
* API サーバー(MagicOnion)
* バトルエンジンサーバー (リアルタイム通信; MagicOnion)
* マッチメイキングサーバー (リアルタイム通信; MagicOnion)
* バッチ(ConsoleAppFramework)
* デバッグ機能サーバー (Web; Blazor)
* 管理画面サーバー (Web; Blazor)
* インフラ (Infrastructure as Code; Pulumi)

サーバー側アプリケーションは.NET 6をKubernetes上で動かしています。Unityクライアント側でも[CysharpのOSS](https://github.com/Cysharp/)は7つクレジットされていますが、表記のないサーバー側専用のものを合わせたら10個以上使用しています。ここまで徹頭徹尾C#でやっているプロジェクトは世界的にも珍しいんじゃないでしょうか。中心的に活躍しているのは[MagicOnion](https://github.com/Cysharp/MagicOnion/)ですが、サーバーサイドゲームループのための[LogicLooper](https://github.com/Cysharp/LogicLooper)、負荷テストのための[DFrame](https://github.com/Cysharp/DFrame/)なども実戦投入されて、成果を出しました。サーバートラブルも特になく、しっかり安定稼働しました。という事後報告です。そして今日、もとより期間限定公開ということで一週間の配信期間が終了しました。

アーキテクチャ含めの詳しい話は後日どこかでできるといいですねー。

こういった構成を、Cysharpだから出来る、のではなくて、誰もが実現できる環境にしていきたいと思っています。重要なパーツは積極的にOSS化していますし、実績も着実に積み重ねられています。が、しかしまだまだ難しい面も数多くあるということは認識しています。かといってmBaaSの方向でやっていくべき、とは思わないんですね。ロジックはゲームの差別化のための重要な要素であり、サーバーサイドでも書くべきで。だから注力しているのは書きやすくするための環境で、そのために足りないものを提供していっています。

サーバーとクライアントの繋ぎ、あるいはサーバーとサーバーの繋ぎが、MagicOnionだけだと複雑で難しいと思っていて、ちょうど先月-今月はメッセージングライブラリの開発に注力しています。[AlterNats](https://github.com/Cysharp/AlterNats)という名前でPreview公開していますが、これを挟むと色々改善されるんじゃないかなあ、と思っているので、少々お待ち下さい。