# MemoryPackにみる .NET 7/C# 11世代のシリアライザー最適化技法

[MemoryPack](https://github.com/Cysharp/MemoryPack)という、C#に特化することで従来のシリアライザーとは比較にならないほどのパフォーマンスを発揮する新しいシリアライザーを新しく開発しました。

![](https://user-images.githubusercontent.com/46207/192748136-262ac2e7-4646-46e1-afb8-528a51a4a987.png)

高速なバイナリシリアライザーである [MessagePack for C#](https://github.com/neuecc/MessagePack-CSharp) と比較しても、通常のオブジェクトでも数倍、データが最適な場合は50~100倍ほどのパフォーマンスにもなります。System.Text.Jsonとでは全く比較になりません。当初は .NET 7 限定としてリリースしましたが、現在は .NET Standard 2.1(.NET 5, 6)やUnity、そしてTypeScriptにも対応しています。

シリアライザーのパフォーマンスは「データフォーマットの仕様」と「各言語における実装」の両輪で成り立っています。例えば、一般的にはバイナリフォーマットのほうがテキストフォーマット（JSONとか）よりも有利ですが、バイナリシリアライザーより速いJSONシリアライザといったものは有り得ます(Utf8Jsonでそれを実証しました)。では最速のシリアライザーとは何なのか？というと、仕様と実装を突き詰めれば、真の最速のシリアライザーが誕生します。

私は、今もですが、長年[MessagePack for C#](https://github.com/neuecc/MessagePack-CSharp)の開発とメンテナンスをしてきました。MessagePack for C#は .NET の世界で非常に成功したシリアライザーで、4000以上のGitHub Starと、Visual Studio内部や、SignalR, Blazor Serverのバイナリプロトコルなど、Microsoftの標準プロダクトにも採用されています。また、この5年間で1000近くのIssueをさばいてきました。そのため、シリアライザーの実装の詳細からユーザーのリアルなユースケース、要望、問題などを把握しています。Roslynを使用したコードジェネレーターによるAOT対応にも当初から取り組み、特にAOT環境(IL2CPP)であるUnityで実証してきました。更にMessagePack for C#以外にも ZeroFormatter(独自フォーマット)、Utf8Json(JSON) といった、これも多くのGitHub Starを獲得したシリアライザーを作成してきているため、異なるフォーマットの性能特性についても深く理解しています。シリアライザーを活用するシチュエーションにおいても、RPCフレームワーク[MagicOnion](https://github.com/Cysharp/MagicOnion/)の作成、インメモリデータベース[MasterMemory](https://github.com/Cysharp/MasterMemory)、そして複数のゲームタイトルにおけるクライアント(Unity)/サーバー、両方の実装に関わってきました。

ようするところ私は .NET のシリアライザー実装について最も詳しい人間の一人であり、MemoryPackはその知見がフルに詰め込まれた、なおかつ、 .NET 7 / C# 11という最新のランタイム/言語機能を使い倒したライブラリになっています。そりゃ速くて当然で異論はないですよね？

というだけではアレなので、実際なんで速いのかというのを理屈で説明していきます……！きっと納得してもらえるはず！ C#の最適化のTipsとしてもどうぞ。

Incremental Source Generator
---
MemoryPackでは .NET 5/C# 9.0 から追加された [Source Generator](https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/source-generators-overview)、それも .NET 6 で強化された [Incremental Source Generator](https://github.com/dotnet/roslyn/blob/main/docs/features/incremental-generators.md)を全面的に採用しています。使い方的には、対象型をpartialに変更する程度で、MessagePack for C#とあまり変わりません（というか極力同じAPIになるように揃えました）。

```csharp
using MemoryPack;

[MemoryPackable]
public partial class Person
{
    public int Age { get; set; }
    public string Name { get; set; }
}

// usage
var v = new Person { Age = 40, Name = "John" };

var bin = MemoryPackSerializer.Serialize(v);
var val = MemoryPackSerializer.Deserialize<Person>(bin);
```

Source Generatorの最大の利点はAOTフレンドリーであることで、従来行っていたIL.Emitによる動的コード生成をせずとも、リフレクションを使用しない、各型に最適化されたシリアライザーコードを自動生成しています。それによりUnityのIL2CPPなどでも安全に動作させることが可能です。

MessagePack for C#では外部ツール(mpc.exe)経由でコード生成することでAOTセーフなシリアライズ処理を実現していましたが、言語機能と統合されたことによって、煩わしい生成プロセス不要で、自然な書き心地のまま高速なシリアライズ処理を可能にしました。

なお、Unity版の場合は言語/コンパイラバージョンの都合上、Incremental Source Generatorではなくて、古いSource Generatorを採用しています。

C#のためのバイナリ仕様
---
キャッチコピーは「Zero encoding」ということで、エンコードしないから速いんだ！という理論を打ち出しています。奇妙に思えて、実のところ別に特殊な話をしているわけではなくて、例えばRustのメジャーなバイナリシリアライザーである[bincode](https://github.com/bincode-org/bincode)なども似通った仕様を持っています。[FlatBuffers](https://github.com/google/flatbuffers)も、without parsingな実装のために、メモリデータに近い内容を読み書きします。ただしMemoryPackはFlatBuffersなどと違い、特別な型を必要としない汎用的なシリアライザーであり、POCOに対してのシリアライズ/デシリアライズを行うものです。また、スキーマのメンバー追加へのバージョニング耐性やポリモーフィズムサポート(Union)も持ちます。さすがにメモリダンプしてるだけ、では全く実用にならないわけで、一般的なシリアライザーとして使えるための仕様として整えてあります。

## varint encoding

Int32は4バイトですが、例えばJSONでは数値を文字列として、1バイト~11バイト(例えば `1` であったり `-2147483648` であったり)の可変長なエンコーディングが施されます。バイナリフォーマットでも、サイズの節約のために1~5バイトの可変長にエンコードされる仕様を持つものが多くあります。例えば[Protocol Buffersの数値型](https://developers.google.com/protocol-buffers/docs/encoding#int-types)はZigZagエンコーディングという、値を7ビットに、後続があるかないかのフラグを1ビットに格納する可変長整数エンコーディングになっています。これにより数値が小さければ小さいほど、バイト数が少なくなります。逆にワーストケースでは本来の4バイトより大きい5バイトに膨れることになります。とはいえ現実的には小さい数値のほうが圧倒的に頻出するはずなので、とても理にかなった方式です。[MessagePack](https://github.com/msgpack/msgpack/blob/master/spec.md)や[CBOR](https://cbor.io/)も同じように、小さい数値では最小で1バイト、大きい場合は最大5バイトになる可変長エンコーディングで処理されます。

つまり、固定長の場合よりも余計な処理が走ることになります。具体的なコードで比較してみましょう。可変長はprotobufで使われるZigZagエンコーディングです。

```csharp
// 固定長の場合
static void WriteFixedInt32(Span<byte> buffer, int value)
{
    ref byte p = ref MemoryMarshal.GetReference(buffer);
    Unsafe.WriteUnaligned(ref p, value);
}

// 可変長の場合
static void WriteVarInt32(Span<byte> buffer, int value) => WriteVarInt64(buffer, (long)value);

static void WriteVarInt64(Span<byte> buffer, long value)
{
    ref byte p = ref MemoryMarshal.GetReference(buffer);

    ulong n = (ulong)((value << 1) ^ (value >> 63));
    while ((n & ~0x7FUL) != 0)
    {
        Unsafe.WriteUnaligned(ref p, (byte)((n & 0x7f) | 0x80));
        p = ref Unsafe.Add(ref p, 1);
        n >>= 7;
    }
    Unsafe.WriteUnaligned(ref p, (byte)n);
}
```

固定長は、つまりC#のメモリをそのまま書き出している(Zero encoding)わけで、さすがにどう見ても固定長のほうが速いでしょう。

このことは配列に適用した場合、より顕著になります。

```csharp
// https://sharplab.io/
Inspect.Heap(new int[]{ 1, 2, 3, 4, 5 });
```

![image](https://user-images.githubusercontent.com/46207/199924027-492a163c-9bd9-41e7-8489-4f5aa61cac52.png)

C#のstructの配列は、データが直列に並びます。この時、[structが参照型を持っていない場合(unmanaged type)](https://learn.microsoft.com/ja-jp/dotnet/csharp/language-reference/builtin-types/unmanaged-types)は、データが完全にメモリ上に並んでいることになります。MessagePackとMemoryPackでコードでシリアライズ処理を比較してみましょう。

```csharp
// 固定長の場合(実際には長さも書き込みます)
void Serialize(int[] value)
{
    // サイズが算出可能なので事前に一発で確保
    var size = (sizeof(int) * value.Length) + 4;
    EnsureCapacity(size);

    // 一気にメモリコピー
    MemoryMarshal.AsBytes(value.AsSpan()).CopyTo(buffer);
}

// 可変長の場合
void Serialize(int[] value)
{
    foreach (var item in value)
    {
        // サイズが不明なので都度バッファサイズのチェック
        EnsureCapacity(); // if (buffer.Length < writeLength) Resize();
        // 1要素毎に可変長エンコード
        WriteVarInt32(item);
    }
}
```

固定長の場合は、多くのメソッド呼び出しを省いて、メモリコピー一発だけで済ませることが可能です。

C#の配列はintのようなプリミティブ型だけではなく、これは複数のプリミティブを持ったstructでも同様の話で、例えば(float x, float y, float z)を持つVector3の配列の場合は、以下のようなメモリレイアウトになります。

![image](https://user-images.githubusercontent.com/46207/199926307-bad558f9-b912-4b96-90fc-5c2d1a2837ea.png)

float(4バイト)はMessagePackにおいて、固定長で5バイトです。追加の1バイトは、その値が何の型(IntなのかFloatなのかStringなのか...)を示す識別子が先頭に入ります。具体的には[0xca, x, x, x, x]といったように。いわばタグ付与エンコーディングを行っているわけです。MemoryPackのフォーマットは識別子を持たないため、4バイトをそのまま書き込みます。

ベンチマークで50倍の差だった、Vector3[10000]で考えてみましょう。

```csharp
// 以下の型がフィールドにあるとする
// byte[] buffer
// int offset

void SerializeMemoryPack(Vector3[] value)
{
    // どれだけ複雑だろうとコピー一発で済ませられる
    var size = Unsafe.SizeOf<Vector3>() * value.Length;   
    if ((buffer.Length - offset) < size)
    {
        Array.Resize(ref buffer, buffer.Length * 2);
    }
    MemoryMarshal.AsBytes(value.AsSpan()).CopyTo(buffer.AsSpan(0, offset))
}

void SerializeMessagePack(Vector3[] value)
{
    // 配列の長さ x フィールドの数だけ繰り返す
    foreach (var item in value)
    {
        // X
        {
            // EnsureCapacity
            if ((buffer.Length - offset) < 5)
            {
                // 実際にはResizeではなくてbufferWriter.Advance()です
                Array.Resize(ref buffer, buffer.Length * 2);
            }
            var p = MemoryMarshal.GetArrayDataReference(buffer);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref p, offset), (byte)0xca);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref p, offset + 1), item.X);
            offset += 5;
        }
        // Y
        {
            if ((buffer.Length - offset) < 5)
            {
                Array.Resize(ref buffer, buffer.Length * 2);
            }
            var p = MemoryMarshal.GetArrayDataReference(buffer);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref p, offset), (byte)0xca);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref p, offset + 1), item.Y);
            offset += 5;
        }
        // Z
        {
            if ((buffer.Length - offset) < 5)
            {
                Array.Resize(ref buffer, buffer.Length * 2);
            }
            var p = MemoryMarshal.GetArrayDataReference(buffer);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref p, offset), (byte)0xca);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref p, offset + 1), item.Z);
            offset += 5;
        }
    }
}
```

MessagePackだと30000回のメソッド呼び出しが必要なところが(そしてそのメソッド内では、書き込みメモリが足りているかのチェックと、書き終わった後のオフセットの追加が(愚直に処理する場合)都度必要になる)、一回のメモリコピーだけになります。こうなると、処理時間が文字通り桁違いに変わってきて、冒頭のグラフの50倍~100倍の高速化の理由はここにあります。

もちろん、デシリアライズ処理もコピー一発になります。

```csharp
// MemoryPackのデシリアライズ、コピーするだけ。
Vector3[] DeserializeMemoryPack(ReadOnlySpan<byte> buffer, int size)
{
    var dest = new Vector3[size];
    MemoryMarshal.Cast<byte, Vector3>(buffer).CopyTo(dest);
    return dest;
}

// ループで都度floatの読み取りが必要
Vector3[] DeserializeMessagePack(ReadOnlySpan<byte> buffer, int size)
{
    var dest = new Vector3[size];
    for (int i = 0; i < size; i++)
    {
        var x = ReadSingle(buffer);
        buffer = buffer.Slice(5);
        var y = ReadSingle(buffer);
        buffer = buffer.Slice(5);
        var z = ReadSingle(buffer);
        buffer = buffer.Slice(5);
        dest[i] = new Vector3(x, y, z);
    }
    return dest;
}
```

この辺は、MessagePackのフォーマットそのものの限界のため、仕様に従う限りは、圧倒的な速度差はどうやっても覆せません。ただしMessagePackの場合はext format familyという仕様があり、独自仕様としてこれらの配列だけ特別扱いして処理する（MessagePackとしての互換性はなくなりますが）ことも許されています。実際、MessagePack for C#ではUnity向けに `UnsafeBlitResolver` という、上記のような処理をする特別な拡張オプションを用意していました。

しかし恐らく、ほとんどの人が使っていないでしょう。別に普通にシリアライズできるものを、言語間運用製を壊す、C#だけの独自拡張オプションをわざわざ使おうとは、中々思わない、というのは分かります。そこがまた歯痒かったんですよね、明らかに遅いのに、明らかに速くできるのに、だからせっかく用意したのに、デフォルトではない限り使われない、しかしデフォルトは絶対に仕様に従うべきであり……。

## string処理の最適化

MemoryPackではStringに関して、2つの仕様を持っています。UTF8か、UTF16か、です。C#のstringはUTF16のため、UTF16のままシリアライズすると、UTF8へのエンコード/デコードコストを省くことができます。

```csharp
void EncodeUtf16(string value)
{
    var size = value.Length * 2;
    EnsureCapacity(size);

    // char[] -> byte[] -> Copy
    MemoryMarshal.AsBytes(value.AsSpan()).CopyTo(buffer);
}

string DecodeUtf16(ReadOnlySpan<byte> buffer, int length)
{
    ReadOnlySpan<char> src = MemoryMarshal.Cast<byte, char>(buffer).Slice(0, length);
    return new string(src);
}
```

ただし、MemoryPackのデフォルトはUTF8です。これは単純にペイロードのサイズの問題で、UTF16だとASCII文字が2倍のサイズになってしまうため、UTF8にしました（なお、日本語の場合はUTF16のほうがむしろ縮まる可能性が高いです）。

UTF8の場合でも、他のシリアライザにはない最適化をしています。

```csharp
void WriteUtf8MemoryPack(string value)
{
    var source = value.AsSpan();
    var maxByteCount = (source.Length + 1) * 3;
    EnsureCapacity(maxByteCount);
    Utf8.FromUtf16(source, dest, out var _, out var bytesWritten, replaceInvalidSequences: false);
}

void WriteUtf8StandardSerializer(string value)
{
    var maxByteCount = Encoding.UTF8.GetByteCount(value);
    EnsureCapacity(maxByteCount);
    Encoding.UTF8.GetBytes(value, dest);
}
```

`var bytes = Encoding.UTF8.GetBytes(value);` は論外です、stringの書き込みで `byte[]` のアロケーションは許されません。しかし、多くのシリアライザはで使われている `Encoding.UTF8.GetByteCount` も避けるべきです、UTF8は可変長のエンコーディングであり、 GetByteCount は正確なエンコード後のサイズを算出するために、文字列を完全に走査します。つまり GetByteCount -> GetBytes は文字列を二度も走査することになります。

通常シリアライザーは余裕を持ったバッファの確保が許されています。そこでMemoryPackではUTF8エンコードした場合のワーストケースである文字列長の3倍の確保にすることで、二度の走査を避けています。

デコードの場合は、更に特殊な最適化を施しています。

```csharp
string ReadUtf8MemoryPack(int utf16Length, int utf8Length)
{
    unsafe
    {
        fixed (byte* p = &buffer)
        {
            return string.Create(utf16Length, ((IntPtr)p, utf8Length), static (dest, state) =>
            {
                var src = MemoryMarshal.CreateSpan(ref Unsafe.AsRef<byte>((byte*)state.Item1), state.Item2);
                Utf8.ToUtf16(src, dest, out var bytesRead, out var charsWritten, replaceInvalidSequences: false);
            });
        }
    }
}

string ReadStandardSerialzier(int utf8Length)
{
    return Encoding.UTF8.GetString(buffer.AsSpan(0, utf8Length));
}
```

通常、byte[]からstringを取り出すには Encoding.UTF8.GetString(buffer) を使います。MessagePack for C#でもそうです。しかし、改めて、UTF8は可変長のエンコーディングであり、そこからUTF16としての長さは分かりません。そのためUTF8.GetStringだと、stringに変換するためのUTF16としての長さ算出が必要なので、中では文字列を二度走査しています。擬似コードでいうと

```csharp
var length = CalcUtf16Length(utf8data);
var str = String.Create(length);
Encoding.Utf8.DecodeToString(utf8data, str);
```

といったことになっています。一般的なシリアライザの文字列フォーマットはUTF8であり、当たり前ですがUTF16へのデコードなどといったことは考慮されていないため、C#の文字列としての効率的なデコードのためにUTF16の長さが欲しくても、データの中にはありません。

しかしMemoryPackの場合はC#を前提においた独自フォーマットのため、文字列はUTF16-LengthとUTF8-Lengthの両方(8バイト)をヘッダに記録しています。そのため、`String.Create<TState>(Int32, TState, SpanAction<Char,TState>)` と[Utf8.ToUtf16](https://learn.microsoft.com/en-us/dotnet/api/system.text.unicode.utf8.toutf16)の組み合わせにより、最も効率的なC# Stringへのデコードを実現しました。

ペイロードサイズについて
---
MemoryPackは固定長エンコーディングのため可変長エンコーディングに比べてどうしてもサイズが膨らむ場合があります。特にlongを可変長エンコードすると最小1バイトになるので、固定長8バイトに比べると大きな差となり得ます。しかし、MemoryPackはフィールド名を持たない(JSONやMessagePackのMap)ことやTagがないことなどから、JSONよりも小さいのはもちろん、可変長エンコーディングを持つprotobufやMsgPackと比較しても大きな差となることは滅多にないと考えています。

データは別に整数だけじゃないので、真にサイズを小さくしたければ、圧縮(LZ4やZStandardなど)を考えるべきですし、圧縮してしまえばあえて可変長エンコーディングする意味はほぼなくなります。より特化して小さくしたい場合は、列指向圧縮にしたほうがより大きな成果を得られる(Apache Parquetなど)ので、現代的には可変長エンコーディングを採用するほうがデメリットは大きいのではないか？と私は考えています。冒頭でも少し紹介しましたが、実際Rustのシリアライザー[bincode](https://github.com/bincode-org/bincode)のデフォルトは固定長だったりします。

MemoryPackの実装と統合された効率的な圧縮については、現在BrotliEncode/Decodeのための補助クラスを標準で用意しています。しかし、性能を考えるとLZ4やZStandardを使えたほうが良いため、将来的にはそれらの実装も提供する予定です。

.NET 7 / C#11を活用したハイパフォーマンスシリアライザーのための実装
---
MemoryPackは .NET Standard 2.1向けの実装と .NET 7向けの実装で、メソッドシグネチャが若干異なります。.NET 7向けには、最新の言語機能を活用した、より性能を追求したアグレッシブな実装になっています。

まずシリアライザのインターフェイスは以下のような static abstract membersが活用されています。

```csharp
public interface IMemoryPackable<T>
{
    // note: serialize parameter should be `ref readonly` but current lang spec can not.
    // see proposal https://github.com/dotnet/csharplang/issues/6010
    static abstract void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, scoped ref T? value)
        where TBufferWriter : IBufferWriter<byte>;
    static abstract void Deserialize(ref MemoryPackReader reader, scoped ref T? value);
}
```

MemoryPackはSource Generatorを採用し、対象型が `[MemortyPackable]public partial class Foo` であることを要求するため、最終的に対象型は

```csharp
[MemortyPackable]
partial class Foo : IMemoryPackable
{
    static void IMemoryPackable<Foo>.Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, scoped ref Foo? value) 
    {
    }
        
    static void IMemoryPackable<Foo>.Deserialize(ref MemoryPackReader reader, scoped ref Foo? value)
    {
    }
}
```

といったものを生成します。これにより、仮想メソッド経由呼び出しのコストを避けています。

```csharp
public void WritePackable<T>(scoped in T? value)
    where T : IMemoryPackable<T>
{
    // IMemoryPackableが対象の場合、静的メソッドを直接呼び出しに行く
    T.Serialize(ref this, ref Unsafe.AsRef(value));
}

// 
public void WriteValue<T>(scoped in T? value)
{
    // IMemoryPackFormatter<T> を取得し、仮想メソッド経由で Serialize を呼び出す
    var formatter = MemoryPackFormatterProvider.GetFormatter<T>();
    formatter.Serialize(ref this, ref Unsafe.AsRef(value));
}
```

また、`MemoryPackWriter`/`MemoryPackReader` では `ref field` を活用しています。

```csharp
public ref struct MemoryPackWriter<TBufferWriter>
    where TBufferWriter : IBufferWriter<byte>
{
    ref TBufferWriter bufferWriter;
    ref byte bufferReference;
    int bufferLength;
```

`ref byte bufferReference`, `int bufferLength` の組み合わせは、つまり`Span<byte>`のインライン化です。また、`TBufferWriter`を`ref TBufferWriter`として受け取ることにより、ミュータブルな`struct TBufferWriter : IBufferWrite<byte>`を安全に受け入れて呼び出すことができるようになりました。

全ての型への最適化
---
例えばコレクションは `IEnumerable<T>` としてシリアライズ/デシリアライズすることで実装の共通化が可能ですが、MemoryPackでは全ての型に対して個別の実装をするようにしています。単純なところでは `List<T>`を処理するのに

```csharp
public void Serialize(ref MemoryPackWriter writer, IEnumerable<T> value)
{
    foreach(var item in source)
    {
        writer.WriteValue(item);
    }
}

public void Serialize(ref MemoryPackWriter writer, List<T> value)
{
    foreach(var item in source)
    {
        writer.WriteValue(item);
    }
}
```

この2つでは全然性能が違います。`IEnumerable<T>`へのforeachは `IEnumerator<T>` を取得しますが、`List<T>`へのforeachは `struct List<T>.Enumerator` という最適化された専用の構造体のEnumeratorを取得するからです。

しかし、もっと最適化する余地があります。

```csharp
public sealed class ListFormatter<T> : MemoryPackFormatter<List<T?>>
{
    public override void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, scoped ref List<T?>? value)
    {
        if (value == null)
        {
            writer.WriteNullCollectionHeader();
            return;
        }

        writer.WriteSpan(CollectionsMarshal.AsSpan(value));
    }
}

// MemoryPackWriter.WriteSpan
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public void WriteSpan<T>(scoped Span<T?> value)
{
    if (!RuntimeHelpers.IsReferenceOrContainsReferences<T>())
    {
        DangerousWriteUnmanagedSpan(value);
        return;
    }

    var formatter = GetFormatter<T>();
    WriteCollectionHeader(value.Length);
    for (int i = 0; i < value.Length; i++)
    {
        formatter.Serialize(ref this, ref value[i]);
    }
}

// MemoryPackWriter.DangerousWriteUnmanagedSpan
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public void DangerousWriteUnmanagedSpan<T>(scoped Span<T> value)
{
    if (value.Length == 0)
    {
        WriteCollectionHeader(0);
        return;
    }

    var srcLength = Unsafe.SizeOf<T>() * value.Length;
    var allocSize = srcLength + 4;

    ref var dest = ref GetSpanReference(allocSize);
    ref var src = ref Unsafe.As<T, byte>(ref MemoryMarshal.GetReference(value));

    Unsafe.WriteUnaligned(ref dest, value.Length);
    Unsafe.CopyBlockUnaligned(ref Unsafe.Add(ref dest, 4), ref src, (uint)srcLength);

    Advance(allocSize);
}
```

まず、そもそも現代では `List<T>` の列挙は `CollectionsMarshal.AsSpan(value)` 経由で、`Span<T>`を取得して、それを列挙するのが最適です。それによってEnumerator経由というコストすら省くことが可能です。更に、`Span<T>`が取得できているなら、`List<int>`や`List<Vector3>`の場合にコピーのみで処理することもできます。

Deserializeの場合にも、興味深い最適化があります。まず、MemoryPackのDeserializeは `ref T? value` を受け取るようになっていて、valueがnullの場合は内部で生成したオブジェクトを（普通のシリアライザと同様）、valueが渡されている場合は上書きするようになっています。これによってDeserialize時の新規オブジェクト生成というアロケーションをゼロにすることが可能です。コレクションの場合も、`List<T>`の場合は`Clear()`を呼び出すことで再利用します。

その上で、特殊なSpanの呼び出しをすることにより、 `List<T>.Add` すら避けることに成功しました。

```csharp
public sealed class ListFormatter<T> : MemoryPackFormatter<List<T?>>
{
    public override void Deserialize(ref MemoryPackReader reader, scoped ref List<T?>? value)
    {
        if (!reader.TryReadCollectionHeader(out var length))
        {
            value = null;
            return;
        }

        if (value == null)
        {
            value = new List<T?>(length);
        }
        else if (value.Count == length)
        {
            value.Clear();
        }

        var span = CollectionsMarshalEx.CreateSpan(value, length);
        reader.ReadSpanWithoutReadLengthHeader(length, ref span);
    }
}

internal static class CollectionsMarshalEx
{
    /// <summary>
    /// similar as AsSpan but modify size to create fixed-size span.
    /// </summary>
    public static Span<T?> CreateSpan<T>(List<T?> list, int length)
    {
        list.EnsureCapacity(length);

        ref var view = ref Unsafe.As<List<T?>, ListView<T?>>(ref list);
        view._size = length;
        return view._items.AsSpan(0, length);
    }

    // NOTE: These structure depndent on .NET 7, if changed, require to keep same structure.

    internal sealed class ListView<T>
    {
        public T[] _items;
        public int _size;
        public int _version;
    }
}

// MemoryPackReader.ReadSpanWithoutReadLengthHeader
public void ReadSpanWithoutReadLengthHeader<T>(int length, scoped ref Span<T?> value)
{
    if (length == 0)
    {
        value = Array.Empty<T>();
        return;
    }

    if (!RuntimeHelpers.IsReferenceOrContainsReferences<T>())
    {
        if (value.Length != length)
        {
            value = AllocateUninitializedArray<T>(length);
        }

        var byteCount = length * Unsafe.SizeOf<T>();
        ref var src = ref GetSpanReference(byteCount);
        ref var dest = ref Unsafe.As<T, byte>(ref MemoryMarshal.GetReference(value)!);
        Unsafe.CopyBlockUnaligned(ref dest, ref src, (uint)byteCount);

        Advance(byteCount);
    }
    else
    {
        if (value.Length != length)
        {
            value = new T[length];
        }

        var formatter = GetFormatter<T>();
        for (int i = 0; i < length; i++)
        {
            formatter.Deserialize(ref this, ref value[i]);
        }
    }
}
```

`new List<T>(capacity)` や `List<T>.EnsurceCapacity(capacity)` によって、`List<T>`の抱える内部の配列のサイズを事前に拡大しておくことが可能です。これにより、都度拡大/コピーが内部で発生することを避けることができます。

その状態で `CollectionsMarshal.CreateSpan` を使うと、取得できるSpanは、長さ0のものです。なぜなら内部のsizeは変更されていないため、です。もし `CollectionMarshals.AsMemory`があれば、そこから`MemoryMarshal.TryGetArray`のコンボで生配列を取得できて良いのですが、残念ながら Span からは元になっている配列を取得する手段がありません。そこで、`Unsafe.As`で強引に型の構造を合わせて、`List<T>._size`を弄ることによって、拡大済みの内部配列を取得することができました。

そうすればunamanged型の場合はコピーだけで済ませてしまう最適化や、`List<T>.Add`(これは都度、配列のサイズチェックが入る)を避けた、`Span<T>[index]`経由での値の詰め込みが可能になり、従来のシリアライザのデシリアライズよりも遥かに高いパフォーマンスを実現しました。

`List<T>`への最適化が代表的ではありますが、他にも紹介しきれないほど、全ての型を精査し、可能な限りの最適化をそれぞれに施してあります。

まとめ
---
なぜ開発しようかと思ったかというと、MessagePack for C#に不満がでてきたから、です。残念ながら .NET「最速」とはいえないような状況があり、その理由としてバイナリ仕様が足を引っ張っているため、改善するのにも限界があることには随分前から気づいていました。また、実装面でもIL生成とRoslynを使った外部ツールとしてのコードジェネレーター(mpc)の、二種のメンテナンスがかなり厳しくなってきているということもありました。外部ツールとしてのコードジェネレーターはトラブルの種で、何かと環境によって動かないということが多発していて、Source Generatorにフル対応できるのなら、もはや廃止したいぐらいにも思っていました。

そこに .NET 7/C# 11 の ref fieldやstatic abstract methodを見た時、これをシリアライザー開発に応用したらパフォーマンスの底上げが可能になる、ついでにSource Generator化すれば、いっそIL生成も廃止してSource Generatorに一本化できるのではないか？それならもう、それをMessagPack for C#に適用する前に、パフォーマンス向上に問題のあるバイナリ仕様の限界も無視した、C#のためだけに究極の性能を実現するシリアライザーを作って、本当の最速を実証してしまえばいいのでは？と。

性能特化の実験的シリアライザーではなくて、実用性も重視したシリアライザーであるために、MessagePack for C#での経験も元にして、多くの機能も備えるようにしました。

```
* .NETのモダンI/O API対応(IBufferWriter<byte>, ReadOnlySpan<byte>, ReadOnlySequence<byte>)
* 既存オブジェクトへの上書きデシリアライズ
* ポリモーフィズムなシリアライズ(Union)
* PipeWriter/Readerを活用したストリーミングシリアライズ/デシリアライズ
* (やや限定的ながらも)バージョニング耐性
* TypeScriptコード生成
* Unity(2021.3)サポート
```

欠点としては、バージョニング耐性が、仕様上やや貧弱です。詳しくは[ドキュメントを参照してください](https://github.com/Cysharp/MemoryPack#version-tolerant)。パフォーマンスをやや落としてバージョニング耐性を上げるオプションを追加することは検討しています。また、メモリコピーを多用するので、実行環境が little-endian であることを前提にしています。ただし現代のコンピューターはほぼすべて little-endian であるため、問題にはならないはずです。

パフォーマンスのために特化したstructを作ってメモリコピーする、といったことはC#の最適化のための手段として、そこまで珍しいわけではなく、やったことある人もいるのではないかと思います。そこからすると、あるいはこの記事を読んで、MemoryPackは一見ただのメモリコピーの塊じゃん、みたいな感じがあるかもしれませんが、汎用シリアライザーとして成立させるのはかなり大変で、そこをやりきっているのが新しいところです。

当初実現していなかった .NET 5/6(Standard 2.1)対応やUnity対応は完了したので、今後は[MasterMemory](https://github.com/Cysharp/MasterMemory)のSource Generator/MemoryPack対応や、[MagicOnion](https://github.com/Cysharp/MagicOnion/)のシリアライザ変更対応など、利用できる範囲をより広げることを考えています。Cysharpの C#ライブラリ のエコシステムの中心になると位置づけているので、今後もかなり力入れて成長させていこうと思っていますので、まずは、是非是非試してみてください！