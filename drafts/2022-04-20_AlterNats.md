# AlterNats - //TODO:いい感じのタイトル



// TODO:なんか図とかいつものとか


// TODO:API解説とか簡単に




## メタバースアーキテクチャ

// TODO:MagicOnionとか絡めて


// TODO:バッチとパイプライニングについて何か書く


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
````

### 自動パイプライニング

NATSプロトコルの書き込み、読み込みは全てパイプライン（バッチ）化されています。これは[RedisのPipelining](https://redis.io/docs/manual/pipelining/)の解説が分かりやすいですが、例えばメッセージを3つ送るのに、一つずつ送って、都度応答を待っていると、送受信における多数の往復がボトルネックになります。

メッセージの送信において、AlterNatsは自動でパイプライン化しています。[System.Threading.Channels](https://devblogs.microsoft.com/dotnet/an-introduction-to-system-threading-channels/)を用いてメッセージは一度キューに詰め込まれ、書き込み用のループが一斉に取り出してバッチ化します。ネットワーク送信が完了したら、再び送信処理待ち中に溜め込まれたメッセージを一括処理していく、という書き込みループのアプローチを取ることで、最高速の書き込み処理を実現しました。

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

// TODO:ObjectPoolの説明










### Zero-copy Architecture


TODO: ReadOnlySequence とかSystem.IO.Pipeline

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


## まとめ

プロトコルが単純で少ないのでちゃちゃっと作れると思いきや、まあ確かに雑にTcpClientとStreamReader/Writerでやれば秒殺だったのですが、プロトコルって量産部分でしかないので、そこがどんだけ量少なかろうと、基盤の作り込みは相応に必要で、普通に割と時間かかってしまった、のですが結構良い感じに作れたと思います。コード的にも例によって色々な工夫が盛り込まれていますので、是非読んでみてください。







