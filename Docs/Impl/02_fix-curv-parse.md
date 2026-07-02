# 02: トーンカーブ (curv) の構造ズレ修正

- 優先度: **高** (カーブ制御点が無意味な値になり、壊れたカーブが「有効」として読み込まれる)
- 規模: 小 (1 メソッドの書き直し + シグネチャ変更 1 箇所)
- 対象ファイル: `Core/Parser/PSDAdditionalInfoParser.cs`

## 背景 (バグ内容)

`HandleCurves` (現在 212 行付近) は次の構造を想定している:

```
(誤) version(2B) → channelCount(2B) → channelId(2B) → pointCount(2B) → 点列
```

しかし実際の 'curv' の形式は以下 (psd-tools `Curves.read = read_fmt("BHI")` と照合済み):

```
(正) 1B  is_map フラグ (通常 0。1 なら 256B のルックアップマップ形式)
     2B  version (=1)
     4B  チャンネルビットマップ (bit n が立っていればチャンネル n のカーブが続く。
         bit 0 = 複合/コンポジットチャンネル)
     ビットが立っているチャンネルごと (ビット番号の昇順):
       2B  pointCount (2..19)
       pointCount × [ 2B output値, 2B input値 ]   ※ (output, input) の順
```

先頭 1 バイトの欠落と、4 バイトビットマップを「2B カウント + 2B チャンネル ID」と
誤読することで全体がズレ、読み取った制御点はゴミになる。さらに現在の実装は
ブロック境界チェックを持たないため、誤読した pointCount が大きいとブロック外の
バイトまで読み進む (呼び出し側の境界 seek でストリーム自体は復旧するが、
ゴミカーブが `HasCurves = true` で残る)。

## 修正内容

### 1. HandleAdditionalInfo からの呼び出しに len を渡す

```csharp
case "curv": // トーンカーブ
    HandleCurves(r, layer, len);
    break;
```

### 2. HandleCurves を正しい構造で書き直す

方針: v1 対応として **複合チャンネル (bit 0) のカーブのみ** を採用する。
per-channel カーブ (bit 1 以降) は読み飛ばす。is_map == 1 (ルックアップマップ形式) は
未対応としてスキップする。

```csharp
static void HandleCurves(BigEndianBinaryReader r, PSDLayer layer, uint len)
{
    // curv バイナリ構造 (psd-tools 準拠):
    //   is_map(1B) + version(2B, =1) + チャンネルビットマップ(4B)
    //   + ビットが立っているチャンネルごと (昇順): pointCount(2B) + 点 (output(2B), input(2B))×N
    // v1 対応: 複合チャンネル (bit 0) のカーブのみ採用。is_map=1 (マップ形式) は未対応。
    long end = r.Position + len;

    byte isMap = r.ReadByte();
    if (isMap != 0) return;             // 256B ルックアップマップ形式は未対応

    r.ReadUInt16();                     // version (=1)
    uint channelBits = r.ReadUInt32();  // ビットマップ (bit 0 = 複合チャンネル)
    if (channelBits == 0) return;

    for (int bit = 0; bit < 32; bit++)
    {
        if ((channelBits & (1u << bit)) == 0) continue;
        if (r.Position + 2 > end) return;           // 想定レイアウトと不一致

        ushort pointCount = r.ReadUInt16();
        if (pointCount > 19) return;                 // 仕様上 2..19。超過はレイアウト不一致とみなす

        if (bit == 0)
        {
            // 複合チャンネル: 採用
            if (r.Position + pointCount * 4 > end) return;
            var points = new List<Vector2>(pointCount);
            for (int i = 0; i < pointCount; i++)
            {
                ushort output = r.ReadUInt16();
                ushort input  = r.ReadUInt16();
                points.Add(new Vector2(input, output));
            }
            if (points.Count < 2) return;

            layer.Adjustment.HasCurves   = true;
            layer.Adjustment.CurvePoints = points;
            return; // 複合チャンネルを読めたら終了 (per-channel は捨てる)
        }

        // 複合以外のチャンネル: 点列を読み飛ばす
        long skip = pointCount * 4L;
        if (r.Position + skip > end) return;
        r.Skip(skip);
    }
}
```

## 検証方法

1. Photoshop でトーンカーブ調整レイヤー (例: S 字カーブ、点 3〜4 個) を含む PSD を作成。
2. 読み込んで verbose ダンプに `Curves(4pts)` のように正しい点数が出ること、
   レイヤーパネルのカーブエディタに Photoshop で作った形状に近いカーブが
   表示されることを確認。
3. プレビューと「マージ参照」小窓を比較し、階調の傾向が一致することを確認。
4. Clip Studio Paint の「トーンカーブ」レイヤーを PSD 書き出ししたファイルでも確認
   (CSP は curv として書き出す)。

## 注意事項

- `List<Vector2>` / `Mathf` を使うため既存の using (System.Collections.Generic / UnityEngine)
  で足りる。
- 点は (output, input) の順で格納されている。`Vector2(input, output)` に詰め替えるのは
  既存コードと同じ (AnimationCurve の x=入力, y=出力 に対応)。
- 例外を投げずに `return` で抜ければ呼び出し側の境界 seek が後始末する
  (プロジェクトの境界管理規約どおり)。
- 将来 per-channel カーブに対応する場合もこの構造が土台になるので、
  ビットマップの走査ループは全ビット分回す形を維持すること。
