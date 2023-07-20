# AlterNats は 公式の NATS .NET Client v2 に引き継がれました

[NATS](https://nats.io/)のサードパーティー(alternative)クライアントであった[AlterNats](https://github.com/Cysharp/AlterNats)は、公式に引き取られて[NATS.NET V2](https://github.com/nats-io/nats.net.v2)となりました。v2の詳細に関してはNATS公式からのブログ[NATS .NET Client v2 Alpha Released with Core NATS Support](https://nats.io/blog/nats-dotnet-v2-alpha-release/)を参照ください。

> NATS community members started to take note, and develop client libraries for NATS based on modern .NET APIs. One notable client library that emerged was the AlterNats library by Cysharp, which includes a fully asynchronous API, leverages Span<T> , and supports client-side WebSockets from browsers in Blazor . NATS maintainers and AlterNats maintainers agreed that AlterNats would make a great starting point for NATS.Client v2!

NATSに関してはAlterNatsリリース時の記事 [AlterNats - ハイパフォーマンスな.NET PubSubクライアントと、その実装に見る.NET 6時代のSocketプログラミング最適化のTips、或いはMagicOnionを絡めたメタバース構築のアーキテクチャについて](https://neue.cc/2022/05/11_AlterNats.html)に色々書きましたが、[Cloud Native Computing Foundation](https://www.cncf.io/)配下のPubSubミドルウェアで、RedisなどでのPubSubに比べるとパフォーマンスを始めとして多くのメリットがあります。

ただしこういうものはサーバー実装も重要ですがクライアント実装も重要であり、そして当時のNATSの公式クライアント(v1)は正直酷かった！せっかくの素晴らしいミドルウェアが.NETでは活かされない、また、RedisでのPubSubには不満があり、そもそも.NETでのベストなPubSubのソリューションがないことに危機意識を感じていたので、独自に実装を進めたのがAlterNatsでした。

ただし、枯れたプロトコルならまだしも、進化が早いミドルウェアのクライアントが乱立しているのは決して良いことでもないでしょう。新機能への追随速度やメンテナンスの保証という点でも、サードパーティクライアントとして進んでいくよりも、公式に統合されることのほうが絶対に良いはずです。

というわけで今回の流れは大変ポジティブなことだし、野良実装にとって最高の道を辿れたんじゃないかと思っています。私自身は実装から一歩引きますが、使っていく上で気になるところがあれば積極的にPR上げていくつもりではあります。

なお、NATSに関しては来月CEDEC 2023でのセッション[メタバースプラットフォーム「INSPIX WORLD」はPHPもC++もまとめてC#に統一！～MagicOnionが支えるバックエンド最適化手法～](https://cedec.cesa.or.jp/2023/session/detail/s64258612468b3)で触れる、かもしれません、多分。というわけでぜひ聞きに来てください……！

メタバース関連では、今年の5月にTGS VRなどを手掛けている[ambr](https://ambr.co.jp/)さんのテックブログにて[VRメタバースのリアルタイム通信サーバーの技術にMagicOnionとNATSを選んだ話](https://ambr-inc.hatenablog.com/entry/20230512/1683882000)という紹介もしていただいていました。

OSSとメンテナンスの引き継ぎ
---
権限の移管は何度か経験があって

* [linq.js](https://github.com/mihaifm/linq)、
* [ReactiveProperty](https://github.com/runceel/ReactiveProperty)
* [CloudStructures](https://github.com/xin9le/CloudStructures)

は完全に手放しています。ほか、[MagicOnion](https://github.com/Cysharp/MagicOnion)はCysharp名義に移ったうえで、現在の開発リードは私ではありません。また、最近では[MessagePack for C#](https://github.com/MessagePack-CSharp/MessagePack-CSharp)はMessagePack-CSharp Organizationに移していて共同のOwner権限になっています。

どうしても常に100%の力を一つのOSSに注ぐことはできないので、本来はうまく移管していけるのが良いわけですが、いつもうまくできるわけじゃなくて、[Utf8Json](https://github.com/neuecc/Utf8Json)なんかはうまく移管できないままarchivedにしてしまっています。

やっぱ出した当時は自分が手綱を握っていたいという気持ちがとても強いわけですが、関心が徐々に薄れていくタイミングと他の人に渡せるタイミングがうまく噛み合わないと、死蔵になってしまうというところがあり、まぁ、難しいです。これだけやっていても上手くできないなあ、と……。

今回のは大変良い経験だったので、作ってメンテナンスを続ける、そしてその先についても考えてやっていきたいところですね。

ともあれ、良い事例を一つ作れた＆素晴らしいライブラリをC#に一つ持ち込むことができたということで、とても気分がよいですです。