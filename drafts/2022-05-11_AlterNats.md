# AlterNats - ハイパフォーマンスな.NET PubSubクライアントと、その実装に見る.NET 6時代のSocketプログラミング最適化のTips、或いはMagicOnionを絡めたメタバース構築のアーキテクチャについて

タイトルはここぞとばかりに全盛りにしてみました！今回NATSの.NETクライアント実装としてAlterNatsというライブラリを新しく作成し、公開しました。

* [github - Cysharp/AlterNats](https://github.com/Cysharp/AlterNats)

公式の既存クライアントの3倍以上、StackExchange.RedisのPubSubと比較して5倍以上高速であり、通常のPubSubメソッドは全てゼロアロケーションです。

![image](https://user-images.githubusercontent.com/46207/164392256-46d09111-ec70-4cf3-b33d-38dc5d258455.png)

そもそも[NATS](https://nats.io/)とはなんぞやか、というと、クラウドネイティブなPubSubのミドルウェアです。[Cloud Native Computing Foundation](https://www.cncf.io/)のincubating projectなので、それなりの知名度と実績はあります。

PubSubというと、特にC#だと[Redis](https://redis.io/)のPubSub機能で行うのが、[StackExchange.Redis](https://github.com/StackExchange/StackExchange.Redis)という実績あるライブラリもあるし、AWSやAzure、GCPがマネージドサービスも用意しているしで、お手軽でいいのですが、盲目的にそれを使うのが良いのか少し疑問に思っていました。

RedisはKVS的な使い方がメインであり、PubSubはどちらかというとオマケ機能であるため

* PubSub専用のモニタリングの欠如
* PubSub用のクラスタリング対応
* マネージドサービスでの価格体系のバランスの悪さ（PubSub特化ならメモリはあまりいらない）
* そもそものパフォーマンス

といった点が具体的な懸念です。そして、NATSはPubSub専用に特化されているため、そのためのシステムが豊富に組まれているし、性能も申し分なさそうに思えました。しいて欠点を言えばマネージドサービスが存在しないのがネックですが、純粋なPubSubとしての利用ならば永続化処理について考える必要がないので、ミドルウェアとしては運用しやすい部類にはいるのではないかと思っています。（NATS自体はNATS JetStreamという機能によってAt-least / exactly onceの保証のあるメッセージングの対応も可能ですが、そこに対応させるにはストレージが必要になる場合もあります）

しかし調べていくうちに懸念となったのが公式クライアントである[nats.net](https://github.com/nats-io/nats.net)で、あまり使いやすくないのですね。async/awaitにも対応していないし、古くさく、それどころかそもそも.NET的に奇妙に見えるAPIであり、そうなるとパフォーマンスに関しても疑問に思えてくる。

何故そうなっているかの理由はReadMeにも明記されていて、メンテナンス性のためにGoクライアント(ちなみにNATS Server自体はGoで書かれている)と同じようなコードベースになっている、と。そのためC#的ではない部分が多々あるし、GoとC#ではパフォーマンスを出すための書き方が全く異なるので、あまり良い状況ではなさそう。

それならば完全にC#に特化して独自に作ってしまうほうがいいだろうということで、作りました。公式クライアントと比べると全ての機能をサポートしているわけではない（JetStreamにも対応していないしLeaf Nodes運用で必須になるであろうTLSにも対応していません）のですが、PubSubのNATS Coreに特化して、まずは最高速を叩き出せるようにしました。PubSub利用する分には機能面での不足はないはずです。

AlterNatsは公式じゃないAlternativeなNATSクライアントという意味です。まんまですね。割と語感が良いので命名的には結構気に入ってます。

## Getting Started

APIは、`nats.net`があまりにもC#っぽくなくややこしい、ということを踏まえて、シンプルに、簡単に、C#っぽく書けるように調整しました。

```csharp
// create connection(default, connect to nats://localhost:4222)
await using var conn = new NatsConnection();

// for subscriber. await register to NATS server(not means await complete)
var subscription = await conn.SubscribeAsync<Person>("foo", x =>
{
    Console.WriteLine($"Received {x}");
});

// for publisher.
await conn.PublishAsync("foo", new Person(30, "bar"));

// unsubscribe
subscription.Dipose();

// ---

public record Person(int Age, string Name);
```

Subscribeでhandlerを登録し、Publishでメッセージを飛ばす。データは全て自動でシリアライズされます（デフォルトではSystem.Text.Json、MessagePack for C#を用いたハイパフォーマンスなシリアライズも可能な拡張オプションも標準で用意してあります）

別のURLへの接続や、認証のための設定などを行うNatsOptions/ConnectOptionsはイミュータブルです。そのため、with式で構築するやり方を取っています。

```csharp
// Options can configure `with` operator
var options = NatsOptions.Default with
{
    Url = "nats://127.0.0.1:9999",
    LoggerFactory = new MinimumConsoleLoggerFactory(LogLevel.Information),
    Serializer = new MessagePackNatsSerializer(),
    ConnectOptions = ConnectOptions.Default with
    {
        Echo = true,
        Username = "foo",
        Password = "bar",
    }
};

await using var conn = new NatsConnection(options);
````

NATSには標準で結果を受け取るプロトコルも用意されています。サーバー間の簡易的なRPCとして使うと便利なところもあるのではないかと思います。これも`SubscribeRequestAsync`/`RequestAsync`という形で簡単に直感的に書けるようにしました（Request側は戻り値の型を指定する必要があるため、型指定が少しだけ冗長になります）

```csharp
// Server
await conn.SubscribeRequestAsync("foobar", (int x) => $"Hello {x}");

// Client(response: "Hello 100")
var response = await conn.RequestAsync<int, string>("foobar", 100);
```

例では `await using`ですぐに破棄してしまっていますが、基本的にはConnectionはシングルトンによる保持を推奨しています。staticな変数に詰めてもいいし、DIでシングルトンとして登録してしまってもいいでしょう。接続は明示的にConnectAsyncすることもできますが、接続されていない場合は自動で接続を開くようにもなっています。

コネクションはスレッドセーフで、物理的にも一つのコネクションには一つの接続として繋がり、全てのコマンドは自動的に多重化されます。これにより裏側で自動的にバッチ化された高効率な通信を実現していますが、負荷状況に応じて複数のコネクションを貼った場合が良いケースもあります。AlterNatsではNatsConnectionPoolという複数コネクションを内包したコネクションも用意しています。また、クライアント側で水平シャーディングを行うためのNatsShardingConnectionもあるため、必要に応じて使い分けることが可能です。

内部のロギングはMicrosoft.Extensions.Loggingで管理されています。`AlterNats.Hosting`パッケージを使うと、Generic Hostと統合された形で適切なILoggerFactoryの設定と、シングルトンのサービス登録を行ってくれます。

DIでの取り出しは直接NatsConnectionを使わずに、INatsCommandを渡すことで余計な操作（コネクションの切断など）が出来ないようになります。

```csharp
using AlterNats;

var builder = WebApplication.CreateBuilder(args);

// Register NatsConnectionPool, NatsConnection, INatsCommand to ServiceCollection
builder.Services.AddNats();

var app = builder.Build();

app.MapGet("/subscribe", (INatsCommand command) => command.SubscribeAsync("foo", (int x) => Console.WriteLine($"received {x}")));
app.MapGet("/publish", (INatsCommand command) => command.PublishAsync("foo", 99));

app.Run();
```

## メタバースアーキテクチャ

Cysharpでは[MagicOnion](https://github.com/Cysharp/MagicOnion)という .NET/Unity で使えるネットワークフレームワークを作っているわけですが、AlterNatsはこれと絡めることで、構成の幅を広げることができると考えています、というかむしろそのために作りました。

クライアントにUnity、サーバーにMagicOnionがいるとして、サーバーが一台構成なら、平和です、繋げるだけですもの。開発の最初とかローカルでは楽なのでこの状態でもいいですね。

![image](https://user-images.githubusercontent.com/46207/164406771-58318153-c6a7-49c0-b3af-2b8389e2c9c1.png)

しかし現実的にはサーバーは複数台になるので、そうなると色々なパターンが出てきます。よくあるのが、ロードバランサーを立ててそれぞれが別々のサーバーに繋がっているものを、更に後ろのPubSubサーバーを通して全サーバーに分配するパターン。

![image](https://user-images.githubusercontent.com/46207/164409016-b6e99f36-bdf7-47a9-80a6-558010963a36.png)

これはNode.jsのリアルタイムフレームワークである[Socket.IO](https://socket.io/)のRedisアダプター、それの.NET版である[SignalR](https://docs.microsoft.com/ja-jp/aspnet/signalr/overview/getting-started/introduction-to-signalr)のRedisバックプレーン、もちろんMagicOnionにもあるのですが、このパターンはフレームワークでサポートされている場合も多いです。RedisのPubSubでできることはNATSでもできる、ということで、NATSでもできます。

これは各サーバーをステートレスにできるのと、スケールしやすいので、Chatなどの実装にはやりやすい。欠点はステートを持ちにくいので、クライアントにステートがあり、データのやり取りをするタイプしか実装できません。サーバー側にステートを持ったゲームロジックは持たせずらいでしょう（ステートそのものは各サーバーで共有できないため）。また、PubSubを通すことによるオーバーヘッドも気になるところかもしれません。

ロードバランサーを立てる場合、ロードバランサーのスティッキーセッションを活用して一台のサーバーに集約させるというパターンもあります（あるいは独自プロトコルでもリバースプロキシーを全面に立てて、カスタムなロジックで後ろの台を決定することもほぼ同様の話です）。ただし、色々なユーザーを同一サーバーに集約させたいようなケースでは、そのクッキーの発行誰がやるの、みたいなところは変わらずありますね。そこまで決めれるならIPアドレスを返して直繋ぎさせてしまってもいいんじゃないの？というのも真です。

そうした外側に対象のIPアドレスを教えてくれるサービスがいて、先にそれに問い合わせてから、対象のサーバーへ繋ぎに行くパターンは、古典的ですが安定です。

![image](https://user-images.githubusercontent.com/46207/164417937-7d1adedb-36ee-453b-9ca6-9d41aded50af.png)

この場合は同一サーバーに繋ぎにいくためにサーバー内にインメモリでフルにステートを持たせることが出来ますし、いわゆるゲームループを中で動かして処理するようなこともできます。また、画面のないヘッドレスUnityなどをホストして、クライアントそのものをサーバー上で動かすこともできますね。

しかし、このパターンは素直なようでいて、実際VMだとやりやすいのですが、Kubernetesでやるのは難しかったりします。というのも、Kubernetesの場合は外部にIPが露出していないため、クラスター内の一台の特定サーバーに繋ぎにいくというのが難しい……！

このような場合に最近よく活用されているのが[Agones](https://agones.dev/site/)というGoogleが主導して作っているKubernetesの拡張で、まさにゲーム向きにKubernetesを使えるようにするためのシステムです。

ただし、これはこれで難点があって、Agonesが想定しているゲームサーバーは1プロセス1ゲームセッション(まさにヘッドレスUnityのような)のホスティングであるため、1つのプロセスに多数のゲームセッションをホストさせるような使い方はそのままだと出来ません。コンテナなので、仮想的なプロセスを複数立ち上げればいいでしょ、というのが思想なのはわからなくもないのですが、現実的には軽量なゲームサーバー（それこそMagicOnionで組んだりする場合）なら、1プロセスに多数のゲームセッションを詰め込めれるし、これをコンテナで分けて立ち上げてしまうとコスト面では大きな差が出てしまいます。

さて、Cysharpではステートフルな、特にゲームに向いたC#サーバーを構築するための補助ライブラリとして[LogicLooper](https://github.com/Cysharp/LogicLooper)というゲームループを公開しています。このライブラリはこないだリリースした[プリコネ！グランドマスターズ](https://neue.cc/2022/04/08_priconne-grandmasters.html)でも使用していますが、従来MagicOnionと同居して使っていたLogicLooperを、剥がしたアーキテクチャはどうだろうか、という提案があります。（実際のプリコネ！グランドマスターズのアーキテクチャはリバースプロキシーを使った方式を採用しているので、この案とは異なります）

![image](https://user-images.githubusercontent.com/46207/164417734-f2ec80e7-f12f-4a84-8252-ce28f9b53f05.png)

パーツが増えて複雑になったように見えて、この構成には大きな利点があります。まず、同居しているものがなくなったので複雑になったようで実はシンプルになっています。それぞれがそれぞれの役割にフルに集中できるようになるため、パフォーマンスも良くなり、かつ、性能予測もしやすくなります。特にロジックをフルに回転させるLogicLooperがクライアントや接続数の影響を受けずに独立できているのは大きな利点です。

ゲーム全体のステートはLogicLooper自体が管理するため、クライアントとの接続を直接受けているMagicOnion自体はステートレスな状態です。そのため、インフラ的にもロードバランサーの下にMagicOnionを並べるだけで済みますし、サーバー間の接続に伴う面倒事は全てNATSに押し付けられるため、インフラ管理自体はかなりシンプルな構成が取れます。

また、MagicOnion自体はステートを持てるシステムであり、各ユーザーそれぞれのステートを持つのは容易です（サーバーを越えなければいい）。そこで、LogicLooperから届いたデータのうち、繋がってるユーザーに届ける必要がないデータは、MagicOnionの持つユーザーのステートを使ってカリング処理をして、そもそも転送しなかったり間引いたりして通信量を削減することで、ユーザーの体験が良くなります。

各ユーザーから届くデータを使ったステート更新/データ送信に関しては、LogicLooperがゲームループ状になっているので、ループの間に溜まったデータをもとにしてバッチ処理を行えばいいでしょう。バッチ化というと、通信「回数」の削減のためのコマンドを単純にまとめあげて一斉送信するものと、内容を見て処理内容を縮小するパターンが考えられますが、LogicLooperを使ったアプローチでは後者を効率的に行なえます。前者のコマンドの一斉送信に関しては、AlterNatsが裏側で自動パイプライニング化としてまとめているので（後で詳しく説明します）、そこに関しても効率化されています。

このアーキテクチャで気になるのがPubSub通信のオーバーヘッドですが、それに関しての解決策がAlterNatsで、究極的に高速なクライアントがあれば（さすがにインメモリには到底及ばないとはいえ）、そもそものクライアントとサーバーの間にもネットワークがいるわけで、経路のトータルで見れば実用的な範囲に収められる。という想定で作りました。

ところで、そして究極的な利点は、全てC#で組めるということです。どういうことかというと、MagicOnionもLogicLooperも汎用的なC#フレームワークです。特別なプラグインを差し込んで処理するというわけではなくて、ふつーのC#コードをふつーに書くことで、それぞれの箇所に、アプリケーション固有のコードを仕込んでいくことができる。これが、本当の大きな利点です。専用のC++ミドルウェアを作って挟んで最適化できるぞ！などといったシステムは、素晴らしいことですが、専門性が高く再現性が低い。MagicOnionとLogicLooper、そしてAlterNatsを活用したこの構成なら、C#エンジニアなら誰でも（容易に）できる構成です。[Cysharp](https://cysharp.co.jp/)のメッセージは「C#の可能性を切り開いていく」ですが、誰もが実現できる世界を作っていくというのが目標でもあります。

なお、ワーカーとしてのLogicLooperを作るに[Worker Service](https://docs.microsoft.com/ja-jp/dotnet/core/extensions/workers)という.NET 6からのプロジェクトタイプが適切です。

## ハイパフォーマンスSocketプログラミング

### Socket API

C#で最も低レベルにネットワーク処理を扱えるクラスは[Socket](https://docs.microsoft.com/ja-jp/dotnet/api/system.net.sockets.socket)です。そして、非同期でハイパフォーマンスな処理を求めるなら[SocketAsyncEventArgs](https://docs.microsoft.com/ja-jp/dotnet/api/system.net.sockets.socketasynceventargs)をうまく再利用しながらコールバックを仕込む必要があります。

これは非常に厄介で些か難易度も高いのですが、現在はasync/awaitの時代、ちゃんとawaitできる***Asyncメソッド郡が用意されています。しかし、使ってはいけないAPI、使ってはいけないオーバーロードも並んでいるので、その選別が必要です。SocketのAPIは歴史的事情もあり混沌としてしまっているのです……。

使うべきAPIを分かりやすく見分ける手段があります。それは戻り値が `ValueTask` のものを選ぶことです。

```csharp
public ValueTask ConnectAsync(string host, int port, CancellationToken cancellationToken)
public ValueTask<int> ReceiveAsync(Memory<byte> buffer, SocketFlags socketFlags, CancellationToken cancellationToken)
public ValueTask<int> SendAsync(ReadOnlyMemory<byte> buffer, SocketFlags socketFlags, CancellationToken cancellationToken))
```

オーバーロードにはTask返しのものもあるので、気をつけてください。

```csharp
// これらのAPIは使ってはいけない
public Task ConnectAsync(string host, int port)
public Task<int> ReceiveAsync(ArraySegment<byte> buffer, SocketFlags socketFlags)
public Task<int> SendAsync(ArraySegment<byte> buffer, SocketFlags socketFlags)
```

ValueTask返しのAPIは内部的には `AwaitableSocketAsyncEventArgs` というものがValueTaskの中身になるようになっていて、これがいい感じに使いまわされる(awaitされると内部に戻るようになっている）ことで、Taskのアロケーションもなく効率的な非同期処理を実現しています。`SocketAsyncEventArgs`の使いにくさとは雲泥の差なので、これは非常にお薦めできます。

### テキストプロトコルのバイナリコード判定

[NATSのプロトコル](https://docs.nats.io/reference/reference-protocols/nats-protocol)はテキストプロトコルになっていて、文字列処理で簡単に切り出すことができます。実際これはStreamReaderを使うことで簡単にプロトコルの実装ができます。ReadLineするだけですから。しかし、ネットワークに流れるのは(UTF8)バイナリデータであり、文字列化は無駄なオーバーヘッドとなるため、パフォーマンスを求めるなら、バイナリデータのまま処理する必要があります。

NATSでは先頭の文字列(`INFO`, `MSG`, `PING`, `+OK`, `-ERR`など)によって流れてくるメッセージの種類が判定できます。文字列処理で空白でSplitして if (msg == "INFO") などとすればめちゃくちゃ簡単ですが、先にも言った通り文字列変換は意地でも通しません。INFOは[73, 78, 70, 79]なので、Slice(0, 4).SequenceEqual で判定するのは悪くないでしょう。[`ReadOnlySpan<byte>`のSequenceEqual](https://docs.microsoft.com/ja-jp/dotnet/api/system.memoryextensions.sequenceequal)はめちゃくちゃ最適化されていて、長いものであれば必要であればSIMDとかも使って高速に同値判定します。LINQのSequenceEqualとは別物です！

しかし、もっと欲張って見てみましょう、プロトコルの識別子はサーバーから送られてくるものは全て4文字以内に収まっています。つまり、これはIntに変換しやすい状態です！というわけで、AlterNatsのメッセージ種判定コードはこうなっています。

```csharp
// msg = ReadOnlySpan<byte>
if(Unsafe.ReadUnaligned<int>(ref MemoryMarshal.GetReference<byte>(msg)) == 1330007625) // INFO
{
}
```

これ以上速い判定はできないと思うので、理論上最速ということでいいでしょう。3文字の命令も、直後に必ずスペースや改行が来るので、それを含めた以下のような定数を使って判定に回しています。

```csharp
internal static class ServerOpCodes
{
    public const int Info = 1330007625;  // Encoding.ASCII.GetBytes("INFO") |> MemoryMarshal.Read<int>
    public const int Msg = 541545293;    // Encoding.ASCII.GetBytes("MSG ") |> MemoryMarshal.Read<int>
    public const int Ping = 1196312912;  // Encoding.ASCII.GetBytes("PING") |> MemoryMarshal.Read<int>
    public const int Pong = 1196314448;  // Encoding.ASCII.GetBytes("PONG") |> MemoryMarshal.Read<int>
    public const int Ok = 223039275;     // Encoding.ASCII.GetBytes("+OK\r") |> MemoryMarshal.Read<int>
    public const int Error = 1381123373; // Encoding.ASCII.GetBytes("-ERR") |> MemoryMarshal.Read<int>
}
```

バイナリプロトコルなら特に何のひねりも必要なく実装できるので、バイナリプロトコルのほうが実装者に優しくて好きです……。

### 自動パイプライニング

NATSプロトコルの書き込み、読み込みは全てパイプライン（バッチ）化されています。これは[RedisのPipelining](https://redis.io/docs/manual/pipelining/)の解説が分かりやすいですが、例えばメッセージを3つ送るのに、一つずつ送って、都度応答を待っていると、送受信における多数の往復がボトルネックになります。

メッセージの送信において、AlterNatsは自動でパイプライン化しています。[System.Threading.Channels](https://devblogs.microsoft.com/dotnet/an-introduction-to-system-threading-channels/)を用いてメッセージは一度キューに詰め込まれ、書き込み用のループが一斉に取り出してバッチ化します。ネットワーク送信が完了したら、再び送信処理待ち中に溜め込まれたメッセージを一括処理していく、という書き込みループのアプローチを取ることで、最高速の書き込み処理を実現しました。

![image](https://user-images.githubusercontent.com/46207/167585601-5634057e-812d-4b60-ab5b-61d9c8c37063.png)

ラウンドトリップタイムの話だけではなく（そもそもNATSの場合はPublish側とSubscribe側が独立しているので応答待ちというのもないのですが）、システムコールの連続した呼び出し回数を削減できるという点でも効果が高いです。

なお、.NET最高速ロガーである[ZLogger](https://github.com/Cysharp/ZLogger/)でも同じアプローチを取っています。

### 一つのオブジェクトに機能を盛る

Channelに詰め込む都合上、データを書き込みメッセージオブジェクトに入れてヒープに保持しておく必要があります。また、書き込み完了まで待つ非同期メソッドのためのPromiseも必要です。

```csharp
await connection.PublishAsync(value);
```

こうしたAPIを効率よく実装するために、どうしても確保する必要のある一つのメッセージオブジェクト（内部的にはCommandと命名されている）に、あらゆる機能を同居して詰め込みましょう。

```csharp
class AsyncPublishCommand<T> : ICommand, IValueTaskSource, IThreadPoolWorkItem, IObjectPoolNode<AsyncPublishCommand<T>>

internal interface ICommand
{
    void Write(ProtocolWriter writer);
}

internal interface IObjectPoolNode<T>
{
    ref T? NextNode { get; }
}
```

このオブジェクト(`AsyncPublishCommand<T>`)自体は、T dataを保持して、Socketにバイナリデータとして書き込むための役割(`ICommand`)をまずは持っています。

それに加えて[IValueTaskSource](https://docs.microsoft.com/ja-jp/dotnet/api/system.threading.tasks.sources.ivaluetasksource)であることにより、このオブジェクト自身がValueTaskになります。

そしてawait時のコールバックとして、書き込みループを阻害しないためにThreadPoolに流す必要があります。そこで従来の`ThreadPool.QueueUserWorkItem(callback)`を使うと、内部的には `ThreadPoolWorkItem` を生成してキューに詰め込むため、余計なアロケーションがあります。 .NET Core 3.0から[IThreadPoolWorkItem](https://docs.microsoft.com/ja-jp/dotnet/api/system.threading.ithreadpoolworkitem)を実装することで、内部の`ThreadPoolWorkItem`の生成をなくすことができます。

最後に、同居させることで必要なオブジェクトが1つになりましたが、その1つをプーリングしてゼロアロケーション化します。オブジェクトプールは`ConcurrentQueue<T>`などを使うと簡単に実装できますが、自分自身をStackのNodeにすることで、配列を確保しないで済むようにしています。また、Nodeの出し入れに関しては、今回のキャッシュの実装では正確に取り出せる必要性はないため、lockは使わず、マルチスレッドで競合が発生した場合はキャッシュミス扱いにして新規生成するようにしています。これはオブジェクトプーリングにおける性能バランスとしては、良いチョイスだと考えています。

```csharp
internal sealed class ObjectPool<T>
    where T : class, IObjectPoolNode<T>
{
    int gate;
    int size;
    T? root;
    readonly int limit;

    public ObjectPool(int limit)
    {
        this.limit = limit;
    }

    public int Size => size;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryPop([NotNullWhen(true)] out T? result)
    {
        // Instead of lock, use CompareExchange gate.
        // In a worst case, missed cached object(create new one) but it's not a big deal.
        if (Interlocked.CompareExchange(ref gate, 1, 0) == 0)
        {
            var v = root;
            if (!(v is null))
            {
                ref var nextNode = ref v.NextNode;
                root = nextNode;
                nextNode = null;
                size--;
                result = v;
                Volatile.Write(ref gate, 0);
                return true;
            }

            Volatile.Write(ref gate, 0);
        }
        result = default;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryPush(T item)
    {
        if (Interlocked.CompareExchange(ref gate, 1, 0) == 0)
        {
            if (size < limit)
            {
                item.NextNode = root;
                root = item;
                size++;
                Volatile.Write(ref gate, 0);
                return true;
            }
            else
            {
                Volatile.Write(ref gate, 0);
            }
        }
        return false;
    }
}
```    

### Zero-copy Architecture

Publish/Subscribeするデータは通常、C#の型をJSONやMessagePackなどにシリアライズしたものを流します。この場合、どうしてもbyte[]でやり取りすることが多くなります、例えばStackExchange.Redisの`RedisValue`の中身は実質byte[]で、送信にせよ受信にせよ、byte[]を生成して保持することになります。

これを避けるために、ArrayPoolから出し入れしてごまかしてゼロアロケーションにする、みたいなのはありがちではありますが、それでもコピーのコストが発生していることには代わりありません。ゼロアロケーションは当然目指すところですが、ゼロコピーに向けても頑張りましょう！

AlterNatsのシリアライザーはWriteに`IBufferWriter<byte>`, Readに`ReadOnlySequence<byte>`を要求します。

```csharp
public interface INatsSerializer
{
    int Serialize<T>(ICountableBufferWriter bufferWriter, T? value);
    T? Deserialize<T>(in ReadOnlySequence<byte> buffer);
}

public interface ICountableBufferWriter : IBufferWriter<byte>
{
    int WrittenCount { get; }
}
```

```csharp
// 例えばMessagePack for C#を使う場合の実装
public class MessagePackNatsSerializer : INatsSerializer
{
    public int Serialize<T>(ICountableBufferWriter bufferWriter, T? value)
    {
        var before = bufferWriter.WrittenCount;
        MessagePackSerializer.Serialize(bufferWriter, value);
        return bufferWriter.WrittenCount - before;
    }

    public T? Deserialize<T>(in ReadOnlySequence<byte> buffer)
    {
        return MessagePackSerializer.Deserialize<T>(buffer);
    }
}
```

System.Text.JsonやMessagePack for C#のSerializeメソッドには`IBufferWriter<byte>`を受け取るオーバーロードが用意されています。`IBufferWriter<byte>`経由でSocketに書き込むために用意しているバッファーにシリアライザが直接アクセスし、書き込みすることで、Socketとシリアライザ間でのbyte[]のコピーをなくします。

![image](https://user-images.githubusercontent.com/46207/167587816-c50b0af3-edaa-4a2a-b536-67aed0a5f908.png)

Read側では、[`ReadOnlySequence<byte>`](https://docs.microsoft.com/ja-jp/dotnet/api/system.buffers.readonlysequence-1)を要求します。Socketからのデータの受信は断片的な場合も多く、それをバッファのコピーと拡大ではなく、連続した複数のバッファを一塊として扱うことでゼロコピーで処理するために用意されたクラスが`ReadOnlySequence<T>`です。

「ハイパフォーマンスの I/O をより簡単に行えるように設計されたライブラリ」である[System.IO.Pipelines](https://docs.microsoft.com/ja-jp/dotnet/standard/io/pipelines)の`PipeReader`で読み取ったものを扱うのが、よくあるパターンとなります。ただし、AlterNatsではPipelinesは使わずに独自の読み取り機構と`ReadOnlySequence<byte>`を使用しました。

System.Text.JsonやMessagePack for C#のSerializeメソッドには`IBufferWriter<byte>`を受け取るオーバーロードが用意されているため、それを直接渡すことができます。つまり、現代的なシリアライザは`IBufferWriter<byte>`と`ReadOnlySequence<byte>`のサポートは必須です。これらをサポートしていないシリアライザはそれだけで失格です。


## まとめ

プロトコルが単純で少ないのでちゃちゃっと作れると思いきや、まあ確かに雑にTcpClientとStreamReader/Writerでやれば秒殺だったのですが、プロトコルって量産部分でしかないので、そこがどんだけ量少なかろうと、基盤の作り込みは相応に必要で、普通に割と時間かかってしまった、のですが結構良い感じに作れたと思います。コード的にも例によって色々な工夫が盛り込まれていますので、是非ソースコードも読んでみてください。

クライアント側の実装によってパフォーマンスが大きく違うというのはシリアライザでもよくあり経験したことですが、NATSのパフォーマンスを論じるにあたって、その言語のクライアントは大丈夫ですか？というところがあり、そして、C#は大丈夫ですよ、と言えるものになっていると思います。

NATSの活用に関してはこれからやっていくので実例あるんですか？とか言われると知らんがな、というところですが（ところでMagicOnionはこないだの[プリコネ！グランドマスターズ](https://neue.cc/2022/04/08_priconne-grandmasters.html)だけではなく最近特によくあるので、実例めっちゃあります）、これから色々使っていこうかなと思っているので、まぁ是非AlterNatsと共に試してみてください。