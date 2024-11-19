# CysharpのOSS Top10まとめ / Ulid vs .NET 9 UUID v7 / MagicOnion

「CysharpのOSS群から見るModern C#の現在地」というタイトルでセッションしてきました。

<script defer class="speakerdeck-embed" data-id="73bfd578c3324a6e8ce74457445fe9c0" data-ratio="1.7777777777777777" src="//speakerdeck.com/assets/embed.js"></script>

作りっぱなし、というわけではないですが（比較的メンテナンスしてるとは思います！）、リリースから年月が経ったライブラリをどう思っているかは見えないところありますよね、というわけで、その辺を軽く伝えられたのは良かったのではないかと思います。

この中だと非推奨に近くなっているのが[ZString](https://github.com/Cysharp/ZString)と[Ulid](https://github.com/Cysharp/Ulid)でしょうか。

Ulid vs .NET 9 UUID v7
---
スライドにも書きましたが、ULIDをそこそこ使ってきての感想としては、「Guidではないこと」が辛いな、と。独自文字列形式とか要らないし。そんなわけで私はむしろUUID v7のほうを薦めたいレベルだったりはします。.NET 9から`Guid.CreateVersion7()`という形で、標準で生成できるようになりました。

パフォーマンス的なところは些細なことなので問題ないのですが、 .NET 9未満との互換性が取れないのは厳しいところかもしれません。というわけで、自作のV7実装を用意してあげるといいでしょう。以下に置いておきますのでどうぞ（コードのベースはdotnet/runtimeのCCreateVersion7です）

```Csharp
public static class GuidEx
{
    private const byte Variant10xxMask = 0xC0;
    private const byte Variant10xxValue = 0x80;
    private const ushort VersionMask = 0xF000;
    private const ushort Version7Value = 0x7000;

    public static Guid CreateVersion7() => CreateVersion7(DateTimeOffset.UtcNow);

    public static Guid CreateVersion7(DateTimeOffset timestamp)
    {
        // 普通にGUIDを作る
        Guid result = Guid.NewGuid();

        // 先頭48bitをいい感じに埋める
        var unix_ts_ms = timestamp.ToUnixTimeMilliseconds();

        // GUID layout is int _a; short _b; short _c, byte _d;
        Unsafe.As<Guid, int>(ref Unsafe.AsRef(ref result)) = (int)(unix_ts_ms >> 16); // _a
        Unsafe.Add(ref Unsafe.As<Guid, short>(ref Unsafe.AsRef(ref result)), 2) = (short)(unix_ts_ms); // _b

        ref var c = ref Unsafe.Add(ref Unsafe.As<Guid, short>(ref Unsafe.AsRef(ref result)), 3);
        c = (short)((c & ~VersionMask) | Version7Value);

        ref var d = ref Unsafe.Add(ref Unsafe.As<Guid, byte>(ref Unsafe.AsRef(ref result)), 8);
        d = (byte)((d & ~Variant10xxMask) | Variant10xxValue);

        return result;
    }

    // GuidにはTimestamp部分を取り出すメソッドがないので、これも用意してあげると便利
    public static DateTimeOffset GetTimestamp(in Guid guid)
    {
        // エンディアンについては特に考慮してません
        ref var p = ref Unsafe.As<Guid, byte>(ref Unsafe.AsRef(in guid));
        var lower = Unsafe.ReadUnaligned<uint>(ref p);
        var upper = Unsafe.ReadUnaligned<ushort>(ref Unsafe.Add(ref p, 4));
        var time = (long)upper + (((long)lower) << 16);
        return DateTimeOffset.FromUnixTimeMilliseconds(time);
    }
}
```

UUID v7のよくあるユースケースはDBの主キーにGUID(UUID v4)の代わりに使う、ということです。UUID v4だとランダムに配置されるので断片化して、auto incrementの主キーに比べると色々と遅くなる。それがv7だとランダムの性質を持ちつつも配置場所はタイムスタンプベースなのでauto incrementと同様になるため性能劣化がない。

という理屈を踏まえたうえで、.NETのUUID v7事情を踏まえると単純に置き換えるだけで良い、とはなりません。

GUIDは内部的なバイナリデータとしてはリトルエンディアンで保持していて、出力時に切り分けるというデザインになっています(無指定の場合はlittleEndianでの出力)。

```csharp
public readonly struct Guid
{
    public byte[] ToByteArray()
    public byte[] ToByteArray(bool bigEndian)
    public bool TryWriteBytes(Span<byte> destination)
    public bool TryWriteBytes(Span<byte> destination, bool bigEndian, out int bytesWritten)
}
```

String(char36)として格納するなら気にしなくてもいいのですが、GUID型やバイナリ型としてデータベースに格納する時は、UUID v7に関してはビッグエンディアンで書き出さないと、ソート可能にならない非常に都合が悪い。これのハンドリングは言語のデータベースドライバーライブラリの責務となっています。

代表的なライブラリを見ていくと、MySQLの[mysqlconnector-netはコネクションストリング](https://mysqlconnector.net/connection-options/)で `GuidFormat=Binary16` を指定することでbig-endianでBINARY(16)に書き込む設定となります。

PostgreSQLの場合、[npgsqlのGuidUuidConverter](https://github.com/npgsql/npgsql/blob/94de20fed2e7e64a1eb6f26c9fc044131a362958/src/Npgsql/Internal/Converters/Primitive/GuidUuidConverter.cs#L29)が常にbigEndianとして処理するようになっているようです。

ではMicrosoft SQL Serverはどうかというと、ばっちしlittle-endianです。ダメです。というわけで、性能を期待してCreateVersion7を使うと、逆に断片化して遅くなるような憂き目にあいます。

こちらは[dotnet/SqlClientのdiscussions#2999](https://github.com/dotnet/SqlClient/discussions/2999)で議論されているようなので、成り行きに注目ということで。今までとの互換性などを考えると一括でbigにしてしまえばいいじゃん、というわけにもいかないしで、中々素直にはいかないかもしれませんね……。

なお、このことは別に.NET 9がリリースされる前にもわかっていたことなのに（私でもダメだという状況は把握していた）、リリースされるまでアクションが全く起きないというところに、今のSQL Serverへのやる気を感じたりなかったり。

MagicOnion
---
イベントではCysharpの @mayuki さんからMagicOnionの入門セッションもありました！

<script defer class="speakerdeck-embed" data-id="d5b4ad47f5cd4e9f984022e64d623d51" data-ratio="1.7777777777777777" src="//speakerdeck.com/assets/embed.js"></script>

MagicOnionも2016年の初リリース、2018年のリブート(v2)、googleのgRPC C Coreからgrpc-dotnetベースへの変更、クライアントのHttpClientベースへの変更など、内部的には色々変わってきたし機能面でも磨かれてきています。まだまだ次のアップデートが控えている、最前線で戦える強力なフレームワークとなっています！