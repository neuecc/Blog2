# 他言語がメインの場合のRustの活用法 - csbindgenによるC# x Rust FFI実践事例

[Rust.Tokyo 2023](https://rust.tokyo/2023)というRustのカンファレンスで、「他言語がメインの場合のRustの活用法 - csbindgenによるC# x Rust FFI実践事例」と題して[csbindgen](https://github.com/Cysharp/csbindgen)周りの話をしてきました。

<iframe class="speakerdeck-iframe" frameborder="0" src="https://speakerdeck.com/player/fca414aeffb9486ab2f738466df6da02" title="他言語がメインの場合のRustの活用法 - csbindgenによるC# x Rust FFI実践事例" allowfullscreen="true" style="border: 0px; background: padding-box padding-box rgba(0, 0, 0, 0.1); margin: 0px; padding: 0px; border-radius: 6px; box-shadow: rgba(0, 0, 0, 0.2) 0px 5px 40px; width: 100%; height: auto; aspect-ratio: 560 / 315;" data-ratio="1.7777777777777777"></iframe>

タイトルが若干かなり回りっくどい雰囲気になってしまいましたが、Rustのカンファレンスということで、あまりC#に寄り過ぎないように、という意識があったのですが、どうでしょう……？

会場での質問含めて何点かフォローアップを。

FFIとパフォーマンス
---
Rustは速い！FFIは速い！ということが常に当てはまるわけでもなく、例えばGoのcgoはかなり遅いという話があったりします。[Why cgo is slow @ CapitalGo 2018](https://speakerdeck.com/filosottile/why-cgo-is-slow-at-capitalgo-2018)。このことは直近のRustのasyncの話[Why async Rust?](https://without.boats/blog/why-async-rust/)でも触れられていて、cgoの遅さはGoroutine(Green Thread)が影響を及ぼしているところもある、とされています。.NET でも[Green Threadを実験的に実装してみたというレポート](https://github.com/dotnet/runtimelab/blob/bec51070f1071d83f686be347d160ea864828ef8/docs/design/features/greenthreads.md)がついこないだ出ていたのですが、FFIの問題とか、まぁ諸々あってasync/awaitでいいじゃろ、という結論になっています。

で、C#のFFI速度ですが、こちらの[Testing FFI Hot Loop Overhead - Java, C#, PHP, and Go](https://vancan1ty.com/blog/post/52)という記事での比較ではFFIにおいては圧勝ということになっているので、まぁ、実際C#のFFIは速いほうということでいいんじゃないでしょーか（昔からWin32 APIを何かと叩く必要があったりとかいう事情もありますし）。

とはいえ、原則Pure C#実装のほうがいいなあ、という気持ちはめっちゃあります。パフォーマンスのためのネイティブライブラリ採用というのは、本当に限定的な局面だけではありますね。そんなわけで、その限定的な局面であるところのコンプレッションライブラリを鋭意開発中です、来月に乞うご期待。

Zig, C++
---
FFI目的でunsafeなRust中心になるぐらいなら[Zig](https://ziglang.org/ja/)のほうがいいんじゃない？というのは一理ある。というか最初はそう思ってZigを試したんですが、ちょーっと難しいですね。一理ある部分に関しては一理あるんですが、それ以外のところではRustのほうが上だという判断で、総合的にはRustを採用すべきだと至りました。

具体的には資料の中のRustの利点、これは資料中ではC++との比較という体にしていますが、Zigとの比較という意味もあります。標準公式のパッケージマネージャーがないし、開発環境の乏しさは、たとえZigが言語的にRustよりイージーだとしても、体感は正直言ってRustよりもハードでした。コンパイルエラーもRustは圧倒的にわかりやすいんですが、Zigはめちゃくちゃ厳しい……。

また、ZigはZigでありC/C++ではない。これはRustも同じでRustはRustでC/C++ではない、つまりCとZig(Rust)を連動させるには[bindgen](https://github.com/rust-lang/rust-bindgen)のようなものが必要なのですが、Zigのそれの安定性がかなり低い、パースできない.hが普通にチラホラある。rust-bindgenのIssue見ていると本当に色々なケースに対応させる努力を延々と続けていて、それがbindgenの信頼性（と実用性）に繋がっているわけで、Zigはまだまだその域には達していないな、と。

Cはまだいいとしても、C++のエコシステムを使うという点では、ZigもRustも難しい。セッションの中では[PhysX 5](https://github.com/NVIDIA-Omniverse/PhysX)を例に出しましたが、物理エンジンはOSSどころだと[Bullet Physics](https://github.com/bulletphysics/bullet3)も[Jolt Physics](https://github.com/jrouwe/JoltPhysics)も、SDKそのままそのものはC++だけなんですよね。これをC++以外の言語に持ち込むのは非常に骨の折れる仕事が必要になってきます。Rustに関してはEmbark Studiosが[physx-rs](https://github.com/EmbarkStudios/physx-rs)を作ってくれたのである程度現実的ではありますが、何れにせよ大仕事が必要で、そのままでは持ち込めないというのが現実です。

physx-rsではC++のPhysXをRustで動かすために、まずC APIのPhysXを自動生成してそれ経由でRustから呼び出す、という話をしましたが、ZigもC++のものを呼び出すには、概ね同様のアプローチを取る必要があり、例えばZigでJolt Physicsを動かす[zphysics](https://github.com/michal-z/zig-gamedev)というプロジェクトでは、C++のJoltに対して、C APIで公開するJoltCという部分を作って、それ経由でZigから呼び出すという手法を取っています。

この辺のことは[MagicPhysX](https://github.com/Cysharp/MagicPhysX)を作る時にめちゃくちゃ迷走して色々作りかけてたので痛感しています。そう、最初はZigでBullet Physicsを動かしてC#から呼び出すMagicBulletというプロジェクトだったこともあったのだ……。

最後発C++後継系言語であるところの[Carbon Language](https://github.com/carbon-language/carbon-lang)は、C++におけるTypeScriptというのを標榜しているので、そうしたC++との連携を最優先に考えた言語になっているんじゃないかなー、と思います（触ってないので知らんですけど！）。C++の後継はRust(やZig)があるからいらんやろー、とはならない、C++の資産を活かしながらもモダンな言語仕様を使えるようにする、という絶妙な立ち位置を狙っているんじゃないかなー、と。どのぐらい盛り上がっていくのかわかりませんが……！

C++/CLI
---
[C++/CLI](https://ja.wikipedia.org/wiki/C%2B%2B/CLI)は使わないんですか？という質問がありました。.NETとC++ライブラリの連携という点で、C++/CLIはたしかに良いソリューションで、C++のライブラリをC#のために公開するブリッジとしては最高に使いやすい代物でした。.NET Frameworkの時代までは。

C++/CLIの問題は「.NET Core の C++/CLI サポートは Windows のみ」ということで、特にライブラリがLinuxサポートしないというのはありえないので、.NET Core以降にC++/CLIを新規採用するのは基本ありえない、といった状態になっています。こういった問題があるので、 .NET Framework時代に作られていたC++ライブラリをC#で使える系ライブラリはほとんど使えなくなりました。例えばPhysX 4の.NETバインディングである[PhysX.NET](https://github.com/stilldesign/PhysX.Net)は、C++/CLIでバインディングが作られているため、.NET 5対応はしていますが、サポートプラットフォームはWindowsのみです。

[csbindgen](https://github.com/Cysharp/csbindgen)は、そうした.NET / C連携での空白地帯にちょうどうまくはまったライブラリなのではないかと思います。C++連携については頑張るしかないですが、そこはしょうがないね……！ ただ、Rustはエコシステムがうまく動いているので、Pure Rustライブラリであったり、RustでC++バインディングが作られているものを経由してC#バインディングを作る、といった手法でうまく回せる場合も多いんじゃないかなあー、というところがいいところです。それと、近年のライブラリ事情でいうと、物理エンジンみたいな老舗系はC++で作られていますが、例えば暗号通貨系のライブラリなんかは最初からRust実装だったりするものも多いので、RustからC#への持ち込み、のほうが今後の実用性としても高いんじゃないかと踏んでいます。