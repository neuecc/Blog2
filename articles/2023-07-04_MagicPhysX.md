# MagicPhysX - .NET用のクロスプラットフォーム物理エンジン

MagicPhysXというライブラリを新しく公開しました！.NETで物理エンジンを動かすというもので、その名の通り、[NVIDIA PhysX
](https://github.com/NVIDIA-Omniverse/PhysX)のC#バインディングとなっています。

* [Cysharp/MagicPhysX](https://github.com/Cysharp/MagicPhysX)

使い道としては

* GUIアプリケーションの3D部分
* 自作ゲームエンジンへの物理エンジン組み込み
* ディープラーニングのためのシミュレーション
* リアルタイム通信におけるサーバーサイド物理

といったことが考えられます。

.NET用のPhysXバインディングは他にも存在しますが、C++/CLIでバインディングを生成している都合上Windowsでしか動かせなかったり、バージョンが最新ではない4.xベースだったりしますが、MagicPhysXは最新のPhysX 5ベースで、かつ、Windows, MacOS, Linuxの全てで動きます！(win-x64, osx-x64, osx-arm64, linux-x64, linux-arm64)。これはバインディングの作り方としてクロスプラットフォームコンパイルに強いRustと、[Cysharp/csbindgen](https://github.com/Cysharp/csbindgen)によってC#のバインディングの自動生成をしているからです。

先にアーキテクチャの話をしましょう。MagicPhysXは[EmbarkStudios](https://www.embark-studios.com/)による[physx-rs](https://github.com/EmbarkStudios/physx-rs)をビルド元に使っています。

> EmbarkStudiosはEA DICEで[Frostbite](https://www.ea.com/frostbite)ゲームエンジン(Battlefield)を作っていた人たちが独立して立ち上げたスタジオで、Rustによるゲームエンジンを作成中です。また、その過程で生まれたRustのライブラリをOSSとして積極的に公開しています。一覧は[Embark Studios Open Source](https://embark.dev/)にあります。必見！

PhysXのライブラリはC++で出来ていて、他の言語で使うことは考慮されていません。そのために他の言語に持ち込むためには、C++上で別言語で使うためのブリッジ部分を作った上で、バインディングを用意するという二度手間が必要になってきます。それはRustであっても例外ではありません。また、二度手間というだけではなく、PhysXのソースコードはかなり大きいため、その作業量も膨大です。

以前に[csbindgen - C#のためのネイティブコード呼び出し自動生成、或いはC#からのネイティブコード呼び出しの現代的手法について](https://neue.cc/2023/03/09-csbindgen.html)で紹介しましたが、[SWIG](https://www.swig.org/)などのC++からの自動生成、Rustであれば[cxx](https://cxx.rs/)、[autocxx](https://github.com/google/autocxx)のような自動化プロジェクトも存在しますが、C++そのものの複雑さからいっても、求めるものを全自動で出力するのは難しかったりします。

physx-rsでは[An unholy fusion of Rust and C++ in physx-rs (Stockholm Rust Meetup, October 2019)](https://www.youtube.com/watch?v=RxtXGeDHu0w)というセッションでPhysXをRustに持ち込むための手段の候補、実際に採用した手段についての解説があります。最終的に採用された手段について端的に言うと、PhysXに特化してコード解析してC APIを生成する独自ジェネレーターを用意した、といったところでしょうか。そしてつまり、physx-rsには他言語でもバインディング手段として使えるPhysXのC APIを作ってくれたということにもなります！

更にcsbindgenには、rsファイル内のextern "C"の関数からC#を自動生成する機能が備わっているので、Rustを経由することでC++のPhysXをC#に持ち込めるというビルドパイプラインとなりました。

そういう成り立ちであるため、MagicPhysXのAPIはPhysXのAPIそのものになっています。

```csharp
using MagicPhysX; // for enable Extension Methods.
using static MagicPhysX.NativeMethods; // recommend to use C API.

// create foundation(allocator, logging, etc...)
var foundation = physx_create_foundation();

// create physics system
var physics = physx_create_physics(foundation);

// create physics scene settings
var sceneDesc = PxSceneDesc_new(PxPhysics_getTolerancesScale(physics));

// you can create PhysX primitive(PxVec3, etc...) by C# struct
sceneDesc.gravity = new PxVec3 { x = 0.0f, y = -9.81f, z = 0.0f };

var dispatcher = phys_PxDefaultCpuDispatcherCreate(1, null, PxDefaultCpuDispatcherWaitForWorkMode.WaitForWork, 0);
sceneDesc.cpuDispatcher = (PxCpuDispatcher*)dispatcher;
sceneDesc.filterShader = get_default_simulation_filter_shader();

// create physics scene
var scene = physics->CreateSceneMut(&sceneDesc);

var material = physics->CreateMaterialMut(0.5f, 0.5f, 0.6f);

// create plane and add to scene
var plane = PxPlane_new_1(0.0f, 1.0f, 0.0f, 0.0f);
var groundPlane = physics->PhysPxCreatePlane(&plane, material);
scene->AddActorMut((PxActor*)groundPlane, null);

// create sphere and add to scene
var sphereGeo = PxSphereGeometry_new(10.0f);
var vec3 = new PxVec3 { x = 0.0f, y = 40.0f, z = 100.0f };
var transform = PxTransform_new_1(&vec3);
var identity = PxTransform_new_2(PxIDENTITY.PxIdentity);
var sphere = physics->PhysPxCreateDynamic(&transform, (PxGeometry*)&sphereGeo, material, 10.0f, &identity);
PxRigidBody_setAngularDamping_mut((PxRigidBody*)sphere, 0.5f);
scene->AddActorMut((PxActor*)sphere, null);

// simulate scene
for (int i = 0; i < 200; i++)
{
    // 30fps update
    scene->SimulateMut(1.0f / 30.0f, null, null, 0, true);
    uint error = 0;
    scene->FetchResultsMut(true, &error);

    // output to console(frame-count: position-y)
    var pose = PxRigidActor_getGlobalPose((PxRigidActor*)sphere);
    Console.WriteLine($"{i:000}: {pose.p.y}");
}

// release resources
PxScene_release_mut(scene);
PxDefaultCpuDispatcher_release_mut(dispatcher);
PxPhysics_release_mut(physics);
```

つまり、そのままでは決して扱いやすくはないです。部分的に動かすだけではなく、本格的にアプリケーションを作るなら、ある程度C#に沿った高レベルなフレームワークを用意する必要があるでしょう。MagicPhysX内ではそうしたサンプルを用意しています。それによって上のコードはこのぐらいシンプルになります。

```csharp
using MagicPhysX.Toolkit;
using System.Numerics;

unsafe
{
    using var physics = new PhysicsSystem(enablePvd: false);
    using var scene = physics.CreateScene();

    var material = physics.CreateMaterial(0.5f, 0.5f, 0.6f);

    var plane = scene.AddStaticPlane(0.0f, 1.0f, 0.0f, 0.0f, new Vector3(0, 0, 0), Quaternion.Identity, material);
    var sphere = scene.AddDynamicSphere(1.0f, new Vector3(0.0f, 10.0f, 0.0f), Quaternion.Identity, 10.0f, material);

    for (var i = 0; i < 200; i++)
    {
        scene.Update(1.0f / 30.0f);

        var position = sphere.transform.position;
        Console.WriteLine($"{i:D2} : x={position.X:F6}, y={position.Y:F6}, z={position.Z:F6}");
    }
}
```

ただしあくまでサンプルなので、参考にしてもらいつつも、必要な部分は自分で作ってもらう必要があります。

Unityのようなエディターがないと可視化されてなくて物理エンジンが正しい挙動になっているのか確認できない、ということがありますが、PhysXにはPhysX Visual Debuggerというツールが用意されていて、MagicPhysXでも設定することでこれと連動させることが可能です。

![](https://user-images.githubusercontent.com/46207/250030945-2018e821-41c4-44a2-aac6-f0705993ab9b.png)

Dedicated Server
---
Cysharpでは[MagicOnion](https://github.com/Cysharp/MagicOnion)や[LogicLooper](https://github.com/Cysharp/LogicLooper)といったサーバーサイドでゲームのロジックを動かすためのライブラリを開発しています。その路線から行って物理エンジンが必要なゲームでさえも通常の .NET サーバーで動かしたいという欲求が出てくるのは至極当然でしょう……（？）

UEやUnityのDedicated Serverの構成だとヘッドレスなUE/Unityアプリケーションをサーバー用ビルドしてホスティングすることになりますが、サーバー用のフレームワークではないので、あまり作りやすいとは言えないんですよね。通常用サーバー向けのライブラリとの互換性、ライフサイクルの違い、ランタイムとしてのパフォーマンスの低さ、などなど。

というわけで、MagicOnionのようなサーバー向けフレームワークを使ったほうがいいのですが、物理エンジンだけはどうにもならない。今までは……？

と、言いたいのですが、まずちゃんとしっかり言っておきたいのですが、現実的には少々（かなり）難しいでしょう！コライダーどう持ってくるの？とかAPIが違う（Unityの物理エンジンはPhysXですが、API的に1:1の写しではないので細かいところに差異がある）のでそもそも挙動を合わせられないし、でもこういう構成ならサーバーだけじゃなくクライアントでも動かしたい、そもそもそうじゃないとデバッガビリティが違いすぎる。

と、ようするに、もしゲーム自体にある程度、物理エンジンに寄せた挙動が必要なら、「物理エンジン大統一」が必須だと。MagicPhysXは残念ながらそうではありません。実のところ当初はそれを目指していました、Unityとほぼ同一挙動でほぼ同一APIになるのでシームレスに持ち込むことができるライブラリなのだ、と。しかし現状はそうではないということは留意してください。また、その当初予定である互換APIを作り込む予定もありません。

まとめ
---
このライブラリ、かなり迷走したプロジェクトでもあって、そもそも最初は[Bullet Physics](https://github.com/bulletphysics/bullet3)を採用する予定でした。ライブラリ名が先に決めてあってMagicBulletってカッコイイじゃん、みたいな。その後に[Jolt Physics](https://github.com/jrouwe/JoltPhysics)を使おうとして、これもバインディングをある程度作って動く状態にしたのですが、「物理エンジン大統一」のためにPhysXにすべきだろうな、という流れで最終的にPhysXを使って作ることにしました。

形になって良かったというのはありますが（そしてcsbindgenの実用性！）、「物理エンジン大統一」を果たせなかったのは少々残念ではあります。最初の完成予想図ではもっともっと革命的なもののはずだったのですが……！

とはいえ、PhysX 5をクロスプラットフォームで.NETに持ち込んだということだけでも十分に難易度が高く新しいことだと思っているので、試す機会があれば、是非触って見ください。