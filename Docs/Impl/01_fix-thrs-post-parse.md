# 01: しきい値 (thrs) / ポスタリゼーション (post) のバイナリ解釈修正

- 優先度: **高** (読み込んだ PSD の見た目が確実に壊れるバグ)
- 規模: 極小 (数行 ×2 箇所)
- 対象ファイル: `Core/Parser/PSDAdditionalInfoParser.cs`

## 背景 (バグ内容)

`HandleThreshold` / `HandlePosterize` は先頭 2 バイトを「version」として読み捨て、
次の 2 バイトを値として採用している。しかし実際のバイナリ形式に **version フィールドは
存在しない**。正しい形式は次のとおり (psd-tools の `ShortIntegerElement = read_fmt("H2x")` と照合済み):

```
thrs: [level  (uint16, 1..255)] [padding (2 bytes)]
post: [levels (uint16, 2..255)] [padding (2 bytes)]
```

現状は実際の値を「version」として捨て、パディング (0) を値として採用するため:

- しきい値レイヤー: レベルが常に 0 → 合成結果がほぼ全面白になる
- ポスタリゼーションレイヤー: 階調数が常に 0 → `Mathf.Max(2, 0)` で 2 に固定され、
  PSD で設定した階調数が反映されない

## 修正内容

`Core/Parser/PSDAdditionalInfoParser.cs` の 2 メソッドを修正する。

### HandleThreshold (現在 177 行付近)

```csharp
// 修正前
static void HandleThreshold(BigEndianBinaryReader r, PSDLayer layer)
{
    r.ReadUInt16();                     // version (=1)  ← 誤り。この 2 バイトが実際の level
    ushort level = r.ReadUInt16();       // 0 .. 255      ← 実際はパディングを読んでいる
    ...
}

// 修正後: 先頭 2 バイトが level。後続 2 バイトのパディングは呼び出し側の境界 seek に任せる
static void HandleThreshold(BigEndianBinaryReader r, PSDLayer layer)
{
    ushort level = r.ReadUInt16();       // 1 .. 255 (パディング 2B は境界 seek でスキップ)
    layer.Adjustment.HasThreshold   = true;
    layer.Adjustment.ThresholdLevel = Mathf.Clamp(level, 0, 255);
}
```

### HandlePosterize (現在 185 行付近)

```csharp
// 修正後: 先頭 2 バイトが levels
static void HandlePosterize(BigEndianBinaryReader r, PSDLayer layer)
{
    ushort levels = r.ReadUInt16();      // 2 .. 255 (パディング 2B は境界 seek でスキップ)
    layer.Adjustment.HasPosterize    = true;
    layer.Adjustment.PosterizeLevels = Mathf.Max(2, (int)levels);
}
```

コメントの「version (=1)」という記述も削除すること (誤解の元)。

## 検証方法

1. Photoshop (または Clip Studio Paint の PSD 書き出し) で、
   - しきい値調整レイヤー (レベル例: 100) を含む PSD
   - ポスタリゼーション調整レイヤー (階調数例: 6) を含む PSD
   を用意する。
2. ツールで読み込み、`PSDParser.VerboseLog = true` のダンプまたはレイヤーパネルで
   `Threshold(100)` / `Posterize(6)` と読めていることを確認。
3. プレビューを「マージ参照」小窓 (Photoshop 側の合成結果) と目視比較し、
   おおむね一致することを確認。

## 注意事項

- 呼び出し側 (`PSDLayerRecordParser.ParseLayerExtra`) がブロック末尾へ必ず `Seek` するため、
  パディング 2 バイトを明示的に読む必要はない。読んでも害はないが、既存の
  「境界 seek に任せる」スタイルに合わせて読まない実装とする。
- `UIThresholdLevel` / `UIPosterizeLevels` への反映は `PSDLayerAssembler.InitUIState` が
  既に行っているので変更不要。
