# csbindgen - C#のためのネイティブコード呼び出し自動生成、或いはC#からのネイティブコード呼び出しの現代的手法について

ネイティブコードとC#を透過的に接続するために、RustのFFIからC#のDllImportコードを自動生成するライブラリを作成し、公開しました。Cysharp初のRustライブラリです！先週にプレビューを出していましたが、しっかりした機能強化とReadMeの充実をして正式公開、です！

* [Cysharp/csbindgen](https://github.com/Cysharp/csbindgen)
* [crates.io/crates/csbindgen](https://crates.io/crates/csbindgen)

めちゃくちゃスムーズにネイティブコードがC#から呼べるようになります。すごい簡単に。超便利。こりゃもうばんばんネイティブコード書きたくなりますね……！ただし書くコードはRustのみ対応です。いや、別にRustでいいでしょ、Rustはいいぞ……！

しかしまず前提として言っておくと、ネイティブコードは別に偉くもなければ、必ず速いというわけでもないので、極力書くのはやめましょう。C#で書くべき、です。高速なコードが欲しければ、ネイティブコードに手を出す前にC#で速くすることを試みたほうがずっと良いです。C#は十分高速に書くことのできる言語です！ネイティブコードを書くべきでない理由は山ほどありますが、私的に最大の避けたい理由はクロスプラットフォームビルドで、今の世の中、ターゲットにしなければならないプラットフォーム/アーキテクチャの組み合わせは、普通にやっていても10を超えてしまいます。win/linux/osx/iOS/Android x x86/x64/arm。C#では .NET のランタイムやUnityが面倒見てくれますが、ネイティブコードの場合はこれを自前で面倒みていく必要があります。そこそこ面倒みてくれるはずのUnityだって辛いのに、それにプラスして俺々ビルド生態系を加えるのはかなり厳しいものがある。

とはいえ、C#をメインに据えつつもネイティブコードを利用すべきシチュエーションもあるにはあります。

* Android NDKや .NET unmanaged hosting APIなど、ネイティブAPIしか提供されていないものを使いたい場合
* C で作られているネイティブライブラリを利用したい場合
* ランタイムのライブラリの利用を避けたい場合、例えばUnityで .NET のSocket(Unityの場合 .NET のランタイムが古いのでパフォーマンスを出しにくい)を避けてネイティブのネットワークコードを書くのには一定の道理がある

[NativeAOT](https://learn.microsoft.com/ja-jp/dotnet/core/deploying/native-aot/)という解決策もなくはないですが、まだそんなに現実的でもなければ、用途的にもこういうシチュエーションでは限定的でもあるので、そこは素直にネイティブコードを書いていくべき、でしょう。

そこでの最初の選択肢は当然C++なわけですが、いやー、C++のクロスプラットフォームビルドは大変だしなあ。となると、最近評判を聞く[Zig](https://ziglang.org/ja/)はどうだろうか、と試してみました、が、撤退。目指すコンセプトは大変共感するところがあるのですが(FFIなしのCライブラリとの統合や、安全だけど複雑さを抑えた文法など)、まだ、完成度が、かなり、厳しい……。

で、最後の選択肢が[Rust](https://www.rust-lang.org/ja)でした。FFIなしでの呼び出しではないものの[cc crate](https://crates.io/crates/cc)や[cmake crate](https://crates.io/crates/cmake)といったライブラリを使うと自然に統合されるし、[bindgen](https://github.com/rust-lang/rust-bindgen)によるバインディングの自動生成はよく使われているだけあってめっちゃ安定して簡単に生成できます。ていうかZigが全然安定感なかった（シームレスなCとの統合とは……）ので雲泥の差でびっくりした。開発環境もまぁまぁ充実してるしコマンド体系も現代的。クロスプラットフォームビルドも容易！そして難しいと評判で避けていた言語面でも、いや、全然いいね。仕組みが理屈で納得できるし、C#とは文法面でもあまり離れていないので、全然すんなりと入れました。もちろん難しいところも多々ありますが、ラーニングカーブはそんなに急ではない、少なくとも最近のモダンC#をやり込んでる人なら全然大丈夫でしょう……！

と、いうわけで、しかし主な用途はC#からの利用で、特にCライブラリの取り込みにRustを使おうと決めたわけですが、C#に対して公開するためのコードが膨大でキツかったので、自動化したかったんですね。DllImportの自動化は[SWIG](https://www.swig.org/)や[CppSharp](https://github.com/mono/CppSharp)というのもありますが、普通のC++をそのまま持ってこようとする思想は、複雑なコードを吐いてしまったりで正直イマイチだな、と。

csbindgenは、まず、面倒なところをRustのbindgenに丸投げです。複雑なC(C++)のコードを解析対処にするから複雑になるのであって、bindgenによって綺麗なRustに整形してもらって、生成対象にするのはそうしたFFI向けに整理されたRustのみを対象にすることで、精度と生成コードの単純さを担保しました。自分でネイティブコードを書く場合も、RustはFFI不可能な型を公開しようとすると警告も出してくれるので、必然的に生成しやすい綺麗なコードになっています。型もRustは非常に整理されているため、C#とマッピングしやすくなっています。C#もまた近年のnintや`delegate*`、.NET 6からの[CLong](https://learn.microsoft.com/ja-jp/dotnet/api/system.runtime.interopservices.clong)などの追加によって自然なやり取りができるようになりました。csbindgenはそれら最新の言語機能を反映することで、自然で、かつパフォーマンスの良いバインディングコードを生成しています。

Getting Started
---
コンフィグにビルド時依存に追加してもらって、`build.rs`というコンパイル前呼び出し(Rustのコードでpre-build書ける機能やビルド時依存を追加できる機能はとても良い)に設定を入れるだけです、簡単！

```toml
[build-dependencies]
csbindgen = "1.2.0"
```

```rust
// extern "C" fnが書かれているlib.rsを読み取って DllImport["nativelib"]なコードを"NativeMethods.g.cs"に出力する
csbindgen::Builder::default()
    .input_extern_file("lib.rs")
    .csharp_dll_name("nativelib")
    .generate_csharp_file("../dotnet/NativeMethods.g.cs")
    .unwrap();
```

単純なコードを例に出すと、このx, yを受け取ってintを返す関数は

```csharp
#[no_mangle]
pub extern "C" fn my_add(x: i32, y: i32) -> i32 {
    x + y
}
```

こういったC#コードを生成します。

```csharp
// NativeMethods.g.cs
using System;
using System.Runtime.InteropServices;

namespace CsBindgen
{
    internal static unsafe partial class NativeMethods
    {
        const string __DllName = "nativelib";

        [DllImport(__DllName, EntryPoint = "my_add", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern int my_add(int x, int y);
    }
}
```

直感的で単純な出力です、逆にそれがいい、むしろそれがいい。生成に対応している型はプリミティブ以外にもstructやunion、enum、関数やポインターなどRustのFFIで流せる型のほとんどには対応しています。

また、Rustのbindgenやcc/cmake crateを併用すると、CのライブラリをC#に簡単に持ちこむことができます。例えば圧縮ライブラリの[lz4](https://github.com/lz4/lz4)は、csbindgenでの生成の前にbindgenとccの設定も足してあげると

```csharp
// lz4.h を読み込んで lz4.rs にRust用のbindingコードを出力する
bindgen::Builder::default()
    .header("c/lz4/lz4.h")
    .generate().unwrap()
    .write_to_file("lz4.rs").unwrap();

// cc(C Compiler)によってlz4.cを読み込んでコンパイルしてリンクする
cc::Build::new().file("lz4.c").compile("lz4");

// bindgenの吐いたコードを読み込んでcsファイルを出力する
csbindgen::Builder::default()
    .input_bindgen_file("lz4.rs")
    .rust_file_header("use super::lz4::*;")
    .csharp_entry_point_prefix("csbindgen_")
    .csharp_dll_name("liblz4")
    .generate_to_file("lz4_ffi.rs", "../dotnet/NativeMethods.lz4.g.cs")
    .unwrap();
```

これでC#から呼び出せるコードが簡単に生成できます。ビルドもRustで `cargo build` するだけでCのコードがリンクされてDLLに含まれています。

```csharp
// NativeMethods.lz4.g.cs

using System;
using System.Runtime.InteropServices;

namespace CsBindgen
{
    internal static unsafe partial class NativeMethods
    {
        const string __DllName = "liblz4";

        [DllImport(__DllName, EntryPoint = "csbindgen_LZ4_compress_default", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern int LZ4_compress_default(byte* src, byte* dst, int srcSize, int dstCapacity);

        // snip...
    }
}
```

試してもらうと、本当に簡単にCライブラリが持ち込みができて感動します。Rustやbindgenがとにかく偉い。

csbindgenはUnityでの利用も念頭においているので、よくあるiOSでのIL2CPPだけ __Internal にしたいみたいなシチュエーションでも

```csharp
#if UNITY_IOS && !UNITY_EDITOR
    const string __DllName = "__Internal";
#else
    const string __DllName = "nativelib";
#endif
```

といったような生成ルールの変更がコンフィグに含めてあります。とても実用的で気が利いてます。

LibraryImport vs DllImport
---
.NET 7から[LibraryImport](https://learn.microsoft.com/ja-jp/dotnet/standard/native-interop/pinvoke-source-generation)という新しい呼び出しのためのソースジェネレーターが追加されました。これはDllImportのラッパーになっていて、DllImportは、本来ネイティブコードとやり取りできない型(例えば配列や文字列などの参照型はC#のヒープ上に存在するもので、ネイティブ側に渡せない)を裏で自動的にやってくれるという余計なお世話が含まれていて、それがややこしさや性能面、そしてNativeAOTビリティの欠如などの問題を含んでいたので、そういう型が渡された場合はLibraryImportの生成するC#コードで吸収した上で、byte* としてDllImportに渡すようなラッパーが生成されるようになっています。

つまり余計なお世話をする本来ネイティブコードとやり取りできない型を生成しないようにすればDllImportでも何の問題もないので、今回はDllImportでの生成を選んでいます。そのほうがUnityでも使いやすいし。

Win32のAPIをDllImportで簡単に呼び出せるようにするために暗黙的な自動変換を多数用意しておく、というのは時代背景的には理解できます。C#がWindowsのためだけの言語であり、時折Win32 APIの呼び出しが必須なこともあったのは事実であり、便利な側面もあったでしょう。しかし現在はWindowsのためだけの言語でもなく、またWin32 APIの呼び出しに関しては[CsWin32](https://github.com/microsoft/CsWin32)というSource Generatorを活用した支援も存在します。

もう現代では、そうしたDllImportの古い設計を引きずって考える必要はない、頼るべきではないでしょう。つまり参照型を渡したり[In]や[Out]は使うべきではないし、変換を考慮した設計を練る必要もありません。実際 .NET 7ではそうしたDllImportの機能を使うとエラーにする[DisableRuntimeMarshallingAttribute](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.compilerservices.disableruntimemarshallingattribute)が追加されました。

ポインターに関しても今はあまり忌避するものではないと思っています。そもそもネイティブとの通信はunsafeだし、Spanによって比較的使いやすい型に変換することも容易なので。中途半端に隠蔽するぐらいなら、DllImportするレイヤーではポインターはポインターとして持っておきましょう。C#として使いやすくするのは、その外側できっちりやればいい話です、DllImportで吸収するものではない。というのが今風の設計思想であると考えています。なんだったら私はIntPtrよりvoid*のほうが好きだよ。

コールバックの相互受け渡し
---
C# -> Rust あるいは Rust -> C# でコールバックを渡し合ってみましょう。まずRust側はこんな風に書くとします。

```rust
#[no_mangle]
pub extern "C" fn csharp_to_rust(cb: extern "C" fn(x: i32, y: i32) -> i32) {
    let sum = cb(10, 20); // invoke C# method
    println!("{sum}");
}

#[no_mangle]
pub extern "C" fn rust_to_csharp() -> extern fn(x: i32, y: i32) -> i32 {
    sum // return rust method
}

extern "C" fn sum(x:i32, y:i32) -> i32 {
    x + y
}
```

C#のメソッドを受け取ったら、それを読んで表示(println)するだけ、あるいは足し算する関数をC#に渡すだけ、のシンプルなメソッドです。生成コードは以下のようなものになります。

```csharp
[DllImport(__DllName, EntryPoint = "csharp_to_rust", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
public static extern void csharp_to_rust(delegate* unmanaged[Cdecl]<int, int, int> cb);

[DllImport(__DllName, EntryPoint = "rust_to_csharp", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
public static extern delegate* unmanaged[Cdecl]<int, int, int> rust_to_csharp();
```

`delegate* unmanaged[Cdecl]<int, int, int>` というのは、あまり見慣れない定義だと思うのですが、C# 9.0から追加された本物の[関数ポインター](https://learn.microsoft.com/ja-jp/dotnet/csharp/language-reference/proposals/csharp-9.0/function-pointers)になります。定義を手書きするのは少しややこしいですが、自動生成されるので特に問題なしでしょう（？）。使い勝手はかなりよく、普通の静的メソッドのように扱えます。

```csharp
// ネイティブ側に渡したい静的メソッドはUnmanagedCallersOnlyを付ける必要がある
[UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
static int Sum(int x, int y) => x + y;

// &で関数ポインターを取得して渡す
NativeMethods.csharp_to_rust(&Sum);

// Rustからdelegate*を受け取る
var f = NativeMethods.rust_to_csharp();

// 受け取った関数ポインターは普通に呼び出せる
var v = f(20, 30);
Console.WriteLine(v); // 50
```

インスタンスメソッドを渡せないのか？というと渡せません。Cとの相互運用にそんなものはない。どうでもいい勝手な変換はしなくていい。第一引数にコンテキスト(void*)を受け取るコードを用意しておけばいいでしょう。

ところで、UnityもC# 9.0対応、しているし関数ポインターも使えるには使えるのですが、[Extensible calling conventions for unmanaged function pointers is not supported](https://docs.unity3d.com/ja/2021.3/Manual/CSharpCompiler.html)です。UnmanagedCallersOnlyAttributeもないしね。Unity Editor上では普通に動いちゃったりとかしますが、IL2CPPでは動かないのでちゃんと対応しましょう。csbindgenでは `csharp_use_function_pointer(false)` というオプションを設定すると、従来のデリゲートを使用したコードを出力します。

```csharp
// csharp_use_function_pointer(false) の場合の出力結果、専用のデリゲートを一緒に吐き出すようになる
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate int csharp_to_rust_cb_delegate(int x, int y);

[DllImport(__DllName, EntryPoint = "csharp_to_rust", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
public static extern void csharp_to_rust(csharp_to_rust_cb_delegate cb);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate int rust_to_csharp_return_delegate(int x, int y);

[DllImport(__DllName, EntryPoint = "rust_to_csharp", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
public static extern rust_to_csharp_return_delegate rust_to_csharp();

// MonoPInvokeCallback属性を静的メソッドにつける(typeofでデリゲートを設定)
[MonoPInvokeCallback(typeof(NativeMethods.csharp_to_rust_cb_delegate))]
static int Sum(int x, int y) => x + y;

// そのまま渡す
NativeMethods.csharp_to_rust(Method);

// 受け取る関数ポインターに関しては .NET の場合と一緒
var f = NativeMethods.rust_to_csharp();
var v = f(20, 30);
Console.WriteLine(v); // 50
```

面倒くさい専用のデリゲートも同時に出力してくれるので、定義はそこそこ楽になります（Action/Funcといった汎用デリゲートを使うと場合によりクラッシュしてしまったので、必ずそれぞれのパラメーター専用のデリゲートを出力するようにしています）。概ねcsbindgenがよしなに動くように面倒見てあげるので、属性の違いだけ考えればほぼ問題はありません。

コンテキスト
---
多値返しみたいなのは、普通にStructを作ってくださいという話になって、その場合は、C#側でStructはコピーされて、Rust側のメモリからはすぐ消えるということになります。

```rust
#[no_mangle]
pub unsafe extern "C" fn return_tuple() -> MyTuple {
    MyTuple { is_foo: true, bar: 9999 }
}

#[repr(C)]
pub struct MyTuple {
    pub is_foo: bool,
    pub bar: i32,
}
```

もう少し寿命を長く、返却するStructをポインターで返して状態を持ちたい、という場合はRust的には少し工夫が必要です。

```rust
#[no_mangle]
pub extern "C" fn create_context() -> *mut Context {
    let ctx = Box::new(Context { foo: true });
    Box::into_raw(ctx)
}

#[no_mangle]
pub extern "C" fn delete_context(context: *mut Context) {
    unsafe { Box::from_raw(context) };
}

#[repr(C)]
pub struct Context {
    pub foo: bool,
    pub bar: i32,
    pub baz: u64
}
```

```csharp
// C#側、Context*を受け取って
var context = NativeMethods.create_context();

// なにか色々したりずっと持っていたり

// 最後に明示的にfreeしにいく
NativeMethods.delete_context(context);
```

`Box::new` でヒープ上にデータを確保して、`Box::into_raw`でRust上でのメモリ管理から外します。Rustは通常だとスコープが外れると即座にメモリを返却する、のですが、寿命をRust管理外のC#に飛ばすので、素直に（？）unsafeにRust上の管理から外してしまうのが普通に素直でしょう。Rust側で確保しているメモリを開放する場合は、`Box::from_raw`でRust上の管理に戻します。そうするとスコープが外れたらメモリ返却という通常の動作をして、返却が完了します。

この辺はRustだから難しい！という話ではなく、C#でもfixedスコープを外れてポインタを管理したい場合には `GCHandle.Allocc(obj, GCHandleType.Pinned)` して手動でunsafeな管理しなければいけないので、完全に同じ話です。そう考えると、むしろ素直にC#と変わらない話でいいですね。

なお、C#上でこうしたコンテキストの管理をする場合に専用のSafeHandleを作って、それにラップするという流儀がありますが、大仰で、基本的にはそこまでやる必要はないと思ってます。No SafeHandle。そもそも境界越えというunsafeなことをしているのだから、最後まで自己責任でいいでしょう。

csbindgenは戻り値にstructが指定されていると、C#側にも同様のものを生成しに行ってしまいますが、Rust内だけで使うのでC#側には内容公開したくない、というか参照(Box)とかも含まれてるから公開できないし、みたいな場合もあると思います。その場合は `c_void` を返してください。

```rust
#[no_mangle]
pub extern "C" fn create_counter_context() -> *mut c_void {
    let ctx = Box::new(CounterContext {
        set: HashSet::new(),
    });
    Box::into_raw(ctx) as *mut c_void // voidで返す
}

#[no_mangle]
pub unsafe extern "C" fn insert_counter_context(context: *mut c_void, value: i32) {
    let mut counter = Box::from_raw(context as *mut CounterContext); // as で型を戻す
    counter.set.insert(value);
    Box::into_raw(counter); // contextを使い続ける場合はinto_rawを忘れないように
}

#[no_mangle]
pub unsafe extern "C" fn delete_counter_context(context: *mut c_void) {
    let counter = Box::from_raw(context as *mut CounterContext);
    for value in counter.set.iter() {
        println!("counter value: {}", value)
    }
}

// C#側には公開しない
pub struct CounterContext {
    pub set: HashSet<i32>,
}
```

```csharp
// C#側では ctx = void* として受け取る
var ctx = NativeMethods.create_counter_context();
    
NativeMethods.insert_counter_context(ctx, 10);
NativeMethods.insert_counter_context(ctx, 20);

NativeMethods.delete_counter_context(ctx);
```

この辺、`PhantomData<T>`を使って格好良く処理する手法も一応あるんですが、正直複雑になるだけなので、素直に `void*` ベースでやり取りする、に倒したほうがむしろ健全でいいのではと思っています。どっちにしろunsafeな処理してるんだから素直にunsafeな業を受け入れるべき！


Stringと配列のマーシャリング
---
Stringと配列は、C#とRustでそれぞれ構造が違うので、そのままやり取りはできません。ポインタと長さ、つまりC#でいうところのSpanのみがやり取りできます。Span的な処理をするだけならゼロコピーですが、Stringや配列に変換したくなったら、C#とRust、どちらの側でも新規のアロケーションが発生します。これはネイティブコードを導入することの弱みで、Pure C#で通したほうが融通が効く（或いはパフォーマンスに有利に働く）ポイントですね。まあ、ともあれ、つまり基本はSpanです。DllImport上でStringを受けたり配列を受けたりしてはいけません、その手の自動変換にゆだねてはダメ！アロケーションも自己責任で明示的に。

さて、まずは文字列ですが、こういったケースでやり取りする文字列の種類は3つ、UTF8とUTF16と[ヌル終端文字列](https://ja.wikipedia.org/wiki/%E3%83%8C%E3%83%AB%E7%B5%82%E7%AB%AF%E6%96%87%E5%AD%97%E5%88%97)、です。UTF8はRustの文字列(RustのStringは`Vec<u8>`)、C#の文字列はUTF16、そしてCのライブラリなどはヌル終端文字列を返してくることがあります。

今回は例なので明示的にRust上でヌル終端文字列を返してみます。

```rust
#[no_mangle]
pub extern "C" fn alloc_c_string() -> *mut c_char {
    let str = CString::new("foo bar baz").unwrap();
    str.into_raw()
}

#[no_mangle]
pub unsafe extern "C" fn free_c_string(str: *mut c_char) {
    unsafe { CString::from_raw(str) };
}
```

```csharp
// null-terminated `byte*` or sbyte* can materialize by new String()
var cString = NativeMethods.alloc_c_string();
var str = new String((sbyte*)cString);
NativeMethods.free_c_string(cString);
```

C#上では new Stringでポインタ(`sbyte*`)を渡すとヌル終端を探してStringを作ってくれます。明示的にアロケーションしているという雰囲気がいいですね。ポインタはこの場合Rustで確保したメモリなので、C#のヒープ上にコピー（新規String作成）したなら、即返却してやりましょう。

Rustで確保したUTF8、byte[]、あるいはint[]などとにかく配列全般の話はもう少し複雑になってきます。Rustでの配列的なもの(`Vec<T>`)をC#に渡すにあたっては、ポインタと長さをC#に渡せばOKといえばOKなのですが、解放する時にそれだけだと困ります。`Vec<T>`の実態はポインタ、長さ、そしてキャパシティの3点セットになっているので、この3つを渡さなきゃいけないのですね。そして、都度3点セットを処理するのも面倒です、Rust的なメモリ管理を外したり戻したりの作業もあるし。

というわけでちょっと長くなりますが以下のようなユーティリティーを用意しましょう。これの元コードは(元)Rustの開発元であるMozillaのコードなので安全安心です……！

```rust
#[repr(C)]
pub struct ByteBuffer {
    ptr: *mut u8,
    length: i32,
    capacity: i32,
}

impl ByteBuffer {
    pub fn len(&self) -> usize {
        self.length.try_into().expect("buffer length negative or overflowed")
    }

    pub fn from_vec(bytes: Vec<u8>) -> Self {
        let length = i32::try_from(bytes.len()).expect("buffer length cannot fit into a i32.");
        let capacity = i32::try_from(bytes.capacity()).expect("buffer capacity cannot fit into a i32.");

        // keep memory until call delete
        let mut v = std::mem::ManuallyDrop::new(bytes);

        Self {
            ptr: v.as_mut_ptr(),
            length,
            capacity,
        }
    }

    pub fn from_vec_struct<T: Sized>(bytes: Vec<T>) -> Self {
        let element_size = std::mem::size_of::<T>() as i32;

        let length = (bytes.len() as i32) * element_size;
        let capacity = (bytes.capacity() as i32) * element_size;

        let mut v = std::mem::ManuallyDrop::new(bytes);

        Self {
            ptr: v.as_mut_ptr() as *mut u8,
            length,
            capacity,
        }
    }

    pub fn destroy_into_vec(self) -> Vec<u8> {
        if self.ptr.is_null() {
            vec![]
        } else {
            let capacity: usize = self.capacity.try_into().expect("buffer capacity negative or overflowed");
            let length: usize = self.length.try_into().expect("buffer length negative or overflowed");

            unsafe { Vec::from_raw_parts(self.ptr, length, capacity) }
        }
    }

    pub fn destroy_into_vec_struct<T: Sized>(self) -> Vec<T> {
        if self.ptr.is_null() {
            vec![]
        } else {
            let element_size = std::mem::size_of::<T>() as i32;
            let length = (self.length * element_size) as usize;
            let capacity = (self.capacity * element_size) as usize;

            unsafe { Vec::from_raw_parts(self.ptr as *mut T, length, capacity) }
        }
    }

    pub fn destroy(self) {
        drop(self.destroy_into_vec());
    }
}
```

Box::into_raw/from_rawのVec版という感じで、from_vecしたタイミングでメモリ管理から外すのと、destroy_into_vecするとメモリ管理を呼び側に戻す（何もしなければスコープを抜けて破棄される）といったような動作になっています。これはC#側でも(csbindgenによって)定義が生成されているので、メソッドを追加してやります。

```csharp
// C# side span utility
partial struct ByteBuffer
{
    public unsafe Span<byte> AsSpan()
    {
        return new Span<byte>(ptr, length);
    }

    public unsafe Span<T> AsSpan<T>()
    {
        return MemoryMarshal.CreateSpan(ref Unsafe.AsRef<T>(ptr), length / Unsafe.SizeOf<T>());
    }
}
```

これでByteBuffer*で受け取ったものを即Spanに変換できるようになりました！というわけで、Rust上の通常のstring、byte[]、それとint[]の例を見てみると

```rust
#[no_mangle]
pub extern "C" fn alloc_u8_string() -> *mut ByteBuffer {
    let str = format!("foo bar baz");
    let buf = ByteBuffer::from_vec(str.into_bytes());
    Box::into_raw(Box::new(buf))
}

#[no_mangle]
pub unsafe extern "C" fn free_u8_string(buffer: *mut ByteBuffer) {
    let buf = Box::from_raw(buffer);
    // drop inner buffer, if you need String, use String::from_utf8_unchecked(buf.destroy_into_vec()) instead.
    buf.destroy();
}

#[no_mangle]
pub extern "C" fn alloc_u8_buffer() -> *mut ByteBuffer {
    let vec: Vec<u8> = vec![1, 10, 100];
    let buf = ByteBuffer::from_vec(vec);
    Box::into_raw(Box::new(buf))
}

#[no_mangle]
pub unsafe extern "C" fn free_u8_buffer(buffer: *mut ByteBuffer) {
    let buf = Box::from_raw(buffer);
    // drop inner buffer, if you need Vec<u8>, use buf.destroy_into_vec() instead.
    buf.destroy();
}

#[no_mangle]
pub extern "C" fn alloc_i32_buffer() -> *mut ByteBuffer {
    let vec: Vec<i32> = vec![1, 10, 100, 1000, 10000];
    let buf = ByteBuffer::from_vec_struct(vec);
    Box::into_raw(Box::new(buf))
}

#[no_mangle]
pub unsafe extern "C" fn free_i32_buffer(buffer: *mut ByteBuffer) {
    let buf = Box::from_raw(buffer);
    // drop inner buffer, if you need Vec<i32>, use buf.destroy_into_vec_struct::<i32>() instead.
    buf.destroy();
}
```

ByteBuffer自体の管理を外す(into_raw)が必要なのと、from_rawで戻したあとの中身のByteBufferもdestoryかinto_vecしなきゃいけないという、入れ子の管理になっているというのが紛らわしくて死にそうになりますが、ソウイウモノということで諦めましょう……。Drop traitを実装しておくことでクリーンナップ側の処理はもう少しいい感じにできる余地がありますが、Drop traitを実装しないことの理由もそれなりにある（と、Mozillaが言っている）ので、トレードオフになっています。

C#側では、とりあえずAsSpanして、あとはよしなにするという感じですね。

```csharp
var u8String = NativeMethods.alloc_u8_string();
var u8Buffer = NativeMethods.alloc_u8_buffer();
var i32Buffer = NativeMethods.alloc_i32_buffer();
try
{
    var str = Encoding.UTF8.GetString(u8String->AsSpan());
    Console.WriteLine(str);

    Console.WriteLine("----");

    var buffer = u8Buffer->AsSpan();
    foreach (var item in buffer)
    {
        Console.WriteLine(item);
    }

    Console.WriteLine("----");

    var i32Span = i32Buffer->AsSpan<int>();
    foreach (var item in i32Span)
    {
        Console.WriteLine(item);
    }
}
finally
{
    NativeMethods.free_u8_string(u8String);
    NativeMethods.free_u8_buffer(u8Buffer);
    NativeMethods.free_i32_buffer(i32Buffer);
}
```

Rust側で確保したメモリはRust側で解放する！という基本に関しては忠実に守っていきましょう。この例だとC#側で処理したら即解放なので、いい感じにしてくれよ、なんだったらDllImportで暗黙的に自動処理最高、みたいな気になるかもしれませんが、もう少し長寿命で持つケースもあるので、やはりマニュアルでちゃんと解放していきましょう。ていうか暗黙的なアロケーションは一番最悪じゃないです？？？

最後に、C#で確保したメモリをRust側で使う場合の例をどうぞ。

```rust
#[no_mangle]
pub unsafe extern "C" fn csharp_to_rust_string(utf16_str: *const u16, utf16_len: i32) {
    let slice = std::slice::from_raw_parts(utf16_str, utf16_len as usize);
    let str = String::from_utf16(slice).unwrap();
    println!("{}", str);
}

#[no_mangle]
pub unsafe extern "C" fn csharp_to_rust_utf8(utf8_str: *const u8, utf8_len: i32) {
    let slice = std::slice::from_raw_parts(utf8_str, utf8_len as usize);
    let str = String::from_utf8_unchecked(slice.to_vec());
    println!("{}", str);
}


#[no_mangle]
pub unsafe extern "C" fn csharp_to_rust_bytes(bytes: *const u8, len: i32) {
    let slice = std::slice::from_raw_parts(bytes, len as usize);
    let vec = slice.to_vec();
    println!("{:?}", vec);
}
```

```csharp
var str = "foobarbaz:あいうえお"; // JPN(Unicode)
fixed (char* p = str)
{
    NativeMethods.csharp_to_rust_string((ushort*)p, str.Length);
}

var str2 = Encoding.UTF8.GetBytes("あいうえお:foobarbaz");
fixed (byte* p = str2)
{
    NativeMethods.csharp_to_rust_utf8(p, str2.Length);
}

var bytes = new byte[] { 1, 10, 100, 255 };
fixed (byte* p = bytes)
{
    NativeMethods.csharp_to_rust_bytes(p, bytes.Length);
}
```

std::slice::from_raw_partsでSliceを作って、あとはよしなに処理したいことをします。関数を超えて長い寿命を持たせたいならコピー(String作りなりVec作るなり)は必須になってきます。Rust側で確保したメモリはRust側で解放する、のと同じように、C#側で確保したメモリはC#側で解放する、のが重要です。C#の場合はfixedスコープを抜けて参照を持っていない場合は、そのうちGCが処理してくれるでしょう、といった話ですね。

なお、fixedを超えてC#でもう少し長い寿命で持ち回したいときは `GCHandle.Allocc(obj, GCHandleType.Pinned)` して持ち回します。

Rust for C# Developer
---
Rustは、正直すごい気に入ってます。C#の次に気に入りました……！まぁ正直、これで全部やる、Webもなにもかも作る、みたいなのはヤバいかな、と思います。RustでWebやりたいって人はあれでしょ、型がついてて開発環境が充実していてエコシステムが回ってる言語がいいんでしょ？ちょうどいい言語があるんですよ、C#という。……。ではあるんですが、ネイティブが必要って局面で、やりたくないーって逃げたり、NativeAOTがなんとかしてくれるだのといった現実逃避したりせず、ちゃんと正面から向き合えるようになったということはいいことです。

で、実際RustはかなりC#erに馴染む道具だと思っていて、そもそもインターフェイスがないかわりにstructとジェネリクスとtrait(インターフェイスみたいなやつ)で処理するってのは、別にそれC#でもやってますよ！C#のパフォーマンス最速パターンってstructにインターフェイス実装してジェネリクスの型制約でインターフェイス指定してボクシング/仮想メソッド呼び出し回避でstruct投げ込むことですからね。ようはC#の最速パターンだけが強制されてるんだと思えば何も違和感がない。

インスタンスメソッドがないかわりに全部拡張メソッドみたいな雰囲気なのも、いやー、C#も、もはやインスタンスメソッドと拡張メソッド、どっちで実装すればいいかなーって切り分けに悩むこともあるし、[C# 12候補のExtensions](https://ufcpp.net/blog/2023/3/extensions/)なんてきたら完全にどこで実装すりゃいいのかわからんわ、ってなるので、拡張メソッド一択(impl, trait)ですよ、みたいなのはすっきり整理されていて逆にいい。

シンタックスも自然というかC系の多数派に寄り添った感じで親しみやすいし、ドットでメソッド繋げていくので、馴染み深いオブジェクト指向的な手触りが十分ある。それとミュータブルに寛容なところがいいですね。関数型にありがちなイミュータブル至上主義ではなく、どちらかというとメモリそこにあるんだからミュータブルやろ、みたいな雰囲気なのがとてもいい。無駄もないし。所有権周りが厳密なのでミュータブルであっても固めな手応えなのは、これでいいんだよというかこれで的な何かではある。

マクロはコンパイル時ExpresionTreeみたいなもので、proc-macroはSource Generatorみたいなものなので、何が可能になるかすぐに理解できるし、便利さもよくわかる。ていうかコンパイル時ExpressionTreeはC#にも欲しい（実行時だからコスト重いのであんま使わないのでコンパイル時に解決するならもっとばんばん使えるはずなんだよねえ）

キツいかなーと思うのは所有権がどうとかっていうよりも、ジェネリクスの見た目がキツい。C#だったらインターフェイスで動的ディスパッチで整理されているものが、ジェネリクスで静的ディスパッチに倒れているのでジェネリクスの出現率がめっちゃ高い。いや、だってC#でもジェネリクスでると読みやすさ的には一段落下がるわけじゃないですか、それが当たり前って感じだと、慣れとかって問題じゃなく見やすさレベルは下がる。更にその上にジェネリクスがネストするのが当たり前。C#だったらジェネリクスがネストしてるのは見やすさレベル最底辺なので極力出現しないようにしたいって感じなのですが、Rustだと日常茶飯事に出てくる。`Option<Rc<RefCell<_>>>`とかも全然普通に出現するのが、うーむ。理屈では納得いくから特に文句があるようでなにもないんですが。

なんだったらパターンマッチも別に好きじゃないしOptionもResultも好きじゃないしnullの何が悪いんだよぐらいの気持ちにならなくもないんですが、まぁそれはそれ。でも全体的には凄い良いですね、ほんと。

まとめ
---
ところで[csbindgen](https://github.com/Cysharp/csbindgen)のReadMeのほうには更にもっといっぱい変換パターンを紹介していますので、是非そちらもチェックしてみてください。

ネイティブ呼び出しは定義の部分でも、二重定義がそもそもダルいうえに、かなり気を使わなきゃいけないことがなにげに多くて割と大変というか知識量と単純作業量を要求してくるのですが、csbindgenはその部分を完全自動化してくれます。自分でも使っててネイティブコードめっちゃ楽……！という気になります。事実楽。すごい。その後のメモリ管理に関しては、そこはまぁ思う存分悩んでくれという話になるのですが、もはや複雑な点がそれだけに落ち着いたという点では、やはり革命的に便利なのでは？という気になります。

Cのライブラリを持ってくるのも圧倒的に楽なので、私の中でもちょっと考え方が変わってきました。今までは割とPure C#実装至上主義、みたいなところがあったんですが、うまい切り分け、使い分けみたいなのを考えられるようになりました。そして、Cライブラリ利用がより自由になると、まさに[Cysharp](https://cysharp.co.jp/)の掲げる「C#の可能性を切り開いていく」ことにまた一つ繋がってしまったな、と。

まずはこの後に数個、csbindgenを活用したC#ライブラリを提供する予定があります！のですが、その前に、Rustかー、とは思わずに是非csbindgen、試してみてもらえると嬉しいです。