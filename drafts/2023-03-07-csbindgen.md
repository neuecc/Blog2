# csbindgen

ネイティブコードとC#を透過的に接続するために、RustのFFIからC#のDllImportコードを自動生成するライブラリを作成し、公開しました。Cysharp初のRustライブラリです！先週にプレビューを出していましたが、しっかりした機能強化とReadMeの充実をして正式公開、です！

* [Cysharp/csbindgen](https://github.com/Cysharp/csbindgen)
* [crates.io/crates/csbindgen](https://crates.io/crates/csbindgen)

めちゃくちゃスムーズにネイティブコードがC#から呼べるようになります。すごい簡単に。超便利。こりゃもうばんばんネイティブコード書きたくなりますね……！

しかしまず前提として言っておくと、ネイティブコードは別に偉くもなければ必ず速いというわけでもないので、極力書くのはやめましょう。C#で書くべき、です。高速なコードが欲しければ、ネイティブコードに手を出す前にC#で速くすることを試みたほうがずっと良いです。C#は十分高速に書くことのできる言語です！ネイティブコードを書くべきでない理由は山ほどありますが、私的に最大の避けたい理由はクロスプラットフォームビルドで、今の世の中、ターゲットにしなければならないプラットフォーム/アーキテクチャの組み合わせは、普通にやっていても10を超えてしまいます。win/linux/osx/iOS/Android x x86/x64/arm。C#では .NET のランタイムやUnityが面倒見てくれますが、ネイティブコードの場合はこれを自前で面倒みていく必要があります。そこそこ面倒みてくれるはずのUnityだって辛いのに、それにプラスして俺々ビルド生態系を加えるのはかなり厳しいものがある。

しかし、とはいえ、C#をメインに据えつつもネイティブコードを利用すべきシチュエーションもあるにはあります。

* Android NDKや .NET unmanaged hosting APIなど、ネイティブAPIしか提供されていないものを使いたい場合
* C で作られているネイティブライブラリを利用したい場合
* ランタイムのライブラリの利用を避けたい場合、例えばUnityで .NET のSocket(Unityの場合 .NET のランタイムが古いのでパフォーマンスを出しにくい)を避けてネイティブのネットワークコードを書くのには一定の道理がある

[NativeAOT](https://learn.microsoft.com/ja-jp/dotnet/core/deploying/native-aot/)という解決策もなくはないですが、まぁまだそんなに現実的でもなければ、用途的にもこういうシチュエーションでは限定的でもあるので、素直にネイティブコードを書いていくべき、でしょう。

そこでの最初の選択肢は当然C++なわけですが、いやー、C++のクロスプラットフォームビルドは嫌だなあ、C++もあんま書きたくないしなあ。となると、[Zig](https://ziglang.org/ja/)はどうだろうか、と試してみました、が、撤退。目指すコンセプトは大変共感するところがあるのですが(FFIなしのCライブラリとの統合や、安全だけど複雑さを抑えた文法など)、まだ、完成度が、かなり、厳しい……。

で、最後の選択肢がRustでした。FFIなしではないものの[cc crate](https://crates.io/crates/cc)や[cmake crate](https://crates.io/crates/cmake)といったライブラリを使うと自然に統合されるし、[bindgen](https://github.com/rust-lang/rust-bindgen)によるバインディングの自動生成はよく使われているだけあってめっちゃ安定して簡単に生成できる。開発環境もまぁまぁ充実してるしコマンド体系も現代的。クロスプラットフォームビルドも容易！そして難しいと評判で避けていた言語面でも、いや、全然いいね。仕組みが理屈で納得できるし、C#とは文法面でもあまり離れていないので、全然すんなりと入れました。もちろん難しいところも多々ありますが、ラーニングカーブはそんなに急ではない、少なくとも最近のモダンC#をやり込んでる人なら全然大丈夫でしょう……！

と、いうわけで、しかし主な用途はC#からの利用で、特にCライブラリの取り込みにRustを使おうと思った時にC#に対して公開するためのコードが膨大でキツかったので、自動化したかったんですね。DllImportの自動化は[SWIG](https://www.swig.org/)や[CppSharp](https://github.com/mono/CppSharp)というのもありますが、普通のC++をそのまま持ってこようとすると、複雑なコードを吐いてしまったりで中々どうかなあ、と。Cのコードはbindgenによって綺麗なRustに整形してもらって、csbindgenが生成対象にするのはそうしたFFI向けに整理されたRustのみを対象にすることで、精度と生成コードの単純さを担保しました。また、C#側の生成コードも現代的な雰囲気(C# 9.0からの関数ポインターを使う、Spanで扱いやすいように逆にポインタのままにするなどDllImportの暗黙的な変換はなしにする)にすることでパフォーマンス面でのロスをなくしています。

Getting Started
---
コンフィグにビルド時依存に追加してもらって、`build.rs`というコンパイル前呼び出しに設定を入れるだけです、簡単！

```
[build-dependencies]
csbindgen = "1.1.0"
```

```rust
csbindgen::Builder::default()
    .input_extern_file("lib.rs")
    .csharp_dll_name("nativelib")
    .generate_csharp_file("../dotnet/NativeMethods.g.cs")
    .unwrap();
```

単純なコードでは、

```csharp
#[no_mangle]
pub extern "C" fn my_add(x: i32, y: i32) -> i32 {
    x + y
}
```

これは

```csharp
// NativeMethods.g.cs
using System;
using System.Runtime.InteropServices;

namespace CsBindgen
{
    internal static unsafe partial class NativeMethods
    {
        const string __DllName = "nativelib";

        [DllImport(__DllName, EntryPoint = "my_add", CallingConvention = CallingConvention.Cdecl)]
        public static extern int my_add(int x, int y);
    }
}
```

こうなる、と。生成はstructやunion、関数やポインターなどRustのFFIで流せる型のほとんどには対応しています。

また、Rustのbindgenやcc/cmake crateを併用すると、CのライブラリをC#に簡単に持ちこむことができます。例えば圧縮ライブラリの[lz4](https://github.com/lz4/lz4)は、csbindgenでの生成の前にbindgenとccの設定も足してあげると

```csharp
// using bindgen, generate binding code
bindgen::Builder::default()
    .header("c/lz4/lz4.h")
    .generate().unwrap()
    .write_to_file("lz4.rs").unwrap();

// using cc, build and link c code
cc::Build::new().file("lz4.c").compile("lz4");

// csbindgen code, generate both rust ffi and C# dll import
csbindgen::Builder::default()
    .input_bindgen_file("lz4.rs")            // read from bindgen generated code
    .rust_file_header("use super::lz4::*;")     // import bindgen generated modules(struct/method)
    .csharp_entry_point_prefix("csbindgen_") // adjust same signature of rust method and C# EntryPoint
    .csharp_dll_name("liblz4")
    .generate_to_file("lz4_ffi.rs", "../dotnet/NativeMethods.lz4.g.cs")
    .unwrap();
```

C#から呼び出せるコードが簡単に生成できますし、ビルドもRustで `cargo build` するだけです。

```csharp
// NativeMethods.lz4.g.cs

using System;
using System.Runtime.InteropServices;

namespace CsBindgen
{
    internal static unsafe partial class NativeMethods
    {
        const string __DllName = "liblz4";

        [DllImport(__DllName, EntryPoint = "csbindgen_LZ4_compress_default", CallingConvention = CallingConvention.Cdecl)]
        public static extern int LZ4_compress_default(byte* src, byte* dst, int srcSize, int dstCapacity);

        // snip...
    }
}
```

これはやってもらうとめっちゃ簡単に持ち込みができて感動します。偉いのはRustやbindgenなわけですが、いやー、いいですね、とてもいい……。

Unityでの利用も念頭においているので、よくあるiOSでのIL2CPPだけ __Internal にしたい

```csharp
#if UNITY_IOS && !UNITY_EDITOR
        const string __DllName = "__Internal";
#else
        const string __DllName = "nativelib";
#endif
```

といったような生成ルールの変更もコンフィグに含めてあります。とても実用的で気が利いてます。

LibraryImport vs DllImport
---
.NET 7から[LibraryImport](https://learn.microsoft.com/ja-jp/dotnet/standard/native-interop/pinvoke-source-generation)という新しい呼び出しのためのソースジェネレーターが追加されました。これはDllImportのラッパーになっていて、DllImportは、本来ネイティブコードとやり取りできない型(例えば配列や文字列などの参照型はC#のヒープ上に存在するもので、ネイティブ側に渡せない)を裏で自動的にやってくれるという余計なお世話が含まれていて、それがややこしさや性能面、そしてNativeAOTビリティの欠如などの問題を含んでいたので、そういう型が渡された場合はLibraryImportの生成するC#コードで吸収した上で、byte* としてDllImportに渡すようなラッパーが生成されるようになっています。

まぁ、つまり余計なお世話をする本来ネイティブコードとやり取りできない型を生成しないようにすればDllImportでも何の問題もないので、今回はDllImportでの生成を選んでいます。そのほうがUnityでも使いやすいし。

自動的な変換、しかも前提がWin32 APIとの通信でC ABIとちょっとルール違う、というのは時代背景によるものでDllImportの設計が古いせいだと思っているので、現代ではそれに頼るべきではない、つまり参照型を渡したり[In]や[Out]は使うべきではない、と私は考えています。

実際 .NET 7ではそうしたDllImportの機能を使うとエラーにする[DisableRuntimeMarshallingAttribute](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.compilerservices.disableruntimemarshallingattribute)が追加されました。

ポインターに関しても今はあまり忌避するものではないと思っています。そもそもネイティブとの通信はunsafeだし、Spanによって比較的使いやすい型に変換することも容易なので。中途半端に隠蔽するぐらいなら、DllImportするレイヤーではポインターはポインターとして持っておきましょう。C#として使いやすくするのは、その外側できっちりやればいい話です、DllImportで吸収するものではない。

Stringと配列のマーシャリング
---


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



```rust
#[repr(C)]
pub struct ByteBuffer {
    ptr: *mut u8,
    length: i32,
    capacity: i32,
}

impl ByteBuffer {
    pub fn len(&self) -> usize {
        self.length
            .try_into()
            .expect("buffer length negative or overflowed")
    }

    pub fn from_vec(bytes: Vec<u8>) -> Self {
        let length = i32::try_from(bytes.len()).expect("buffer length cannot fit into a i32.");
        let capacity =
            i32::try_from(bytes.capacity()).expect("buffer capacity cannot fit into a i32.");

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
            let capacity: usize = self
                .capacity
                .try_into()
                .expect("buffer capacity negative or overflowed");
            let length: usize = self
                .length
                .try_into()
                .expect("buffer length negative or overflowed");

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


関数のマーシャリング
---

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

```csharp
[DllImport(__DllName, EntryPoint = "csharp_to_rust", CallingConvention = CallingConvention.Cdecl)]
public static extern void csharp_to_rust(delegate* unmanaged[Cdecl]<int, int, int> cb);

[DllImport(__DllName, EntryPoint = "rust_to_csharp", CallingConvention = CallingConvention.Cdecl)]
public static extern delegate* unmanaged[Cdecl]<int, int, int> rust_to_csharp();
```




```csharp
// true(default) generates delegate*
[DllImport(__DllName, EntryPoint = "callback_test", CallingConvention = CallingConvention.Cdecl)]
public static extern int callback_test(delegate* unmanaged[Cdecl]<int, int> cb);

// You can define like this callback method.
[UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
static int Method(int x) => x * x;

// And use it.
callback_test(&Method);

// ---

// false will generates Action/Func, it is useful for Unity
[DllImport(__DllName, EntryPoint = "callback_test", CallingConvention = CallingConvention.Cdecl)]
public static extern int callback_test(Func<int, int> cb);

// Unity can define callback method as MonoPInvokeCallback
[MonoPInvokeCallback(typeof(Func<int, int>))]
static int Method(int x) => x * x;

// And use it.
callback_test(Method);
```

Rust for C# Developer
---
TODO:




まとめ
---
TODO: