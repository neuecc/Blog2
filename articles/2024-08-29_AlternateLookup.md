# .NET 9 AlternateLookup によるC# 13時代のUTF8文字列の高速なDictionary参照

.NET 9 から辞書系のクラス、`Dictionary`, `ConcurrentDictionary`, `HashSet`, `FrozenDictionary`, `FrozenSet`に `GetAlternateLookup<TKey, TValue, TAlternate>()` というメソッドが追加されました。今までDictionaryの操作はTKey経由でしかできませんでした。それは当たり前、なのですが、困るのが文字列キーで、これはstringでも操作したいし、`ReadOnlySpan<char>`でも操作したくなります。今までは`ReadOnlySpan<char>`しか手元にない場合はToStringでstring化が必須でした、ただたんにDictionaryの値を参照したいだけなのに！

その問題も、.NET 9から追加された`GetAlternateLookup`を使うと、辞書に別の検索キーを持たせることが出来るようになりました。

```csharp
var dict = new Dictionary<string, int>
{
    { "foo", 10 },
    { "bar", 20 },
    { "baz", 30 }
};

var lookup = dict.GetAlternateLookup<ReadOnlySpan<char>>();

var keys = "foo, bar, baz";

// .NET 9 SpanSplitEnumerator
foreach (Range range in keys.AsSpan().Split(','))
{
    ReadOnlySpan<char> key = keys.AsSpan(range).Trim();

    // ReadOnlySpan<char>でstring keyの辞書のGet/Add/Removeできる
    int value = lookup[key];
    Console.WriteLine(value);
}
```

ところでSplitは、通常のstringのSplitは配列とそれぞれ区切られたstringをアロケーションしてしまいますが、.NET 8から、`ReadOnlySpan<char>`に対して固定個数のSplitができる[MemoryExtensions.Split](https://learn.microsoft.com/ja-jp/dotnet/api/system.memoryextensions.split)が追加されました。.NET 9では、更にSpanSplitEnumeratorを返すSplitが新たに追加されています。これにより一切の追加のアロケーションなく、元の文字列から`ReadOnlySpan<char>`を切り出すことができます。

そうして取り出した`ReadOnlySpan<char>`のキーで参照するために、`GetAlternateLookup`が必要になってくるわけです。

使い道としては、例えばシリアライザーは頻繁にキーと値のルックアップが必要になります。私の開発している[MessagePack for C#](https://github.com/MessagePack-CSharp/MessagePack-CSharp)では、高速でアロケーションフリーなデシリアライズのために、複数の戦略を採用しています。その一つはUTF8の文字列を8バイトずつの[オートマトン](https://en.wikipedia.org/wiki/Automata_theory)として扱う[AutomataDictionary](https://github.com/MessagePack-CSharp/MessagePack-CSharp/blob/bcedbce3fd98cb294210d6b4a22bdc4c75ccd916/src/MessagePack/Internal/AutomataDictionary.cs)、この部分は更にIL EmitやSource Generatorではインライン化して埋め込まれて辞書検索もなくしています。もう一つは[AsymmetricKeyHashTable](https://github.com/MessagePack-CSharp/MessagePack-CSharp/blob/5793c81/src/MessagePack/Internal/AsymmetricKeyHashTable.cs)という機構で、これは同一の対象を表す2つのキーで検索可能にしようというもので、内部的には `byte[]` と `ArraySegment<byte>` で検索できるような辞書を作っていました。

```csharp
// MessagePack for C#のもの
internal interface IAsymmetricEqualityComparer<TKey1, TKey2>
{
    int GetHashCode(TKey1 key1);
    int GetHashCode(TKey2 key2);
    bool Equals(TKey1 x, TKey1 y);
    bool Equals(TKey1 x, TKey2 y); // TKey1とTKey2での比較
}
```

つまり、今までは、こうした別の検索キーを持った辞書が必要なシチュエーションでは、辞書そのものの自作が必要だったし、パフォーマンスのためには基礎的なデータ構造すら自作を厭わない必要がありましたが、.NET 9からはついに標準でそれが実現するようになりました。

AlternateLookupでも必要なのは`IAlternateEqualityComparer<in TAlternate, T>`で、以下のような定義になっています。(`IAsymmetricEqualityComparer`と似たような定義なので、また時代を10年先取りしてしまったか)

```csharp
public interface IAlternateEqualityComparer<in TAlternate, T>
    where TAlternate : allows ref struct
    where T : allows ref struct
{
    bool Equals(TAlternate alternate, T other);
    int GetHashCode(TAlternate alternate);
    T Create(TAlternate alternate);
}
```

C# 13から追加された言語機能 [allows ref struct](https://learn.microsoft.com/ja-jp/dotnet/csharp/language-reference/builtin-types/ref-struct) によってref struct、つまり`Span<T>`などをジェネリクスの型引数にすることができるようになりました。

基本的にはこれは`IEqualityComparer<T>`とセットで実装する必要があります。実際、`Dictionary.GetAlternateLookup`ではDictionaryの`IEqualityComparer`が`IAlternateEqualityComparer`を実装していないと実行時例外が出ます（コンパイル時チェックではありません！）また、EqualityComparerなのに`Create`があるのが少し奇妙ですが、これはAdd操作のために必要だからです。

現状、標準では`IAlternateEqualityComparer`は`string`用しかありません。stringで標準的に使われるEqualityComparerは`IAlternateEqualityComparer`を実装していて、`ReadOnlySpan<char>`で操作できますが、それ以外は用意されていません。

しかし、現代において現実的に必要なのはUTF8です、`ReadOnlySpan<byte>`です。シリアライザーのルックアップで使う、と言いましたが、現代のシリアライザーの入力はUTF8です。`ReadOnlySpan<char>`の出番なんてありません。というわけで、以下のような`IAlternateEqualityComparer`を用意しましょう！

```csharp
public sealed class Utf8StringEqualityComparer : IEqualityComparer<byte[]>, IAlternateEqualityComparer<ReadOnlySpan<byte>, byte[]>
{
    public static IEqualityComparer<byte[]> Default { get; } = new Utf8StringEqualityComparer();

    // IEqualityComparer

    public bool Equals(byte[]? x, byte[]? y)
    {
        if (x == null && y == null) return true;
        if (x == null || y == null) return false;

        return x.AsSpan().SequenceEqual(y);
    }

    public int GetHashCode([DisallowNull] byte[] obj)
    {
        return GetHashCode(obj.AsSpan());
    }

    // IAlternateEqualityComparer

    public byte[] Create(ReadOnlySpan<byte> alternate)
    {
        return alternate.ToArray();
    }

    public bool Equals(ReadOnlySpan<byte> alternate, byte[] other)
    {
        return other.AsSpan().SequenceEqual(alternate);
    }

    public int GetHashCode(ReadOnlySpan<byte> alternate)
    {
        // System.IO.Hashing package, cast to int is safe for hashing
        return unchecked((int)XxHash3.HashToUInt64(alternate));
    }
}
```

`byte[]`は標準では参照比較になってしまいますが、データの一致で比較したいので、`ReadOnlySpan<T>.SequenceEqual` を使います。これは、特にTが幾つかのプリミティブの場合はSIMDを活用して高速な比較が実現されています。ハッシュコードの算出は、高速なアルゴリズム[xxHash](https://github.com/Cyan4973/xxHash)シリーズの最新版であるXXH3の.NET実装である[XxHash3](https://learn.microsoft.com/ja-jp/dotnet/api/system.io.hashing.xxhash3)を用いるのがベストでしょう。これはNuGetから`System.IO.Hashing`をインポートする必要があります。64ビットで算出するため戻り値はulongですが、32ビット値が必要な場合はxxHashの作者より、ただたんに切り落とすだけで問題ないと言明されているため、intにキャストするだけで済まします。

使う場合の例は、こんな感じです。

```csharp
// Utf8StringEqualityComparerを設定した辞書を作る

var dict = new Dictionary<byte[], bool>(Utf8StringEqualityComparer.Default)
{
    { "foo"u8.ToArray(), true },
    { "bar"u8.ToArray(), false },
    { "baz"u8.ToArray(), false }
};

var lookup = dict.GetAlternateLookup<ReadOnlySpan<byte>>();

// こんな入力があるとする

ReadOnlySpan<byte> json = """    
{
    "foo": 0,
    "bar": 0,
    "baz": 0
}
"""u8;

// System.Text.Json
var reader = new Utf8JsonReader(json);

while (reader.Read())
{
    if (reader.TokenType == JsonTokenType.PropertyName)
    {
        // 切り出したKeyで検索できる
        ReadOnlySpan<byte> key = reader.ValueSpan;
        var flag = lookup[key];
        
        Console.WriteLine(flag);
    }
}
```

一つ注意なのは、`string`と`ReadOnlySpan<byte>`でAlternateKeyを作ろうとするのはやめたほうが良いでしょう。それだと、常にエンコードが必要になり、悪いとこどりのようになってしまいます([Rune](https://learn.microsoft.com/ja-jp/dotnet/api/system.text.rune)を使ってアロケーションレスで処理するにしても、どちらにせよバイナリ比較だけで済ませられる`byte[]`キーとは比較になりません）。どうしても両方の検索が必要なら、辞書を二つ用意するほうがマシです。

ともあれ、これは私にとっては念願の機能です！色々なバリエーションで、Span対応のためにジェネリクスにもできずに決め打ちで辞書を何度も作ってきました、汎用的に使えるようになったのは大歓迎です。`allows ref struct`はジェネリクス定義での煩わしさもありますが（自動判定での付与でも良かったような？）、言語としては重要な進歩です。.NET 9, C# 13、使っていきましょう。現状はまだプレビューですが、11月に正式版がリリースされるはずです。