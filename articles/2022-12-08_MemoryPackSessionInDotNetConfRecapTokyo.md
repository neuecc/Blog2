# C# 11 による世界最速バイナリシリアライザー「MemoryPack」の作り方

と題して、.NET Conf 2022 Recap Event Tokyo というイベントで話してきました。

<iframe class="speakerdeck-iframe" frameborder="0" src="https://speakerdeck.com/player/7b71dc84ae4a4241aa241340fa890f65" title="C#11 による世界最速バイナリシリアライザー「MemoryPack」の作り方" allowfullscreen="true" mozallowfullscreen="true" webkitallowfullscreen="true" style="border: 0px; background: padding-box padding-box rgba(0, 0, 0, 0.1); margin: 0px; padding: 0px; border-radius: 6px; box-shadow: rgba(0, 0, 0, 0.2) 0px 5px 40px; width: 840px; height: 471px;" data-ratio="1.78343949044586"></iframe>

今回は久々の（数年ぶりの！）オフライン登壇イベントということで、なんだか新鮮な気分で、そして実際、オンライン登壇よりも目の前にオーディエンスがいたほうがいいなぁという思いを新たに。事前レコーディングやオンライン登壇だと、どうしてもライブ感のない、冷めた感じになっちゃうな、と。セッション単体の完成度で言ったら何度も取り直して完璧に仕上げた事前録画のほうがいい、かもしれませんが、でもそういうもんじゃあないかなあ、と。スタジオアルバムとライブアルバムみたいなもんですね。そしてスタジオアルバムに相当するのは念入りに書かれたブログ記事とかだったりするので、事前録画のセッションって、なんか中途半端に感じてしまったりはしますね。スタジオライブみたいな。あれってなんかいまいちじゃないですか、そういうことで。

[MemoryPack](https://github.com/Cysharp/MemoryPack)は先程 v1.9.0 をリリースしました！日刊MemoryPackか？というぐらいに更新ラッシュをしていたのですが、バグというかは機能追加をめちゃくちゃやってました。性能面で究極のシリアライザーを目指した、というのはセッションスライドのほうにも書かせてもらっていますが、機能面でも究極のシリアライザーを目指しています、ということで、めちゃくちゃやれる幅が広がってます。

Formatterという名前付けについて
---
特に誰にも聞かれていないのですが説明しておきたいのが `MemoryPackFormatter` という名前を。Formatterって正直馴染みがないし(`BinaryFormatter`かよ？)、 `IMemoryPackSerializer` にしようかな、と当初は考えていたのですが最終的には(MessagePack for C#と同じの)Formatterに落ち着きました。理由は、エントリーポイントである `MemoryPackSerializer` と紛らわしいんですよね。 `MemoryPackFormatter`は自作でもしない限りは表に出て来ないし、上級向けのオプションなので、すっきりと名前で区別がついたほうが良いかな、という感じでつけてます。System.Text.Jsonの場合は `JsonSerializer` と `JsonConverter`という分類で、同じような感じです。

候補になる名前としては`Serializer`か`Formatter`か`Converter`か`Encoder`か`Codec`という感じでしょうか。単純で当たり前のチョイスのようでいて、ユーザーがなるべく悩まず直感的に理解できるように、しっかり考えて悩みながらつけてるんですよということで。それで出来上がった名前が、単純で当たり前のように思ってもらえれば正解なわけです。