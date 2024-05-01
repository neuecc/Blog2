# R3のコードから見るC#パフォーマンス最適化技法実例とTimeProviderについて

4/27に大阪で開催された[C#パフォーマンス勉強会](https://cs-reading.connpass.com/event/309714/)で「R3のコードから見る実践LINQ実装最適化・コンカレントプログラミング実例」という題でセッションしてきました！

<iframe class="speakerdeck-iframe" frameborder="0" src="https://speakerdeck.com/player/205627770b434599925567dbfeca229c" title="R3のコードから見る実践LINQ実装最適化・コンカレントプログラミング実例" allowfullscreen="true" style="border: 0px; background: padding-box padding-box rgba(0, 0, 0, 0.1); margin: 0px; padding: 0px; border-radius: 6px; box-shadow: rgba(0, 0, 0, 0.2) 0px 5px 40px; width: 100%; height: auto; aspect-ratio: 560 / 315;" data-ratio="1.7777777777777777"></iframe>

タイトル的にあまりLINQでもコンカレントでもなかったかな、とは思いますが、[R3](https://github.com/Cysharp/R3)を題材に、具体的なコードをもとにした最適化技法の紹介という点では面白みはあったのではないかと思います。

Rxの定義
---
R3は、やや挑発的な内容を掲げていることもあり、R3は「Rxではない」みたいなことを言われることもあります。なるほど！では、そもそも何をもってRxと呼ぶのか、呼べるのか。私は「Push型でLINQ風のオペレーターが適用できればRx」というぐらいの温度感で考えています。もちろん、R3はそれを満たしています。

`mutable struct`の扱いと同じく、あまり教条主義的にならず、時代に合わせて、柔軟により良いシステムを考えていきましょう。コンピュータープログラミングにおいて、伝統や歴史を守ることは別に大して重要なことではないはずです。

TimeProvider DeepDive
---
[TimeProvider](https://learn.microsoft.com/ja-jp/dotnet/api/system.timeprovider?view=net-8.0)について、セッションでも話しましたが、大事なことなのでもう少し詳しくいきましょう。TimeProviderにまず期待するところとしては、ほとんどが`SystemClock.Now`、つまりオレオレ`DateTime.Now`生成器の代わりを求めているでしょう。それを期待しているとTimeProviderの定義は無駄に複雑に見えます。しかし`TimeProvider`を分解してみると、これは4つの時間を司るクラスの抽象層になっています。

```csharp
public abstract class TimeProvider
{
    // TimeZoneInfo
    public virtual TimeZoneInfo LocalTimeZone => TimeZoneInfo.Local;

    // DateTimeOffset
    public virtual DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow;
    public DateTimeOffset GetLocalNow() =>

    // Stopwatch
    public virtual long TimestampFrequency => Stopwatch.Frequency;
    public virtual long GetTimestamp() => Stopwatch.GetTimestamp();
    public TimeSpan GetElapsedTime(long startingTimestamp, long endingTimestamp) => 
    public TimeSpan GetElapsedTime(long startingTimestamp) => GetElapsedTime(startingTimestamp, GetTimestamp());

    // System.Threading.Timer
    public virtual ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
}

public interface ITimer : IDisposable, IAsyncDisposable
{
    bool Change(TimeSpan dueTime, TimeSpan period);
}
```

4つの時間を司るクラス、すなわち[TimeZoneInfo](https://learn.microsoft.com/ja-jp/dotnet/api/system.timezoneinfo?view=net-8.0)、[DateTimeOffset](https://learn.microsoft.com/ja-jp/dotnet/api/system.datetimeoffset?view=net-8.0)、[Stopwatch](https://learn.microsoft.com/ja-jp/dotnet/api/system.diagnostics.stopwatch?view=net-8.0)、[System.Threading.Timer](https://learn.microsoft.com/ja-jp/dotnet/api/system.threading.timer?view=net-8.0)。

この造りになっているからこそ、あらゆる時間にまつわる挙動を任意に変更することができるのです。

挙動を任意に変更するというとユニットテストでの時間のモックにばかり意識が向きますが（実際、[FakeTimeProvider](https://learn.microsoft.com/ja-jp/dotnet/api/microsoft.extensions.time.testing.faketimeprovider?view=net-8.0)はとても有益です）、別にユニットテストに限らず、優れた時間の抽象化層として使うことができます。ということを実装とともに証明したのがR3で、特にR3では`CreateTimer`をかなり弄っていて、WPFではDispatcherTimerを使うことで自動的にUIスレッドにディスパッチしたり、UnityではPlayerLoopベースのタイマーとしてScaledとUnsacledでTimescaleの影響を受けるタイマー・受けないタイマーなどといった実行時のカスタマイズ性を実現しました。

セッションではStopwatchについてフォーカスしました。二点の時刻の経過時間を求めるのにDateTimeの引き算、つまり

```csharp
DateTime now = DateTime.UtcNow;
/* do something... */
TimeSpan elapesed = DateTime.UtcNow - now;
```

といったコードを書くのはよくあることですが、これはバッドプラクティスです。DateTimeの取得はタダではありません。では、なるほどStopwatchですね？ということで

```csharp
Stopwatch sw = Stopwatch.StartNew();
/* do something... */
TimeSpan elapsed = sw.Elapsed;
```

これは、Stopwatchがclassなのでアロケーションがあります。うまく使いまわしてあげる必要があります。
使いまわしができないシチュエーションのために、アロケーションを避けるためにstructのStopwatch、ValueStopwatchといったカスタム型を作ることもありますが、待ってください、そもそもStopwatchが不要です。

二点の経過時間を求めるなら、時計による時刻も不要で、その地点の何らかのタイムスタンプが取れればそれで十分なのです。

```csharp
// .NET 7以降での手法(GetElapsedTimeが追加された)
long timestamp = Stopwatch.GetTimestamp();
/* do something... */
TimeSpan elapsed = Stopwatch.GetElapsedTime(timestamp);
```

このlongは、通常は高解像度タイムスタンプ、Windowsでは[QueryPerformanceCounter(https://learn.microsoft.com/ja-jp/windows/win32/sysinfo/acquiring-high-resolution-time-stamps)が使われています。TimeSpanでよく使うTicksではないことに注意してください。

ベンチマークを取ってみましょう。

```csharp
using BenchmarkDotNet.Attributes;
using System.Diagnostics;

BenchmarkDotNet.Running.BenchmarkRunner.Run<TimestampBenchmark>();

public class TimestampBenchmark
{
    [Benchmark]
    public long Stopwatch_GetTimestamp()
    {
        return Stopwatch.GetTimestamp();
    }

    [Benchmark]
    public DateTime DateTime_UtcNow()
    {
        return DateTime.UtcNow;
    }

    [Benchmark]
    public DateTime DateTime_Now()
    {
        return DateTime.Now;
    }
}
```

![image](https://github.com/Cysharp/R3/assets/46207/75122cc7-c303-493f-bc78-40388529cf60)

NowではUtcNowに加えてTimeZoneからのオフセット算出が入るために更にもう一段遅くなります。

なお、マイクロベンチマークを取るときは必ず[BenchmarkDotNet](https://github.com/dotnet/BenchmarkDotNet)を使ってください。(micro)benchmark is hard、です。Stopwatchで測られても、あらゆる要因から誤差が出まくるし、そもそも指標もよくわからないしで、数字を見ても何もわかりません。私はそういう数字の記事とかを見た場合、役に立たないと判断して無視します。

まとめ
---
セッション資料に盛り込めた最適化技法の紹介は極一部ではありますが、R3がどれだけ気合い入れて作られているかが伝わりましたでしょうか？10年の時を経て、私自身の成長とC#の成長が合わさり、UniRxからクオリティが桁違いです。

これからも足を止めずにやっていきますし、みなさんも是非モダンC#やっていきましょう……！（Unityも十分モダンC#の仲間入りで良いです！）

そういえばブログに貼り付けるのを忘れてたのですが3月末にはこんなセッションもしていました。

<iframe class="speakerdeck-iframe" frameborder="0" src="https://speakerdeck.com/player/c5a8898ac7c4464584068b0ee3180e94" title=".NETの非同期戦略とUnityとの相互運用" allowfullscreen="true" style="border: 0px; background: padding-box padding-box rgba(0, 0, 0, 0.1); margin: 0px; padding: 0px; border-radius: 6px; box-shadow: rgba(0, 0, 0, 0.2) 0px 5px 40px; width: 100%; height: auto; aspect-ratio: 560 / 315;" data-ratio="1.7777777777777777"></iframe>

ええ、ええ。Unityもモダンですよ！大丈夫！