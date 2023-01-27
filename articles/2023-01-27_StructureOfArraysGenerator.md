# StructureOfArraysGenerator - C#でSoAを簡単に利用するためのSource Generator

最近はSource Generatorブームが続いていて、去年末に[2022年のC# (Incremental) Source Generator開発手法](https://neue.cc/2022/12/16_IncrementalSourceGenerator.html)という記事を出しましたが、まずは今年第一弾のSource Generatorライブラリです。

* [github.com/Cysharp/StructureOfArraysGenerator](https://github.com/Cysharp/StructureOfArraysGenerator/)

これは何かというと、structure of arrays(SoA)を使いやすくするためのコードを生成するというものです。まずそもそもSoAですが、Wikipediaの[AoS and SoA](https://en.wikipedia.org/wiki/AoS_and_SoA)という記事によるところ（日本語版はない）、CPUキャッシュを有効活用したりSIMDを適用させやすくなる構造だよ、と。通常C#の配列はarray of structures(AoS)になります。

![](https://user-images.githubusercontent.com/46207/214814782-fd341e09-731a-4e2f-ba53-ef789a19160e.png)

上の通常の配列がAoSでXYZXYZXYZXYZといったように並んでいる構造ですが、下のStructureOfArraysGeneratorで生成したSoAの配列はXXXXYYYYZZZZという並び順になります。実際にシンプルなパフォーマンステスト（Vector3[10000]に対してYの最大値を求める）によるところ

![](https://user-images.githubusercontent.com/46207/215027253-6f94739f-b827-46ba-a395-690d1df89d46.png)

そのまま書いても2倍、SIMDで書きやすい状態なのでSIMDで処理してしまえば10倍高速化されます。というわけで、パフォーマンスが求められるシチュエーションで非常に有用です。

このライブラリはZigという最近、日本でも注目されている言語（Node.jsの高速な代替として注目されている[Bun](https://bun.sh/)の実装言語）のMultiArrayListにインスパイアされました。Zigの作者 Andrew Kelley氏が講演した [A Practical Guide to Applying Data-Oriented Design](https://vimeo.com/649009599) という素晴らしい講演があるので是非見て欲しいのですが

![image](https://user-images.githubusercontent.com/46207/215052372-1ab33bd2-a578-4c26-8e99-7615a49707ea.png)

データ指向設計(Data-Oriented Design)はパフォーマンスを飛躍的に改善する魔法なのです。ん、それはどこかで聞いたような……？そう、[UnityのDOTS](https://unity.com/ja/dots)です。Data-Oriented Technology Stackです。ECSです。……。まぁ、そんなわけで全体に導入するにはそうとうガラッと設計を変える必要があるので大変厳しくはあるのですが、講演での実例としてZig自身のコンパイラの事例が出てますが、まぁつまりは徹底的にやれば成果は出ます。

しかしまぁ徹底的にやらず部分的に使っても効果があるのはUnityで Job System + Burst ぐらいでいいじゃん、という気持ちになっていることからも明らかです。というわけで部分的なSoA構造の導入にお使いください、かつ、導入や利用の敷居は全然高くないように設計しました。

MultiArray
---
NuGetからインストール（Unityの場合はgit参照か.unitypackageで）するとAnalyzerとして参照されます。StructureOfArraysGeneratorは属性も含めて依存はなく全てのコードが生成コードに含まれる（属性はinternal attributeとして吐かれる）ので、不要なライブラリ依存が増えることはありません。

`[MultiArray(Type)]`を配列的に使いたい`readonly partial struct`につけます。

```csharp
using StructureOfArraysGenerator;

[MultiArray(typeof(Vector3))]
public readonly partial struct Vector3MultiArray
{
}
```

するとSource Generatorは内部的にはこういうコードを生成します。

```csharp
partial struct Vector3MultiArray
{
    // constructor
    public Vector3MultiArray(int length)

    // Span<T> properties for Vector3 each fields
    public Span<float> X => ...;
    public Span<float> Y => ...;
    public Span<float> Z => ...;

    // indexer
    public Vector3 this[int index] { get{} set{} }

    // foreach
    public Enumerator GetEnumerator()
}
```

Structure of **Arrays** と言ってますが、StructureOfArraysGeneratorは Arrays は生成しません。内部的には単一の `byte[]` と各開始地点のオフセットのみを持っていて、生成されるプロパティによって`Span<T>`のビューを返すという設計になっています。

使い方的には配列のように使えますが、`Span<T>`の操作、例えばref var item inによるforeachを使うと、より効率的に扱えます。

```csharp
var array = new Vector3MultiArray(4);

array.X[0] = 10;
array[1] = new Vector3(1.1f, 2.2f, 3.3f);

// multiply Y
foreach (ref var item in v.Y)
{
    item *= 2;
}

// iterate Vector3
foreach (var item in array)
{
    Console.WriteLine($"{item.X}, {item.Y}, {item.Z}");
}
```

Yに2倍を掛ける処理などは、メモリ領域が連続していることにより、`Vector3[]`を `item.Y *= 2` などとして書くよりも高速に処理されます．

他に`List<T>`のようにAddできる`MultiArrayList`や、内部的には`byte[]`を持っているだけであることを生かした[MemoryPack](https://github.com/Cysharp/MemoryPack)での超高速なシリアライズなどにも対応しています。気になったら是非ReadMeのほうを見てください。

.NET 7 時代のSIMD
---
.NETはSIMD対応が進んでいて、[System.Runtime.Intrinsics.X86](https://learn.microsoft.com/ja-jp/dotnet/api/system.runtime.intrinsics.x86)によって、直接ハードウェア命令を書くことが出来ます。

しかし、しかしですね、最近は .NET を Arm で動かすことが現実的になってきました。iOSやAndroidでけはなくMacのArm化、そしてAWS GravitonのようなArmサーバーはコスト面でも有利で、選択肢に十分入ります。そこでAvx.Addなんて書いていたらArmで動きません。勿論 [System.Runtime.Intrinsics.Arm](https://learn.microsoft.com/ja-jp/dotnet/api/system.runtime.intrinsics.arm) というクラスも公開されていて、Arm版のSIMDを手書きすることもできるんですが、分岐して似たようなものを二個書けというのか！という話です。

そこで、 [.NET 7こそがC# SIMDプログラミングを始めるのに最適である理由](https://zenn.dev/pcysl5edgo/articles/d3e787599c5c8b) という記事があるのですが、確かに .NET 7 から追加された [Vector256.LoadUnsafe](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.intrinsics.vector256.loadunsafe) がまずめちゃくくちゃイイ！馴染みが深い（？）Unsafeによる ref var T で書けます！そして[Expose cross-platform helpers for Vector64, Vector128, and Vector256](https://github.com/dotnet/runtime/issues/49397)により、`Vector64/128/256<T>`にプラットフォーム抽象化されたSIMD処理が書けるようになりました、やはり .NET 7から。

例えば .NET 7 でint[]のSumのSIMD化を書いてみます。

```csharp
var array = Enumerable.Range(1, 100).ToArray();

ref var begin = ref MemoryMarshal.GetArrayDataReference(array);
ref var last = ref Unsafe.Add(ref begin, array.Length);

var vectorSum = Vector256<int>.Zero;
ref var current = ref begin;

// Vector256で処理できるだけ処理
ref var to = ref Unsafe.Add(ref begin, array.Length - Vector256<int>.Count);
while (Unsafe.IsAddressLessThan(ref current, ref to))
{
    // 直接足し算できて便利
    vectorSum += Vector256.LoadUnsafe(ref current);
    current = ref Unsafe.Add(ref current, Vector256<int>.Count);
}

// Vector256をintに戻す
 var sum = Vector256.Sum(vectorSum);

// 残りの分は単純処理
while (Unsafe.IsAddressLessThan(ref current, ref last))
{
    sum += current;
    current = ref Unsafe.Add(ref current, 1);
}

Console.WriteLine(sum); // 5050
```

まぁforがwhileのアドレス処理になっていたり、最後にはみ出た分を処理する必要がありますが、かなり自然にSIMDを扱えているといってもいいんじゃないでしょうか。(Unsafeに慣れていれば)かなり書きやすいです。いいね。

ところで .NET 7からLINQがSIMD対応してるからこんなの書く必要ないでしょ？というと、対応してません。LINQのSIMDはint[]のAverage, int[]のMin, Max, long[]のMin, Maxのみと、かなり限定的です。これは互換性の問題などなどがあり、まぁオマケみたいなものだと思っておきましょう。必要な局面があるなら自分で用意する方が無難です。

ともあれ、.NET 7 からは手書きX86 SIMDはArm対応が漏れやすいので、極力Vectorによって抽象化されたコードで書きましょう、ということになります。どうしてもVectorじゃ書けないところだけ、仕方なく書くという感じですね。

まとめ
---
反響全然ないだろうなあと想定していましたが、やはり反響全然ないです！まぁでも結構面白いライブラリになったと思うので、是非使ってください。それと、Incremental Source Generatorの作り方がMemoryPackの頃よりも習熟していて、コードがかなり洗練されたものになっているので、Source Generatorの作り方として参照するならMemoryPackのコードよりもこちらのコードのほうがお薦めです。

というわけで、まだまだSource Generatorネタはいっぱいあるので、今年は大量に量産します！
