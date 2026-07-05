# AI時代におけるC#でのBenchmarkDotNetを使った最速コードの書き方

![](/article_img/20260706_1.png)

AI時代においてテストが、AI Agentがループを回すための足腰として重要なように、プロファイラーも思考のための素材を与えるという点で重要です。従来は、基本的には `Mean` と `Allocated` を見るぐらいでしたが、どうせAIが読むんだったら、与えられる情報は多ければ多いほどいい、ということで `JIT Disassembler` や `BranchMispredictions/Op` も追加していきましょう、デフォルトで。というお話です。この記事では最後にMessagePack for C#の整数値のWriteを改善する例を紹介しますが、asmや分岐予測のデータを元にして、Fableは人間（私）を遥かに超えるコードを生成してくれました。

![](/article_img/20260706_2.png)

↑なんと4倍速の余地がある。

なにせAIはこの手の解析は得意技ですから！得意技というのもあるのですが、「なぜそのコードが速くなったのか」を理解するためには、元コード, Mean, Allocatedだけでは証拠が足りないのです。証拠が足りないと、理由付けは推測でしかなくて、外れることがある。それでは改善ループは進まない、これは別に熟練した人間でも、AIであっても変わらない話であり、人間だけがやってきた今までは、なんとなくの理由付けで済ませて、それで終わらせていたことも多くありました。AIなら機械語を解析するコストはゼロなので、押し付けられるコンテキストは可能な限り押し付けてやりましょう。

手法の説明をするにあたって、`Skill` だの `AGENTS.md`だのは、賞味期限も短いので特に書くことでもないと思っています。新しいモデルが来るたびにゲームチェンジでゲームエンドして手のひらクルクルしててどうするの？小手先のスキル・ハーネスも色々ありますが、やはり賞味期限が短く感じます。別にAI驚き屋が言うような、AIを今すぐ使いこなさなければ乗り遅れる、なんてこたぁないんですよね。明らかに去年よりも、いや、半年前よりも使いこなすのがイージーになってきているので、手のひらクルクルしてる時間を真剣に生きたほうがいいですよ絶対……！

Fable 5も出ましたし、[Anthrorpic公式のFable 5のプロンプトガイド](https://platform.claude.com/docs/en/build-with-claude/prompt-engineering/prompting-claude-fable-5)にもありますが、過剰な指示よりも簡潔に要点絞って方向性を合わせるほうがいいかなあ、と思います。で、これってプロンプトエンジニアリングやハーネス作りに血道をあげるよりは、ずっとイージー度合いが高まっているわけですよね。そして的確な指示を行うには的確な理解力が必要になる、それはプロダクトであれば深いドメイン知識であったりプラットフォームへの理解であったり、ライブラリであれば言語や環境への理解が大事なわけです。たとえAIのほうが知識があったとしても、方向性を示せなければ、真に求めるものを得るのはランダムなガチャを引いて当たりを手にすることを祈るようなものであり、実現は難しいのです。

## 最速のSum

まずはベンチマークの骨組みから、ということで簡単な指示を与えて作らせます。

```
C#でbenchmarkdotnetを使って小さな関数を、なにが最速なのかを確認しながら、AIが結果を読み取って最適なコードを見つける仕組みを作りたい。実行速度だけじゃなくて、JIT結果の機械語のdisassembleとその解析も行ってください。
```

もう少し短くてもいいかなという感じですが、短くしすぎて外してやり取りの往復するのはダルいので、このぐらいで。手順や解析のポイントなんかはAIが自分で作り出して文章化して膨らませてくれるわけだから、秘伝のSkillはいらないかな、と思います。どちらかというと用途別のオーダーメイドなほうがいいとすら思っているので、あれこれ足してコンテキスト汚染するよりも、簡潔なところから、簡潔さを維持しながら育てていくべきではないかな、と。大きなアプリケーションであれば、最終的に固有情報がみっちりかかれた代物が出来上がるかもしれませんが、それはそれで、ようするにオーダーメイドの一点ものが出来上がったということで、とても良いことです。

さて、とりあえずのサンプルとしてAIはSumのベンチマークを作ってきました。これはOpusでもFableでもそうだったし、わかりやすいので、いい例だとは思います。もし作ってこないようだったらSumのベンチマーク作って、とプロンプトに足しておけばいいでしょう。

Baselineのコードは、シンプルな配列のSumのfor loopです。

```csharp
[Benchmark(Baseline = true)]
public int ForLoop()
{
	var d = data;
	int sum = 0;
	for (int i = 0; i < d.Length; i++)
	{
		sum += d[i];
	}
	return sum;
}
```

結果はこうです。

| Method              |      Mean |
| ------------------- | --------: |
| ForLoop             | 211.31 ns |
| ForEachLoop         | 211.44 ns |
| LinqSum             |  59.42 ns |
| VectorSimd          |  36.42 ns |
| TensorPrimitivesSum |  13.54 ns |
| VectorRef           |  32.44 ns |
| Vector512Unrolled   |  22.05 ns |
| Vector512Final      |  18.36 ns |

SIMDにして速くなりましたー、みたいなのは当然かな、と思うのですが、なんか色々やってくれてますね。これは、理屈としてはAIがasmを見ながら3 roundで最適化処理をかけていったという体になります。

| Round | 実装                   | Mean      | asm から見つけた欠陥 → 次の一手                                        |
| ----- | -------------------- | --------- | ---------------------------------------------------------- |
| 1     | ForLoop (baseline)   | 203 ns    | 未ベクトル化                                                     |
| 1     | VectorSimd           | 33 ns     | Slice の範囲チェックが毎反復残存 → ref ベース化                             |
| 2     | Vector512Unrolled    | 19 ns     | `movsxd` が毎ロード前に挿入、端数がスカラー → nuint 化+マスク端数                 |
| 3     | **Vector512Final**   | **18 ns** | メインループが純粋な `vpaddd zmm`×4、端数も AVX-512 マスクレジスタ処理。手書きの理想形に到達 |
| —     | TensorPrimitives.Sum | 13 ns     | 天井(64B アラインメント調整+8段アンロール)                                  |

この結果は究極で、これ以上言えることはなんもありません。ちなみにOpusではここまでたどり着かなかったので、やはりFableは強力だな、と思いました。

さて、コードを見ていきましょう。SIMD化はシンプルな話で、通常だと手書きでもこのぐらいは書くし、このぐらいでいっか、で終わらせるところです。

```csharp
[Benchmark]
public int VectorSimd()
{
	var d = data.AsSpan();
	var acc = Vector<int>.Zero;
	int i = 0;
	for (; i <= d.Length - Vector<int>.Count; i += Vector<int>.Count)
	{
		acc += new Vector<int>(d.Slice(i));
	}
	int sum = Vector.Sum(acc);
	for (; i < d.Length; i++)
	{
		sum += d[i];
	}
	return sum;
}
```

今回の試行ではその先へもグイグイ進んで、Round3の最終形は、アンロール頑張りました。

```csharp
// Round 3: nuint indexing (kills movsxd) + masked vector tail (kills scalar remainder loop)
[Benchmark]
public int Vector512Final()
{
	ref int p = ref MemoryMarshal.GetArrayDataReference(data);
	nuint length = (nuint)data.Length;
	nuint vc = (nuint)Vector512<int>.Count;

	if (!Vector512.IsHardwareAccelerated || length < vc)
	{
		int s = 0;
		for (nuint j = 0; j < length; j++)
		{
			s += Unsafe.Add(ref p, j);
		}
		return s;
	}

	var acc0 = Vector512<int>.Zero;
	var acc1 = Vector512<int>.Zero;
	var acc2 = Vector512<int>.Zero;
	var acc3 = Vector512<int>.Zero;
	nuint i = 0;
	if (length >= vc * 4)
	{
		nuint lastBlock = length - vc * 4;
		for (; i <= lastBlock; i += vc * 4)
		{
			acc0 += Vector512.LoadUnsafe(ref p, i);
			acc1 += Vector512.LoadUnsafe(ref p, i + vc);
			acc2 += Vector512.LoadUnsafe(ref p, i + vc * 2);
			acc3 += Vector512.LoadUnsafe(ref p, i + vc * 3);
		}
	}
	for (nuint lastVector = length - vc; i <= lastVector; i += vc)
	{
		acc0 += Vector512.LoadUnsafe(ref p, i);
	}
	if (i < length)
	{
		// overlapping load of the final vector, masking out already-summed lanes
		nuint offset = length - vc;
		var lane = Vector512<int>.Indices + Vector512.Create((int)offset);
		var mask = Vector512.GreaterThanOrEqual(lane, Vector512.Create((int)i));
		acc1 += mask & Vector512.LoadUnsafe(ref p, offset);
	}
	return Vector512.Sum((acc0 + acc1) + (acc2 + acc3));
}
```

こうしたラウンドの進行に欠かせなかったのが BenchmarkDotNetの `DisassemblyDiagnoser` で、ベンチ実行結果のasmを取得してくれます。

```csharp
var config = ManualConfig.Create(DefaultConfig.Instance)
    .AddJob(Job.ShortRun) // 3 warmup + 3 iterations, fast feedback loop for AI iteration
    .AddDiagnoser(MemoryDiagnoser.Default)
    .AddDiagnoser(new DisassemblyDiagnoser(new DisassemblyDiagnoserConfig(
        maxDepth: 3,
        printSource: true,
        exportGithubMarkdown: true,
        exportCombinedDisassemblyReport: true)))
    .AddExporter(JsonExporter.Full);
```

これによってシンプルなfor-loopの結果から

```assembly
; SumBenchmark.ForLoop()
;         var d = data;
;         ^^^^^^^^^^^^^
;         int sum = 0;
;         ^^^^^^^^^^^^
;         for (int i = 0; i < d.Length; i++)
;              ^^^^^^^^^
;             sum += d[i];
;             ^^^^^^^^^^^^
;         return sum;
;         ^^^^^^^^^^^
       mov       rax,[rcx+8]
       xor       ecx,ecx
       mov       edx,[rax+8]
       test      edx,edx
       jle       short M00_L01
       add       rax,10
M00_L00:
       add       ecx,[rax]
       add       rax,4
       dec       edx
       jne       short M00_L00
M00_L01:
       mov       eax,ecx
       ret
; Total bytes of code 30
```

ここまで機械語の命令を見て自律的な改善を試行してくれます。

```assembly
; SumBenchmark.Vector512Final()
;         ref int p = ref MemoryMarshal.GetArrayDataReference(data);
;         ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^
;         nuint length = (nuint)data.Length;
;         ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^
;         nuint vc = (nuint)Vector512<int>.Count;
;         ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^
;         if (!Vector512.IsHardwareAccelerated || length < vc)
;         ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^
;             int s = 0;
;             ^^^^^^^^^^
;             for (nuint j = 0; j < length; j++)
;                  ^^^^^^^^^^^
;                 s += Unsafe.Add(ref p, j);
;                 ^^^^^^^^^^^^^^^^^^^^^^^^^^
;             return s;
;             ^^^^^^^^^
;         var acc0 = Vector512<int>.Zero;
;         ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^
;         var acc1 = Vector512<int>.Zero;
;         ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^
;         var acc2 = Vector512<int>.Zero;
;         ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^
;         var acc3 = Vector512<int>.Zero;
;         ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^
;         nuint i = 0;
;         ^^^^^^^^^^^^
;         if (length >= vc * 4)
;         ^^^^^^^^^^^^^^^^^^^^^
;             nuint lastBlock = length - vc * 4;
;             ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^
;                 acc0 += Vector512.LoadUnsafe(ref p, i);
;                 ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^
;                 acc1 += Vector512.LoadUnsafe(ref p, i + vc);
;                 ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^
;                 acc2 += Vector512.LoadUnsafe(ref p, i + vc * 2);
;                 ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^
;                 acc3 += Vector512.LoadUnsafe(ref p, i + vc * 3);
;                 ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^
;             for (; i <= lastBlock; i += vc * 4)
;                                    ^^^^^^^^^^^
;         for (nuint lastVector = length - vc; i <= lastVector; i += vc)
;              ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^
;             acc0 += Vector512.LoadUnsafe(ref p, i);
;             ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^
;         if (i < length)
;         ^^^^^^^^^^^^^^^
;             nuint offset = length - vc;
;             ^^^^^^^^^^^^^^^^^^^^^^^^^^^
;             var lane = Vector512<int>.Indices + Vector512.Create((int)offset);
;             ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^
;             acc1 += mask & Vector512.LoadUnsafe(ref p, offset);
;             ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^
;         return Vector512.Sum((acc0 + acc1) + (acc2 + acc3));
;         ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^
       mov       rax,[rcx+8]
       mov       rcx,rax
       cmp       [rcx],cl
       add       rcx,10
       mov       edx,[rax+8]
       cmp       rdx,10
       jb        near ptr M00_L05
       vxorps    ymm0,ymm0,ymm0
       vxorps    ymm1,ymm1,ymm1
       vxorps    ymm2,ymm2,ymm2
       vxorps    ymm3,ymm3,ymm3
       xor       eax,eax
       cmp       rdx,40
       jb        short M00_L01
       lea       r8,[rdx-40]
M00_L00:
       vpaddd    zmm0,zmm0,[rcx+rax*4]
       vpaddd    zmm1,zmm1,[rcx+rax*4+40]
       vpaddd    zmm2,zmm2,[rcx+rax*4+80]
       vpaddd    zmm3,zmm3,[rcx+rax*4+0C0]
       add       rax,40
       cmp       rax,r8
       jbe       short M00_L00
M00_L01:
       mov       r8,rdx
       sub       r8,10
       mov       r10,r8
       cmp       rax,r10
       ja        short M00_L03
M00_L02:
       vpaddd    zmm0,zmm0,[rcx+rax*4]
       add       rax,10
       cmp       rax,r10
       jbe       short M00_L02
M00_L03:
       cmp       rax,rdx
       jae       short M00_L04
       vpbroadcastd zmm4,r8d
       vpaddd    zmm4,zmm4,[7FFB6613A480]
       vpbroadcastd zmm5,eax
       vpcmpnltd k1,zmm4,zmm5
       vpmovm2d  zmm4,k1
       vpandd    zmm4,zmm4,[rcx+r8*4]
       vpaddd    zmm1,zmm4,zmm1
M00_L04:
       vpaddd    zmm0,zmm0,zmm1
       vpaddd    zmm1,zmm2,zmm3
       vpaddd    zmm0,zmm1,zmm0
       vmovaps   zmm1,zmm0
       vextracti32x8 ymm0,zmm0,1
       vpaddd    ymm0,ymm0,ymm1
       vmovaps   ymm1,ymm0
       vextracti128 xmm0,ymm0,1
       vpaddd    xmm0,xmm0,xmm1
       vpsrldq   xmm1,xmm0,8
       vpaddd    xmm0,xmm1,xmm0
       vpsrldq   xmm1,xmm0,4
       vpaddd    xmm0,xmm1,xmm0
       vmovd     eax,xmm0
       vzeroupper
       ret
M00_L05:
       xor       eax,eax
       xor       r8d,r8d
       inc       rdx
       jmp       short M00_L07
M00_L06:
       add       eax,[rcx+r8]
       add       r8,4
M00_L07:
       dec       rdx
       jne       short M00_L06
       vzeroupper
       ret
; Total bytes of code 280
```

人間がこれ見て解析進めようというのは骨が折れるし、あらゆるベンチマークで丁寧な解析を実行することは現実的ではないんですが、AIに与える分にはコストゼロで強力な武器となってくれます。マイクロベンチマーク実行時にDisassemblyを含めるのは新時代の常識といえるでしょう。

なお、オチは .NET 11の[TensorPrimitive.Sum](https://learn.microsoft.com/en-us/dotnet/api/system.numerics.tensors.tensorprimitives.sum)最強理論でした。たった一行で圧倒的最速！

```csharp
[Benchmark]
public int TensorPrimitivesSum() => TensorPrimitives.Sum<int>(data);
```

コードは[TensorPrimitives.IAggregationOperator.Vectorized512](https://github.com/dotnet/dotnet/blob/f7b4c5716faaee8fb8a289aed29118cad955c45f/src/runtime/src/libraries/System.Numerics.Tensors/src/System/Numerics/Tensors/netcore/Common/TensorPrimitives.IAggregationOperator.cs#L513-L693)で見ることができますが、AIの書いたVector512Finalに近いです。しかし更に一層ゴリゴリのコードが書かれているということで、ある意味、AIの思考の正しさが証明されていることでもあります。もちろんFableがこの「答えを知っている」可能性も大いにあり得るので、こういった汎用的な問題だけで真の思考力、応用力を測ることは不可能ではありますが。

## MessagePackのWrite性能を改善する

分岐予測の診断の例も入れたいので、MessagePackのWriteの例を出します。Readはより難しくて見応えがあるんですが、Writeのほうがシンプルなので例としてはいいかな、と。

```txt
次のベンチマークとして、MessagePackのエンコーダーのパフォーマンス改善をしたい。`BinaryPrimitives.TryWriteInt32LittleEndian()`のように`TryWriteInt32()`の実装候補を出して性能改善していってください。

public static class MessagePackPrimitives
{
    public static bool TryWriteInt32(Span<byte> destination, int value, out int bytesWritten)
    {        
    }
}

分岐予測に関しても正確な計測をしたいので  HardwareCounter.BranchMispredictions, HardwareCounter.BranchInstructions も診断情報に追加してください。Windowsで、管理者権限で起動しているので取得実行できるはずです。
```

今回、分岐があることを知っているので最初から指示に入れていますが、そうでなければコード見て確認して追加してもいいかもしれません。いずれにせよ相談（こちらからの明確な指示）がないと入れてくれることはない（と思う）ので、この辺も「AI時代の指示力」といったところでしょうか。

なお、HardwareCounterの実行には管理者権限が必要になり、Cursorの場合は子プロセスに権限を引き継いでくれていたのですが、Claude Code のシェルは管理者権限を継承してくれませんでした。一応、Claude(Fable)が「UAC 経由で実行する(ユーザーに UAC 許可を依頼)」という形で解消してくれたので、全自動とはいかない（UAC許可を押す必要あり）のですが、問題なく解決はしてくれました。

さて、ベースラインのコードはこれになります。

```csharp
// Candidate 1 (baseline): straightforward if-else cascade, MessagePackWriter.WriteInt32 style
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static bool TryWriteInt32Cascade(Span<byte> destination, int value, out int bytesWritten)
{
	if (value >= 0)
	{
		if (value <= 127)
		{
			if (destination.Length < 1) goto Fail;
			destination[0] = (byte)value;
			bytesWritten = 1;
			return true;
		}
		if (value <= 255)
		{
			if (destination.Length < 2) goto Fail;
			destination[0] = UInt8Code;
			destination[1] = (byte)value;
			bytesWritten = 2;
			return true;
		}
		if (value <= 65535)
		{
			if (destination.Length < 3) goto Fail;
			destination[0] = UInt16Code;
			BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(1), (ushort)value);
			bytesWritten = 3;
			return true;
		}
		if (destination.Length < 5) goto Fail;
		destination[0] = UInt32Code;
		BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(1), (uint)value);
		bytesWritten = 5;
		return true;
	}
	else
	{
		if (value >= -32)
		{
			if (destination.Length < 1) goto Fail;
			destination[0] = unchecked((byte)value);
			bytesWritten = 1;
			return true;
		}
		if (value >= sbyte.MinValue)
		{
			if (destination.Length < 2) goto Fail;
			destination[0] = Int8Code;
			destination[1] = unchecked((byte)value);
			bytesWritten = 2;
			return true;
		}
		if (value >= short.MinValue)
		{
			if (destination.Length < 3) goto Fail;
			destination[0] = Int16Code;
			BinaryPrimitives.WriteInt16BigEndian(destination.Slice(1), (short)value);
			bytesWritten = 3;
			return true;
		}
		if (destination.Length < 5) goto Fail;
		destination[0] = Int32Code;
		BinaryPrimitives.WriteInt32BigEndian(destination.Slice(1), value);
		bytesWritten = 5;
		return true;
	}
Fail:
	bytesWritten = 0;
	return false;
}
```

やっていることは単純で、if連打しているだけです。MessagePackのintの仕様としてはint(4バイト)を「1バイトの種類コード+値」として、1~5バイトの可変で保存します。-32~127は1バイト(positive fixint, negative fixint)、255までは2バイト(uint8tag + 値)、65535までは……、といったような分岐で書き込みます。

さて、ベンチマークの結果はこうなっています。

| Method          | Distribution |          Mean | BranchMispredictions/Op | BranchInstructions/Op | Code Size |
| --------------- | ------------ | ------------: | ----------------------: | --------------------: | --------: |
| **Cascade**     | **Large**    | **4.1720 ns** |                   **0** |                 **9** | **426 B** |
| CascadeUnsafe   | Large        |     3.9454 ns |                       0 |                     8 |     331 B |
| FixintFirst     | Large        |     5.3299 ns |                       0 |                    11 |     371 B |
| BranchlessTable | Large        |     1.7388 ns |                       0 |                     4 |     459 B |
| Branchless2     | Large        |     1.7022 ns |                       0 |                     3 |     442 B |
| Hybrid          | Large        |     1.8189 ns |                       0 |                     4 |     471 B |
|                 |              |               |                         |                       |           |
| **Cascade**     | **Mixed**    | **8.5328 ns** |                   **1** |                 **8** | **439 B** |
| CascadeUnsafe   | Mixed        |     8.2713 ns |                       1 |                     8 |     336 B |
| FixintFirst     | Mixed        |     9.2350 ns |                       1 |                    10 |     384 B |
| BranchlessTable | Mixed        |     1.7210 ns |                       0 |                     4 |     459 B |
| Branchless2     | Mixed        |     1.6376 ns |                       0 |                     3 |     442 B |
| Hybrid          | Mixed        |     2.5156 ns |                       0 |                     4 |     474 B |
|                 |              |               |                         |                       |           |
| **Cascade**     | **Small**    | **1.9931 ns** |                   **0** |                 **5** | **404 B** |
| CascadeUnsafe   | Small        |     1.3937 ns |                       0 |                     5 |     309 B |
| FixintFirst     | Small        |     0.6945 ns |                       0 |                     4 |     171 B |
| BranchlessTable | Small        |     1.7197 ns |                       0 |                     4 |     459 B |
| Branchless2     | Small        |     1.6388 ns |                       0 |                     3 |     442 B |
| Hybrid          | Small        |     0.7017 ns |                       0 |                     4 |     450 B |

というわけで、上のシンプルなコード(Cascade)よりも良い結果が出せています。

で、まず、ベンチマーク結果をチェックする場合は、ベンチマークコードのチェックが必須です。ベンチマークは、与える値によって結果がかなり変わります。例えばJsonSerializerのパフォーマンステストをするときに、ASCIIだけのものと、日本語交じりのものとでは、性能が全然変わってきたりします(UTF8エンコーディングのfastpathへの入り方が違う)。というわけで、ちゃんと網羅しているかどうかを確認することは重要です。で、人間がベンチマークを書くとパラメータの網羅性が雑になったりすることが多々ある、少なくとも私は網羅どころか単一パラメータだけで済ませていることも多かったのですが、やはりここはAI時代なのでちゃんと網羅しているものを生成してもらいましょう。またはAIの性能が低いと網羅してくれてない場合もありうるので、ここは人間が、「何をテストしようとしているのか」の確認はすべきだと思っています。AI時代はコードなんて見ないとか言ってる場合じゃないんです。

今回はintの値によってifの回数とかも変わってきて、それが性能差になるので、Small, Large, Mixedとしてパターンを作っています。

```csharp
// Small: all fixint (perfectly predictable branch)
// Mixed: format class chosen at random per element (worst case for branch prediction)
// Large: all 5-byte int32/uint32
[Params("Small", "Mixed", "Large")]
public string Distribution = "Mixed";

[GlobalSetup]
public void Setup()
{
    var rand = new Random(42);
    values = new int[Count];
    buffer = new byte[Count * 5];
    for (int i = 0; i < Count; i++)
    {
        values[i] = Distribution switch
        {
            "Small" => rand.Next(-32, 128),
            "Large" => rand.Next(2) == 0 ? rand.Next(65536, int.MaxValue) : rand.Next(int.MinValue, -32768),
            _ => rand.Next(8) switch
            {
                0 => rand.Next(0, 128),
                1 => rand.Next(-32, 0),
                2 => rand.Next(128, 256),
                3 => rand.Next(-128, -32),
                4 => rand.Next(256, 65536),
                5 => rand.Next(-32768, -128),
                6 => rand.Next(65536, int.MaxValue),
                _ => rand.Next(int.MinValue, -32768),
            },
        };
    }
}
```

Small(-32~127の範囲に収まる)も多いだろうなあ、とは思いますが、まぁMixedのケースも全然いっぱいあるだろうということで、そこのバランスをとっていきたいですね。FixintFirstは、まさにそのfixintのレンジのifを最初に入れることで、Smallだけは絶対に速いというfastpathになっています。

```csharp
// Candidate 3: single-compare fast path for fixint [-32, 127] (the dominant case in typical
// msgpack payloads), everything else in a non-inlined slow path
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static bool TryWriteInt32FixintFirst(Span<byte> destination, int value, out int bytesWritten)
{
    if ((uint)(value + 32) <= 159 && destination.Length >= 1)
    {
        MemoryMarshal.GetReference(destination) = unchecked((byte)value);
        bytesWritten = 1;
        return true;
    }
    return TryWriteInt32MultiByte(destination, value, out bytesWritten);
}
```

ちなみに↑の-32~127のレンジという処理が `if ((uint)(value + 32) <= 159 && destination.Length >= 1)` というお洒落なif一発におさめているのは、地味に特筆すべき点だと思っています。こういうのシレッと含ませてくれると感心しちゃいますよね。

さて、とりあえず重点を置きたいのはMixedの数値なので、それをRound別にチェックしてみます。

| Round | 実装                 |        Mean | asm / カウンタから見つけた欠陥 → 次の一手                                                                                         |
| ----- | ------------------ | ----------: | ----------------------------------------------------------------------------------------------------------------- |
| 1     | Cascade (baseline) |     8.53 ns | if-else 連鎖。分岐 8/op・ミス 1/op — ミスペナルティ(≈13サイクル)が支配 → 分岐自体を消すしかない                                                    |
| 1     | CascadeUnsafe      |     8.27 ns | Slice・個別 store を ref + unaligned store に統合(コード 426→331B)。しかし分岐構造が同じでミスも 1/op のまま → 命令数削減では効かないと確定                 |
| 1     | FixintFirst        |     9.24 ns | fixint を1比較で先行(Small なら 0.69 ns)。Mixed ではその先行分岐自体がミス源 → 分布依存が極端                                                   |
| 1     | BranchlessTable    |     1.72 ns | lzcnt+66エントリ表で分岐レス達成・ミス 0。ただし asm に表の境界チェック(`cmp r11d,42; jae RNGCHKFAIL`)と `sign*33` の乗算相当が残存 → 64エントリ表+Unsafe 化 |
| 計測修正  | (全実装)              |           — | Count=1000 では Mixed でも Mispredictions/Op=0 — Zen 5 予測器が固定配列の分岐パターンを丸ごと記憶していた → Count=100,000 に増量                  |
| 2     | **Branchless2**    | **1.64 ns** | 境界チェック消滅、index 計算が `shr+and+or` に。ホットパス分岐 3/op(全て予測可能)。残るは lzcnt→表ロード→movbe の直列依存チェーンで可変長エンコーダに固有 → **収束**        |
| 2     | Hybrid             |     2.52 ns | Branchless2 の前に fixint 1比較(Small 0.70 ns)。Mixed では先行分岐が ~0.4 ミス/op → 分布が小整数支配と分かっているときだけ選ぶ                        |

まず感心するところは、ベンチマークのパラメータとして再そっはCount = 1000だったのですが、`Mispredictions/Op=0` という計測結果が出ていたので 「Zen 5 予測器が固定配列の分岐パターンを丸ごと記憶していた → Count=100,000 に増量」という対処をしたことです。

```csharp
// large enough that the branch predictor cannot memorize the repeating sequence
// (with 1000 values, Zen 5 learned the whole pattern: BranchMispredictions/Op was 0 even for Mixed)
const int Count = 100_000;
    
[Benchmark(OperationsPerInvoke = Count)]
public int Cascade()
```

この辺も `Mispredictions/Op` の計測を入れていたからこそ素早く自動で認知して対応できたわけで、そうじゃなければ進行不能になる可能性がありました。

さて、コードのチェックに入りたいのですが、if分岐の最速化対応は手コードだとあんまやれることがないんですが、AIはブランチレスコードを2パターン作ってくれました。最速案のBranchless2を見てみます。

```csharp
// Round 2: significant bits of a non-negative int are 0..31, never 32, so a 64-entry table
// indexed by ((value >>> 26) & 32) | bits works — single OR instead of imul, and the
// power-of-two size lets us drop the bounds check via Unsafe
static ReadOnlySpan<uint> Formats64 =>
[
    // non-negative: bits 0..7 fixint, 8 uint8, 9..16 uint16, 17..31 uint32
    Fix1, Fix1, Fix1, Fix1, Fix1, Fix1, Fix1, Fix1,
    U8,
    U16, U16, U16, U16, U16, U16, U16, U16,
    U32, U32, U32, U32, U32, U32, U32, U32, U32, U32, U32, U32, U32, U32, U32,
    // negative (bits of ~value): 0..5 fixint, 6..7 int8, 8..15 int16, 16..31 int32
    Fix1, Fix1, Fix1, Fix1, Fix1, Fix1,
    I8, I8,
    I16, I16, I16, I16, I16, I16, I16, I16,
    I32, I32, I32, I32, I32, I32, I32, I32, I32, I32, I32, I32, I32, I32, I32, I32,
];

[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static bool TryWriteInt32Branchless2(Span<byte> destination, int value, out int bytesWritten)
{
    if (destination.Length >= 5)
    {
        int x = value ^ (value >> 31);
        int bits = 32 - BitOperations.LeadingZeroCount((uint)x); // 0..31
        int idx = ((value >>> 26) & 32) | bits;
        uint e = Unsafe.Add(ref MemoryMarshal.GetReference(Formats64), idx);
        int len = (int)(e & 0xff);
        ref byte d = ref MemoryMarshal.GetReference(destination);
        d = (byte)((e >> 8) | ((uint)value & (e >> 16)));
        Unsafe.WriteUnaligned(ref Unsafe.Add(ref d, 1), BinaryPrimitives.ReverseEndianness((uint)value << ((5 - len) * 8)));
        bytesWritten = len;
        return true;
    }
    return TryWriteInt32Cascade(destination, value, out bytesWritten);
}
```

こういうの妥当かどうかって全然判断つかないですよね正直。GeminiとかChatGPTにも投げて、ヨシといってるんならもうヨシということにしようか、みたいな感じですらあります（妥当でしたのでヨシ）

と、いうわけでmixedのシチュエーションで 8.53 nsが 1.64 ns に、超高速化されたということになります。これをそのまま採用するかどうかはともかくとして、改めて興味深い結果が得られました。持ち帰って検討させていただきます（？）

## AI時代のコーディング

SumにせよMessagePackのTryWriteIntにせよ、人間を遥かに超えてるのは間違いない。Sumのforループ高速化はカンニング疑惑が拭えなかったわけですが、ブランチレスのMessagePack Writeのコードは世の中にはないと思うので、Fableがちゃんと自力で導出したコードだといえます。この結果はOpusでは得られてなかったので、個人的にはFableが閾値超えてきたな、と思いました。今までは解析は勿論AIの強みだけど書かせるとイマイチだなー、と思ってたんですが、Fableは分析力が更に増したせいなのか、書き力も高くなっているぞ、と。少なくともこの手の関数単位みたいな超スモール領域ではコーディング力に圧倒されてしまう。人間のやることは雰囲気を整えるのと、トレードオフに対してどちらのハンコを押すか決めるぐらいしかない。

ただし、じゃあアプリケーション全体、ライブラリ全体をまるっと作らせて満足いくコードが仕上がってくるかというと、そこはまだまだNoではある。単純にコンテキストの問題もあるし、複雑性が桁違いでもあるし、ただ、AIの性能がめちゃくちゃ上がってそこを克服したとしても、ライブラリ全体の作りが満足いくかどうかは、もう感性の世界でもあるので、気に入るか気に入らないかの領域であり、受け入れるか受け入れないか、といったところではある。AIがどれだけ進化しても自分の100%の好みなものをポン出しで生成することは絶対ないわけなので、ひたすら自分好みにしようとする不毛な応酬をするぐらいなら、心折れて、AIの出した、まぁ動くコードを諦めて受け入れるしかない、にはなりそう。

ようするところオーナーシップが持てなくなってきているんですね、コードから。短期的には動けばいい、んですが中長期的にはモチベーションに大きく影響するので、単純な受け入れは結構危険だと思っています。それでも自分で吟味して生成したコードはいいんです、`TryWriteInt32Branchless2` なんかは自分が生成したコードなら受け入れるでしょう。でも、これがPRですごいパフォーマンス改善のコードなんです！と来た時にはどう対応できるかな、と。多分、拒絶しますね。受け入れたら受け入れたで、もうそのあたりは見たくない、になります。

会社のプロダクトだったら、アプリケーションだったら、コードへの愛着に関しては捨てて、プロダクトに向き合うことでオーナーシップを担保していかなければならない。なんだったら、そのほうが理想的でしょ？といえるかもしれません（個人的には、色々な職種や見てる領域によって大きなプロダクトは成り立つので、エンジニアがコードへの関心が占める割合が高いのは、悪いことではないと思っていますが）。実際私も個人アプリとか、こないだはAndroidアプリを作って公開しましたが、そういうのはコードはどうでもいいしほとんど見てなくて、アプリケーションの出来、ルック＆フィールが全てでした。

けれど、OSSライブラリは100%コードの世界なので、ここを捨ててしまうと、なにもないんですね、なにも残りません。実際、ちょっとやや虚無感を抱き始めているところが無きにしも非ずで、コミュニケーション取っていい機能できたんですよーっていうんで見てみたらAIプルリクですねこれ、なんていうのがあると、どうしても見る気が起きなくなるし、セキュリティチェックしましたー、は大事だしいいんですがセットのAI修正コードの山です、というのはかなり厳しいものがあった。モチベーションがぽきぽき折れてきます。

というわけで、とても複雑な気持ちを抱えています。

それはそれとして、何が作りたいか、というと、全てが研ぎ澄まされた究極のライブラリが作りたいという気持ちがあり、Fableが超強力な武器なのは間違いないわけです。というか、なんだったら今まで公開している今のすべてのコードをむしろ恥じます。全部作り直したい、なんだったら今までのコードは恥なので見たくない（？）とすら思いかねないところもあります。

## まとめ

この記事はAI補助ゼロで100%オーガニックな手書きで作られています。文章に関しては本当にAIベースのものが好きじゃないので、それこそClaude Codeにベンチマーク取らせたわけだから、いい感じにブログになるように整形してよ、といえば、まぁできなくはない、できなくはないんですが、それは血が通ってない面白みも少ない（ないとは言いませんが）記事だなあ、と思ってしまうわけです。ノイズだらけ、雑味だらけのこの文章が一周回ってよく思えてきませんか……！？塩化ナトリウム99%みたいな文章を摂取してる場合じゃあないんです、時代は天然塩。

で、まとめとしては、改めて自分のバイブルである美味しんぼに立ち返って、究極 vs 至高として、究極のライブラリをまずは一品出すところからなんじゃあないでしょうか。の前に、AI時代到来への心の整理がようやくついてきたので（？）各種OSSのこれからについて一つ一つ決めていきたいな、と思います。しんみりしちゃった。いや、どちらかというとワクワクしてるんですよ……！？