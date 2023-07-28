# Unity用のHTTP/2(gRPC) Client、YetAnotherHttpHandlerを公開しました

Cysharpから(主に)Unity用のHTTP/2, gRPC, MagicOnion用の通信ネットワーククライアントを公開しました。実装者は週刊.NET情報配信[WeekRef.NET](https://weekref.net/)を運営している[@mayuki](https://twitter.com/mayuki)さんです。

* [Cysharp/YetAnotherHttpHandler](https://github.com/Cysharp/YetAnotherHttpHandler)

何故これが必要なのかの背景情報としては、[Synamon’s Engineer blog - Unityでもgrpc-dotnetを使ったgRPCがしたい](https://synamon.hatenablog.com/entry/grpc-dotnet-unity) が詳しいのですが、まず、.NETには2つのgRPC実装があります。googleが提供してきたgRPCのネイティブバインディングのGrpc.Core(C-Core)と、Microsoftが提供しているPure C#実装のgrpc-dotnet。現在.NETのgRPCはサーバーもクライアントも完全にPure C#実装のほうに寄っていて、[MagicOnion](https://github.com/Cysharp/MagicOnion/)もサーバーはPure C#実装のものを使っています。しかしクライアントに関しては、諸事情によりUnityでは動かない（TLS関連の問題など）ため、ずっとC-Coreを推奨してきました。しかし、Unity用のビルドは元々experimentalだったうえに、とっくにメンテナンスモードに入り、そしてついに今年5月にサポート期限も切れて完全に宜しくない気配が漂っていました。また、古いx64ビルドなので最近のMac(M1, M2チップ)では動かないためUnity Editorで使うのにも難儀するといった問題も出てきていました。

と、いうわけで、CysharpではUnityで使うgRPCを推奨してきたということもあり、Unityで問題なく使えるgRPC実装としてYetAnotherHttpHandlerを開発・リリースしました。HttpClientの通信レイヤーであるHttpHandlerを差し替えるという形で実装してあるので、ほとんど通常の .NET でのgRPCと同様に扱えます。

内部実装としてはPure Rust実装のHTTP/2ライブラリ[hyper](https://hyper.rs/)とPure RustのTLSライブラリ[rustls](https://github.com/rustls/rustls)を基盤として作ったネイティブライブラリに対して、[Cysharp/csbindgen](https://github.com/Cysharp/csbindgen)で生成したC#バインディングを通して通信する形になっています。

余談
---
YetAnotherHttpHandlerはgRPCやMagicOnionに限らず、Unityで自由に使える HTTP/2 Clientなので、アセットダウンロードの高速化にHTTP/2を用いる、といったような使い道も考えられます。既にモバイルゲームでも幾つかのタイトルでHTTP/2でアセットダウンロードしているタイトルは確認できていまして、例えばセガさんは[CEDEC2021 ダウンロード時間を大幅減！～大量のアセットをさばく高速な実装と運用事例の共有～](https://speakerdeck.com/segadevtech/cedec2021-taunrotoshi-jian-woda-fu-jian-da-liang-falseasetutowosahakugao-su-nashi-zhuang-toyun-yong-shi-li-falsegong-you)のような発表もされています。ネイティブプラグインを自前でビルドして持ち込むというのはだいぶ敷居が高い話でしたが、YetAnotherHttpHandlerを入れるだけでいいなら、だいぶやれるんじゃないか感も出てくるんじゃないでしょうか……？