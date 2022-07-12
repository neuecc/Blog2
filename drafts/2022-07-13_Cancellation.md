# async/awaitのキャンセル処理やタイムアウトを効率的に扱うためのパターン＆プラクティス

async/awaitの鬼門の一つとして、適切なキャンセル処理が挙げられます。しかし別に基本的にはそんな難しいことではなく、CancellationTokneSourceを作る、CanellationTokenを渡す、OperationCanceledExceptionをハンドリングする。というだけの話です。けれど、Tokenに手動でコールバックをRegisterしたときとか、渡す口が空いてないものに無理やりなんとかするときとか、タイムアウトに使った場合の始末とか、ちょっと気の利いた処理をしたいような場面もあり、そうした時にどうすれば良いのか悩むこともあります。

こういうのはパターンと対応さえ覚えてしまえばいい話でもあるので、今回は[AlterNats](https://github.com/Cysharp/AlterNats)の実装時に直面したパターンから、「外部キャンセル・タイムアウト・大元のDispose」が複合された状況での処理の記述方法と、適切な例外処理、そして最後にObjectPoolなども交えた効率的なゼロアロケーションでのCancellationTokenSourceのハンドリング手法を紹介します。

CreateLinkedTokenSourceを使ったパターン
---
何かのClientを実装してみる、ということにしましょう。最も単純なパターンは引数の末尾にCancellationTokenを用意して、内部のメソッドにひたすら伝搬させていくことです。きちんと伝搬させていけば、最奥の処理が適切にCancellationTokenをハンドリングしてキャンセル検知時にOperationCanceledExceptionを投げてくれます。CancellationTokenをデフォルト引数にするか、必ず渡す必要があるよう強制するかは、アプリケーションの性質次第です。アプリケーションに近いコードでは強制させるようにしておくと、渡し忘れを避けれるので良いでしょう。

```csharp
class Client
{
    public async Task SendAsync(CancellationToken cancellationToken = default)
    {
        await SendCoreAsync(cancellationToken);
    }

    async Task SendCoreAsync(CancellationToken cancellationToken)
    {
        // nanika...
    }
}
```

非同期メソッドのキャンセルはCancellationTokenで処理するのが基本で、別途Cancelメソッドを用意する、といったことはやめておきましょう。実装が余計に複雑化するだけです。CancellationTokenを伝搬させるのが基本であり全てです。

任意のキャンセルの他に、タイムアウト処理を入れたい、というのは特に通信系ではよくあります。async/awaitでのタイムアウトの基本は、タイムアウトもキャンセル処理の一つである、ということです。CancellationTokenSourceにはCancelAfterという一定時間後にCancelを発火させるというメソッドが用意されているので、これを使ってCancellationTokenを渡せば、すなわちタイムアウトになります。

```csharp
// Disposeすると内部タイマーがストップされるのでリークしない
using var cts = new CancellationTokenSource();
cts.CancelAfter(TimeSpan.FromMinutes(1));

await client.SendAsync(cts.Token);
```

> [UniTask](https://github.com/Cysharp/UniTask)ではCancelAfterSlimというメソッドが用意されているため、そちらを使うことをお薦めします。Cancelはスレッドプールを使いますが、CancelAfterSlimはPlayerLoop上で動くため、Unityフレンドリーな実装になっています。ただし内部タイマーのストップ手法がCancelAfterSlimの戻り値をDisposeする必要があるというように、実装に若干差異があります。

タイムアウト時間は大抵固定のため、ユーザーに都度CancelAfterを叩かせるというのは、だいぶ使いにくい設計です。そこで、CancelAfterの実行はSendAsyncメソッドの内部で行うことにしましょう。そうした内部のタイムアウト用CancellationTokenと、外部からくるCancellationTokenを合成して一つのCancellationTokenに変換するには、[CancellationTokenSource.CreateLinkedTokenSource](https://docs.microsoft.com/en-us/dotnet/api/system.threading.cancellationtokensource.createlinkedtokensource)が使えます。

```csharp
class Client
{
    public TimeSpan Timeout { get; }

    public Client(TimeSpan timeout)
    {
        this.Timeout = timeout;
    }

    public async Task SendAsync(CancellationToken cancellationToken = default)
    {
        // 連結された新しいCancellationTokenSourceを作る
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(Timeout);

        await SendCoreAsync(cancellationToken);
    }

    // snip...
}
```

これで完成！なのですが、このままだと例外処理に問題があります。

OperationCanceledExceptionは `CancellationToken`というプロパティを持っていて、これを元に呼び出し側はキャンセルの原因を判別することができます。一つ例を出しますが、以下のようにOperationCanceledExceptionをcatchしたうえで、更に判定を入れてコード分岐をかけることがあります。

```csharp
try
{
    await client.SendAsync(token);
}
catch (OperationCanceledException ex) when (ex.CancellationToken == token)
{
    // Cancelの原因をTokenによって判定できる
}
```

例外を何も処理せずに全部おまかせでやると、投げられる OperationCanceledException.CancellationToken は CreateLinkedTokenSource で連結したTokenになってしまい、何の意味もない情報ですし、原因の判別に使うこともできません。

また、タイムアウトをOperationCanceledExceptionとして扱ってしまうことも問題です。OperationCanceledExceptionは特殊な例外で、既知の例外であるとしてロギングから抜いたりすることもままあります（例えばウェブサーバーでクライアントの強制切断(リクエスト中にブラウザ閉じたりとか)でキャンセルされることはよくあるけれど、それをいちいちエラーで記録していたらエラー祭りになってしまう）。タイムアウトは明らかな異常であり、そうしたキャンセルとは確実に区別して欲しいし、OperationCanceledExceptionではない例外になって欲しい。

これは .NET のHttpClientでも [HttpClient throws TaskCanceledException on timeout #21965](https://github.com/dotnet/runtime/issues/21965) としてIssueがあがり(TaskCanceledExceptionはOperationCanceledExceptionとほぼ同義です)、大激論(121コメントもある！)を巻き起こしました。HttpClientはタイムアウトだろうが手動キャンセルだろうが区別なくTaskCanceledExceptionを投げるのですが、原因は、実装が上の例の通りCreateLinkedTokenSourceで繋げたもので処理していて、そして、特に何のハンドリングもしていなかったからです。

結論としてこれはHttpClientの設計ミスなのですが、一度世の中に出したクラスの例外の型を変更することは .NET の互換性維持のポリシーに反するということで（実際、これを変更してしまうと影響は相当大きくなるでしょう）、お茶を濁した対応(InnerExceptionにTimeoutExceptionを仕込んで、判定はそちら経由で一応できなくもないようにした)となってしまったのですが、今から実装する我々は同じ轍を踏んではいけない。ということで、ちゃんと正しく処理するようにしましょう。

```csharp
public async Task SendAsync(CancellationToken cancellationToken = default)
{
    using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    cts.CancelAfter(Timeout);

    try
    {
        await SendCoreAsync(cancellationToken);
    }
    catch (OperationCanceledException ex) when (ex.CancellationToken == cts.Token)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            // 引数のCancellationTokenが原因なので、それを保持したOperationCanceledExceptionとして投げる
            throw new OperationCanceledException(ex.Message, ex, cancellationToken);
        }
        else
        {
            // タイムアウトが原因なので、TimeoutException(或いは独自の例外)として投げる
            throw new TimeoutException($"The request was canceled due to the configured Timeout of {Timeout.TotalSeconds} seconds elapsing.", ex);
        }
    }
}
```

やることは別に難しくはなく、OperationCanceledExceptionをcatchしたうえで、外から渡されたcancellationTokenがキャンセルされているならそれが原因、そうでないならタイムアウトが原因であるという判定をして、それに応じた例外を投げ直します。

最後に、Client自体がDisposeできるとして、それに反応するようなコードにしましょう。

```csharp
class Client : IDisposable
{
    // IDisposableと引っ掛けて、Client自体がDisposeされたら実行中のリクエストも終了させるようにする
    readonly CancellationTokenSource clientLifetimeTokenSource;

    public TimeSpan Timeout { get; }

    public Client(TimeSpan timeout)
    {
        this.Timeout = timeout;
        this.clientLifetimeTokenSource = new CancellationTokenSource();
    }

    public async Task SendAsync(CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(clientLifetimeTokenSource.Token, cancellationToken);
        cts.CancelAfter(Timeout);

        try
        {
            await SendCoreAsync(cancellationToken);
        }
        catch (OperationCanceledException ex) when (ex.CancellationToken == cts.Token)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                // 引数のCancellationTokenが原因なので、それを保持したOperationCanceledExceptionとして投げる
                throw new OperationCanceledException(ex.Message, ex, cancellationToken);
            }
            else if (clientLifetimeTokenSource.IsCancellationRequested)
            {
                // クライアント自体がDisposeされたのでOperationCanceledException、或いは独自の例外を投げる
                throw new OperationCanceledException("Client is disposed.", ex, clientLifetimeTokenSource.Token);
            }
            else
            {
                // タイムアウトが原因なので、TimeoutException(或いは独自の例外)として投げる
                throw new TimeoutException($"The request was canceled due to the configured Timeout of {Timeout.TotalSeconds} seconds elapsing.", ex);
            }
        }
    }

    async Task SendCoreAsync(CancellationToken cancellationToken)
    {
        // nanika...
    }

    public void Dispose()
    {
        clientLifetimeTokenSource.Cancel();
        clientLifetimeTokenSource.Dispose();
    }
}
```

差分はCreateLinkedTokenSourceで連結するトークンを増やすのと、例外処理時の分岐を増やすことだけです。

ゼロアロケーション化する
---
殆どの場合は上記のパターンで全く問題ないのですが、都度CreateLinkedTokenSourceで新しいCancellationTokenSourceを作るのが気になる、かもしれません。どちらにせよasyncメソッドが非同期で実行される場合には、非同期ステートマシン自体のアロケーションが発生するので実際のところ別に気にするほどのことではない。のですが、[IValueTaskSource](https://docs.microsoft.com/en-us/dotnet/api/system.threading.tasks.sources.ivaluetasksource)や[PoolingAsyncValueTaskMethodBuilder](https://docs.microsoft.com/en-us/dotnet/api/system.runtime.compilerservices.poolingasyncvaluetaskmethodbuilder-1)を使ったアロケーションを避ける非同期実装を行っていた場合には、相当気になる問題になってきます。また、HTTP/1のREST呼び出しのような頻度では大したことないですが、これが例えばサーバーで大量の並列実行をさばく、クライアントではリアルタイム通信で毎フレーム通信する、といった用途だと、この辺も気を配りたくなってくるかもしれません。

なお、ここでは説明の簡略化のために、SendAsyncメソッド自体はasync Taskのままにします。

まずは外部キャンセルのない、タイムアウトだけのケースを見ていきます。タイムアウトは正常系の場合は発火しない、つまり殆どの場合は発火しないため、非発火時にはCancellationTokenSourceを使い回すようにしましょう。

```csharp
class Client
{
    // SqlConnectionのようなメソッドを多重に呼ぶことを禁止しているクラスの場合はフィールドにCancellationTokenSourceを一つ
    // HttpClientのようにあちこちから多重に呼ばれる場合があるものはObjectPoolで保持する

    readonly ObjectPool<CancellationTokenSource> timeoutTokenSourcePool;

    public TimeSpan Timeout { get; }

    public Client(TimeSpan timeout)
    {
        this.Timeout = timeout;
        this.timeoutTokenSourcePool = ObjectPool.Create<CancellationTokenSource>();
    }

    public async Task SendAsync()
    {
        var timeoutTokenSource = timeoutTokenSourcePool.Get();
        timeoutTokenSource.CancelAfter(Timeout);

        try
        {
            await SendCoreAsync(timeoutTokenSource.Token);
        }
        finally
        {
            // Timeout処理が発火していない場合はリセットして再利用できる
            if (timeoutTokenSource.TryReset())
            {
                timeoutTokenSourcePool.Return(timeoutTokenSource);
            }
        }
    }
}
```

ObjectPoolの実装は色々ありますが、今回は説明の簡略化のために[Microsoft.Extensions.ObjectPool](https://docs.microsoft.com/en-us/dotnet/api/microsoft.extensions.objectpool)を使いました(NuGetからMicrosoft.Extensions.ObjectPoolを参照する必要あり)。タイムアウトが発動した場合は再利用不能なので、プールに戻してはいけません。なお、 [CancellationTokenSource.TryReset](https://docs.microsoft.com/en-us/dotnet/api/system.threading.cancellationtokensource.tryreset)は .NET 6 からのメソッドになります。それ以前の場合は `CancelAfter(Timeout.InfiniteTimeSpan)` を呼んでタイマー時間を無限大に引き伸ばす変更を入れる（内部的にはTimerがChangeされる）というハックがあります。

外部キャンセルが入る場合には、LinkedTokenを作らず、[CancellationToken.UnsafeRegister](https://docs.microsoft.com/en-us/dotnet/api/system.threading.cancellationtoken.unsaferegister)でタイマー用のCancellationTokenSourceをキャンセルするようにします。

```csharp
public async Task SendAsync(CancellationToken cancellationToken = default)
{
    var timeoutTokenSource = timeoutTokenSourcePool.Get();

    CancellationTokenRegistration externalCancellation = default;
    if (cancellationToken.CanBeCanceled)
    {
        // 引数のCancellationTokenが発動した場合もTimeout用のCancellationTokenを発火させる
        externalCancellation = cancellationToken.UnsafeRegister(static state =>
        {
            ((CancellationTokenSource)state!).Cancel();
        }, timeoutTokenSource);
    }

    timeoutTokenSource.CancelAfter(Timeout);

    try
    {
        await SendCoreAsync(timeoutTokenSource.Token);
    }
    finally
    {
        // Registerの解除(TryResetの前に「必ず」先に解除すること)
        // CancellationTokenRegistration.Disposeは解除完了（コールバック実行中の場合は実行終了）までブロックして確実に待ちます
        externalCancellation.Dispose();
        if (timeoutTokenSource.TryReset())
        {
            timeoutTokenSourcePool.Return(timeoutTokenSource);
        }
    }
}
```

CancellationToken.UnsafeRegisterは .NET 6 からのメソッドでExecutionContextをCaptureしないため、より高効率です。それ以前の場合はRegisterを使うか、呼び出しの前後でExecutionContext.SuppressFlow/RestoreFlowするというハックが使えます(UniTaskのRegisterWithoutCaptureExecutionContextはこの実装を採用しています)。

CancellationTokenにコールバックを仕込む場合、レースコンディションが発生する可能性が出てきます。この場合だとTimeout用のCancellationTokenSourceをプールに戻した後にCancelが発生すると、最悪なことになります。それを防ぐために、CancellationTokenRegistration.DisposeをTryResetの前に必ず呼びましょう。CancellationTokenRegistration.Disposeの優れているところは、コールバックが実行中の場合は実行終了までブロックして確実に待ってくれます。これによりマルチスレッドのタイミング問題ですり抜けてしまうといったことを防いでくれます。

ブロックといいますが、コールバックに登録されたメソッドがすぐに完了する性質のものならば、lockみたいなものなので神経質になる必要はないでしょう。[CancellationTokenRegistration](https://docs.microsoft.com/en-us/dotnet/api/system.threading.cancellationtokenregistration)にはDisposeAsyncも用意されていますが、むしろそちらを呼ぶほうがオーバーヘッドであるため、無理にDisposeAsyncのほうを優先する必要はないと考えています。CancellationTokenRegistrationには他にUnregisterメソッドもあり、これはfire-and-forget的に解除処理したい場合に有効です。使い分けですね。

なお、CancellationTokenへのコールバックのRegister(UnsafeRegister)は、初回はコールバック登録用のスロットを生成するといったアロケーションがありますが、Dispose/Registerを繰り返す二回目以降はスロットを再利用してくれます。このへんも新規に(Linked)CancellationTokenSourceを作るより有利な点となりますね。

引き続き、Client自体の寿命に引っ掛けるCancellationTokenを追加した実装を見ていきましょう。といっても、単純にRegisterを足すだけです。

```csharp
class Client : IDisposable
{
    readonly TimeSpan timeout;
    readonly ObjectPool<CancellationTokenSource> timeoutTokenSourcePool;
    readonly CancellationTokenSource clientLifetimeTokenSource;

    public TimeSpan Timeout { get; }

    public Client(TimeSpan timeout)
    {
        this.Timeout = timeout;
        this.timeoutTokenSourcePool = ObjectPool.Create<CancellationTokenSource>();
        this.clientLifetimeTokenSource = new CancellationTokenSource();
    }

    public async Task SendAsync(CancellationToken cancellationToken = default)
    {
        var timeoutTokenSource = timeoutTokenSourcePool.Get();

        CancellationTokenRegistration externalCancellation = default;
        if (cancellationToken.CanBeCanceled)
        {
            // 引数のCancellationTokenが発動した場合もTimeout用のCancellationTokenを発火させる
            externalCancellation = cancellationToken.UnsafeRegister(static state =>
            {
                ((CancellationTokenSource)state!).Cancel();
            }, timeoutTokenSource);
        }

        // Clientの寿命に合わせたものも同じように追加しておく
        var clientLifetimeCancellation = clientLifetimeTokenSource.Token.UnsafeRegister(static state =>
        {
            ((CancellationTokenSource)state!).Cancel();
        }, timeoutTokenSource);

        timeoutTokenSource.CancelAfter(Timeout);

        try
        {
            await SendCoreAsync(timeoutTokenSource.Token);
        }
        finally
        {
            // Registerの解除増量
            externalCancellation.Dispose();
            clientLifetimeCancellation.Dispose();
            if (timeoutTokenSource.TryReset())
            {
                timeoutTokenSourcePool.Return(timeoutTokenSource);
            }
        }
    }

    async Task SendCoreAsync(CancellationToken cancellationToken)
    {
        // snip...
    }

    public void Dispose()
    {
        clientLifetimeTokenSource.Cancel();
        clientLifetimeTokenSource.Dispose();
    }
}
```

例外処理も当然必要です！が、ここは最初の例のLinkedTokenで作ったときと同じです。

```csharp
public async Task SendAsync(CancellationToken cancellationToken = default)
{
    var timeoutTokenSource = timeoutTokenSourcePool.Get();

    CancellationTokenRegistration externalCancellation = default;
    if (cancellationToken.CanBeCanceled)
    {
        externalCancellation = cancellationToken.UnsafeRegister(static state =>
        {
            ((CancellationTokenSource)state!).Cancel();
        }, timeoutTokenSource);
    }

    var clientLifetimeCancellation = clientLifetimeTokenSource.Token.UnsafeRegister(static state =>
    {
        ((CancellationTokenSource)state!).Cancel();
    }, timeoutTokenSource);

    timeoutTokenSource.CancelAfter(Timeout);

    try
    {
        await SendCoreAsync(timeoutTokenSource.Token);
    }
    catch (OperationCanceledException ex) when (ex.CancellationToken == timeoutTokenSource.Token)
    {
        // 例外発生時の対応はLinkedTokenで作ったときと特に別に変わらず

        if (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(ex.Message, ex, cancellationToken);
        }
        else if (clientLifetimeTokenSource.IsCancellationRequested)
        {
            throw new OperationCanceledException("Client is disposed.", ex, clientLifetimeTokenSource.Token);
        }
        else
        {
            throw new TimeoutException($"The request was canceled due to the configured Timeout of {Timeout.TotalSeconds} seconds elapsing.", ex);
        }
    }
    finally
    {
        externalCancellation.Dispose();
        clientLifetimeCancellation.Dispose();
        if (timeoutTokenSource.TryReset())
        {
            timeoutTokenSourcePool.Return(timeoutTokenSource);
        }
    }
}
```

async/awaitを封印してIValueTaskSourceを使った実装をする場合は、これよりも複数のコールバックを手で処理する必要があるため、遥かに複雑性が増してしまう（async/awaitは偉大！）のですが、基本的な流れは一緒です。CancellationTokenRegistrationの挙動をしっかり把握して実装すれば、レースコンディション起因のバグも防げるはずです。

まとめ
---
簡単かどうかでいうと、言われればなるほどそうですねーって感じですが、都度考えてやれって言われると結構難しいと思います。なので、こういうパターンなんですね、というのを頭に叩き込んでおくというのは重要だと思いますし、まぁとりあえず覚えてください。覚えれば、別にコード的に複雑というわけでもないので、易易と対処できるようになるはずです。

[StackExchange.Redis](https://github.com/StackExchange/StackExchange.Redis/)も非同期メソッド、CancellationTokenを受け取ってなかったりしますし、パフォーマンスを追求しつつCancellationToken対応を入れるのは、かなり難しい問題だったりします。しかしこの .NET 6世代ではかなりメソッドも増えていて、やろうと思えばやりきれるだけの手札が揃っています。なので、パターン化して真正面から立ち向かいましょう……！