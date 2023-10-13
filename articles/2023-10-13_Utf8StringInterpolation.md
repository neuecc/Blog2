# UTF8文字列生成を最適化するライブラリ Utf8StringInterpolation を公開しました

Utf8StringInterpolationという新しいライブラリを公開しました！UTF8文字列の生成と書き込みに特化していて、動作をカスタマイズした文字列補間式によるC#コンパイラの機能を活用した生成と、StringBuilder的な連続的な書き込みの両方をサポートします。

* [Cysharp/Utf8StringInterpolation](https://github.com/Cysharp/Utf8StringInterpolation)

基本的な流れはこんな感じで、Stringを生成するのと同じように、UTF8を生成/書き込みできます。

```csharp
using Utf8StringInterpolation;

// Create UTF8 encoded string directly(without encoding).
byte[] utf8 = Utf8String.Format($"Hello, {name}, Your id is {id}!");

// write to IBufferWriter<byte>(for example ASP.NET HttpResponse.BodyWriter)
Utf8String.Format(bufferWriter, $"Today is {DateTime.Now:yyyy-MM-dd}"); // support format

// like a StringBuilder
var writer = Utf8String.CreateWriter(bufferWriter);
writer.Append("My Name...");
writer.AppendFormat($"is...? {name}");
writer.AppendLine();
writer.Flush();

// Join, Concat methods
var seq = Enumerable.Range(1, 10);
byte[] utf8seq = Utf8String.Join(", ", seq);
```

Cysharpから公開している [ZString](https://github.com/Cysharp/ZString/) と非常に近いのですが、ZStringがString(UTF16), UTF8をサポートしていたのに対して、UTF8側のみを取り出して強化したようなイメージになります。何が強化なのかというと、C# 10.0から[Improved Interpolated Strings](https://github.com/dotnet/csharplang/blob/main/proposals/csharp-10.0/improved-interpolated-strings.md)として、文字列補間式($"foo{bar}baz")のパフォーマンスが大きく向上しました。具体的には、コンパイラが文字列補間式の構造を分解して、値が埋め込まれている箇所はGenericsのまま渡すようになりボクシングが消滅しました。つまりZStringでやっていたことではあるのですが、ZStringはC# 10.0以前のものですからね……！逆に言えば、これによってZStringは半分は不要となったわけです。

もう半分、UTF8側に関しては依然として標準のサポートは薄い、というかほぼない状態です。しかし、Improved Interpolated Strings は文字列補間式での挙動を自由にカスタマイズできるという性質も追加されています。というわけで、文字列補間式を利用してUTF8を組み立てられるようにすればいいのではないか、というのがUtf8StringInterpolationのコンセプトであり、正しくZLoggerの後継として位置づけていることでもあります。

```csharp
// こういう文字列補間式を渡すと:
// Utf8String.Format(ref Utf8StringWriter format)
Utf8String.Format($"Hello, {name}, Your id is {id}!");
```

```csharp
// コンパイラが「コンパイル時」にこのような形に展開します。
var writer = new Utf8StringWriter(literalLength: 20, formattedCount: 2);
writer.AppendLiteral("Hello, ");
writer.AppendFormatted<string>(name);
writer.AppendLiteral(", You id is ");
writer.AppendFormatted<int>(id);
writer.AppendLiteral("!");
```

コンパイル時に展開してくれるというのは性能上非常に重要で、つまり`String.Format`のように実行時に文字列式のパースをしないで済む、わけです。また、ボクシングなしに全ての値を書き込みに呼んでくれます。

`[InterpolatedStringHandler]`を付与している`ref Utf8StringWriter` に `$"{}"`を渡すと、自動的に展開してくれるという仕様になっています。そのUtf8StringWriterは以下のような実装になっています。

```csharp
// internal struct writer write value to utf8 directly without boxing.
[InterpolatedStringHandler]
public ref struct Utf8StringWriter<TBufferWriter> where TBufferWriter : IBufferWriter<byte>
{
    TBufferWriter bufferWriter; // when buffer is full, advance and get more buffer
    Span<byte> buffer;          // current write buffer

    public void AppendLiteral(string value)
    {
        // encode string literal to Utf8 buffer directly
        var bytesWritten = Encoding.UTF8.GetBytes(value, buffer);
        buffer = buffer.Slice(bytesWritten);
    }

    public void AppendFormatted<T>(T value, int alignment = 0, string? format = null)
        where T : IUtf8SpanFormattable
    {
        // write value to Utf8 buffer directly
        while (!value.TryFormat(buffer, out bytesWritten, format))
        {
            Grow();
        }
        buffer = buffer.Slice(bytesWritten);
    }
}
```

.NET 8 の場合は、値が [IUtf8SpanFormattable](https://learn.microsoft.com/ja-jp/dotnet/api/system.iutf8spanformattable?view=net-8.0) という.NET 8から追加されたインターフェイスを実装している場合(intなど標準のプリミティブはほぼ実装されています）、直接TryFormatによりUTF8としてSpanに書き込みます。

さすがに .NET 8 にしか対応していません！というのはエクストリームすぎるので、 .NET Standard 2.1, .NET 6(.NET 7)では [Utf8Formatter.TryFormat](https://learn.microsoft.com/en-us/dotnet/api/system.buffers.text.utf8formatter.tryformat?view=net-7.0) を使うことで、同様の性能を担保しています。

## Builder vs Writer

ZStringのときはStringBuilderに引っ張られすぎていて、Builderとして内部でバッファを抱えるようにしていたのですが、ちょっとUTF8的な利用ではイマイチだということが徐々に分かってきました。今の .NET の基本は `IBufferWriter<byte>` である。というのはついこないだのCEDEC 2023での発表 [モダンハイパフォーマンスC# 2023 Edition](https://speakerdeck.com/neuecc/cedec-2023-modanhaipahuomansuc-number-2023-edition) でかなり語らせていただいたのですが

<iframe class="speakerdeck-iframe" frameborder="0" src="https://speakerdeck.com/player/055c0df858f44aafb4b017bb9c03c2e6" title="CEDEC 2023 モダンハイパフォーマンスC# 2023 Edition" allowfullscreen="true" style="border: 0px; background: padding-box padding-box rgba(0, 0, 0, 0.1); margin: 0px; padding: 0px; border-radius: 6px; box-shadow: rgba(0, 0, 0, 0.2) 0px 5px 40px; width: 100%; height: auto; aspect-ratio: 560 / 315;" data-ratio="1.7777777777777777"></iframe>

BuilderというよりもWriterとして構築すべきだな、ということに至りました。そこで `Utf8StringWriter` は基本的に`IBufferWriter<byte>`を受け取ってそれに書き込むという仕様となりました。

```csharp
public ref partial struct Utf8StringWriter<TBufferWriter>
    where TBufferWriter : IBufferWriter<byte>
{
    Span<byte> destination;
    TBufferWriter bufferWriter;
    int currentWritten;

    public Utf8StringWriter(TBufferWriter bufferWriter)
    {
        this.bufferWriter = bufferWriter;
        this.destination = bufferWriter.GetSpan();
    }

    public void Flush()
    {
        if (currentWritten != 0)
        {
            bufferWriter.Advance(currentWritten);
            currentWritten = 0;
        }
    }
```

バッファが足りなくなったときは拡大するのではなくて、Advanceして新たにGetSpanを呼んで新しいバッファを確保しにいくという形を取りました。StringBuilderと違ってFlushの概念が必要になってしまいましたが、パフォーマンス的には大きな向上を果たしています。

Flushが必要ということを除けば、StringBuilderのように扱うことができます。

```csharp
var writer = Utf8String.CreateWriter(bufferWriter);

// call each append methods.
writer.Append("foo");
writer.AppendFormat($"bar {Guid.NewGuid}");
writer.AppendLine();

// finally call Flush(or Dispose)
writer.Flush();
```

また、ちょっとStringBuilder的に使いたいだけの時に `IBufferWriter<byte>` を用意するのは面倒くさい！という場合のために、内部でプーリングを行っているバッファを使えるオーバーロードも用意しています。戻り値がバッファのコントローラーになっていて、ToArrayや他の`IBufferWriter<byte>`にコピーしたり`ReadOnlySpan<byte>`の取得ができます。

```csharp
// buffer must Dispose after used(recommend to use using)
using var buffer = Utf8String.CreateWriter(out var writer);

// call each append methods.
writer.Append("foo");
writer.AppendFormat($"bar {Guid.NewGuid}");
writer.AppendLine();

// finally call Flush(no need to call Dispose for writer)
writer.Flush();

// copy to written byte[]
var bytes = buffer.ToArray();

// or copy to other IBufferWriter<byte>, get ReadOnlySpan<byte>
buffer.CopyTo(otherBufferWriter);
var writtenData = buffer.WrittenSpan;
```

その他、`Format`, `Join`, `Concat` メソッドなども `IBufferWriter<byte>` を受け取るオーバーロードと `byte[]`を返すオーバーロードの2種を用意しています。

## .NET 8 と StandardFormat

値のフォーマット書式は、特にDateTimeでよく使うと思いますが、数値型などでも多くの書式が用意されています。[.NET の数値、日付、列挙、その他の型の書式を設定する方法](https://learn.microsoft.com/ja-jp/dotnet/standard/base-types/formatting-types) や各種カスタム書式指定文字列は非常に便利です。

しかし、UTF8に値を直接書き込む手段として従来用意されていた[Utf8Formatter.TryFormat](https://learn.microsoft.com/en-us/dotnet/api/system.buffers.text.utf8formatter.tryformat)では、その標準的な書式指定文字列は使えませんでした！代わりに用意されたのが[StandardFormat](https://learn.microsoft.com/en-us/dotnet/api/system.buffers.standardformat)なのですが、恐ろしく限定的なことしかできず(例えば'G', 'D', or 'X'のような一文字charの指定しかできない)、使い物にならないといっても過言ではないぐらいでした。

ところが .NET 8 から追加された IUtf8SpanFormattable.TryFormat では、通常の書式指定文字列が帰ってきました！

```csharp
// Utf8Formatter.TryFormat
static bool TryFormat (int value, Span<byte> destination, out int bytesWritten, System.Buffers.StandardFormat format = default);

// .NET 8 IUtf8SpanFormattable.TryFormat
bool TryFormat (Span<byte> utf8Destination, out int bytesWritten, ReadOnlySpan<char> format, IFormatProvider? provider);
```

パラメーターは非常に似ていますが、formatを文字列で受け取るようになっています。実際に比較してみるとこんな感じです。

```csharp
Span<byte> dest = stackalloc byte[16];
int written = 0;

// ParseできなくてExceptionがthrowされるので表現できない
Utf8Formatter.TryFormat(123.456789, dest, out written, StandardFormat.Parse(".###"));

// 123.456
123.456123.TryFormat(dest, out written, ".###");


// カスタム書式文字列は指定できないので例外！サポートしてるのは `G`, `R`, `l`, `O` だけ！
Utf8Formatter.TryFormat(DateTime.Now, dest, out written, StandardFormat.Parse("yyyy-MM-dd"));

// もちろんちゃんと動作する
DateTime.Now.TryFormat(dest, out written, "yyyy-MM-dd");

Console.WriteLine(Encoding.UTF8.GetString(dest.Slice(0, written)));
```

良かった、やと普通の世界が到達した……！これは ZString や、それを内部に使っていた [ZLogger](https://github.com/Cysharp/ZLogger)で最もフラストレーションを感じていた点です。

Utf8StringInterpolationは .NET 8 では全て IUtf8SpanFormattable で変換するようにしています。しかし、 .NET Standard 2.1, .NET 6, .NET 7では残念ながらUtf8Formatter利用となっているので、書式指定に関しては制限があります。数値に関してはターゲットプラットフォームによって動作したりしなかったりが発生します。

```csharp
// .NET 8 supports all numeric custom format string but .NET Standard 2.1, .NET 6(.NET 7) does not.
Utf8String.Format($"Double value is {123.456789:.###}");
```

ただし、 `DateTime`, `DateTimeOffset`, `TimeSpan` に関しては `Utf8Formatter` を使わない処理をしているため、全てのターゲットプラットフォームでカスタム書式指定が利用可能です！

```csharp
// .NET 8 supports all numeric custom format string but .NET Standard 2.1, .NET 6(.NET 7) does not.
Utf8String.Format($"Double value is {123.456789:.###}");

// DateTime, DateTimeOffset, TimeSpan support custom format string on all target plaftorms.
// https://learn.microsoft.com/en-us/dotnet/standard/base-types/custom-date-and-time-format-strings
Utf8String.Format($"Today is {DateTime.Now:yyyy-MM-dd}");
```

とにかくDateTimeの書式指定がまともに出来ないのはZString/ZLoggerで一番辛かったところなので、それを改善できてとても良かった……。ただしこの対応により、DateTimeの変換性能が落ちているため、性能が最大限引き出せるのは .NET 8 となります。

## Next

さて、とはいえ、UTF8文字列を直接扱わなければならないケースというのは、別にそんなに多くはないでしょう。実際、私も本命は[ZLogger](https://github.com/Cysharp/ZLogger)の大型バージョンアップでの利用を考えています。ZLoggerは今まではZStringベースでしたが、根本からデザインをやり直した新しいものを開発中です。その中の文字列化にUtf8StringInterpolationを使っています。

といったように、アプリケーションの基盤レイヤーに差し込んであげると有効に機能するシチュエーションは色々あると思います。もちろん、直接使ってもらってもいいのですが……！？