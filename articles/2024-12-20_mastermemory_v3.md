# MasterMemory v3 - Source Generator化したC#用の高速な読み込み専用インメモリデータベース

[MasterMemory](https://github.com/Cysharp/MasterMemory) v3出しました！ついにSource Generator化されました！

![image](https://github.com/user-attachments/assets/e804fa52-f6a5-4972-a510-0b3b17a31230)

MasterMemoryはC#のインメモリデータベースで、高速で、メモリ消費量が少なく、タイプセーフ。というライブラリです。SQLiteを素朴に使うよりも *4700*倍高速だぞ、と。 

![image](https://user-images.githubusercontent.com/46207/61031896-61890800-a3fb-11e9-86b7-84c821d347a4.png)

もともとMasterMemoryはC#コードからC#コードを生成するという先進的な設計思想を持ったシステムだったため、Source Generatorとの親和性は高いものでした。今回移植してみて、あまりにもスムーズに移植できるし、旧来のコードも全く手を付けずにそのまま動いたので我ながら感心しました。やっと時代が追い付いたか……。

というわけで、以下のようなC#定義からデータベース構築のためのコードと、クエリ部分がSource Generatorによって自動生成されます。

```csharp
[MemoryTable("person"), MessagePackObject(true)]
public record Person
{
    [PrimaryKey]
    public required int PersonId { get; init; }
    
    [SecondaryKey(0), NonUnique]
    [SecondaryKey(1, keyOrder: 1), NonUnique]
    public required int Age { get; init; }

    [SecondaryKey(2), NonUnique]
    [SecondaryKey(1, keyOrder: 0), NonUnique]
    public required Gender Gender { get; init; }

    public required string Name { get; init; }
}
```

![image](https://user-images.githubusercontent.com/46207/61035808-cb58e000-a402-11e9-9209-d51665d1cd56.png)

C#コードとして生成されるので、クエリが全て入力補完も効くし戻り値も型付けされていてタイプセーフなのはもちろん、パフォーマンスの良さにも寄与しています。

読み取り専用データベースとして使うので、クラス定義はイミュータブルのほうがいいわけですが、最近のC#は `record`, `init`, `required` といった機能が提供されているので、Readonly Databaseとしての使い勝手が更に上がりました。Unityでは`required`は使えませんが`record`と`init`は使えるので、Unityでも問題ありません。

なお、Unity版は今回からNuGetForUnityでの提供となります。また、MessagePack for C#もSource Generator対応のv3を要求します。

Next
---
MasterMemory、実は結構使われています。ゲームでも採用されているものを割と見かけるようになりました。なので、外部ツール由来のコード生成の面倒さにはだいぶ心を痛めていたので、ようやく解消できて本当に嬉しい！

v2からv3へのマイグレーションもそんなに大変ではない、はずです。あえて生成コードの品質や、コアの関数、メソッドシグネチャなどには一切手を加えていないので、今までコマンドラインツールを叩いていた部分を削除するだけで、そのまま動き出すぐらいの代物になっています。

そのうえでrecord対応（今までしてなかった！）や#nullable enable対応（今までしてなかった！）を追加しているので、生成部分以外の使い勝手も上がっているはずです。

今後は[MemoryPack](https://github.com/Cysharp/MermoyPack)対応や、そもそものAPIの更なるモダン化（現状はnetstandard2.0なので古い）、全体的に改修したいところ(ImmutableBuilderなど生成コードの差し替え部分)、などなどやれること自体はめっちゃありますので、折を見て手を入れていけるといいかなあ、と思っています。