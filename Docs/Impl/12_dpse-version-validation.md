# 12: dPSE マーカーのバージョン検証 + 畳み戻し前の内容検証

- Issue #5 指摘5「dPSE ラウンドトリップの密結合」
- 優先度: 中 (将来のフォーマット変更・他ソフト経由の破損データへの防御)
- 規模: 小 (パーサー 1 箇所 + 畳み戻し判定 1 行 + コメント整備)
- 対象ファイル:
  - `Core/Parser/PSDAdditionalInfoParser.cs` (`case "dPSE"`、91 行付近)
  - `Core/Parser/PSDLayerAssembler.cs` (`CanFoldBack`、204 行付近)
  - `Editor/Writer/PSDAdjustmentInfoWriter.cs` (フォーマット仕様コメントの追記、22〜26 行付近)

## 背景

本ツールは書き出し時、ピクセルレイヤーの非破壊調整を `dPSE` マーカー付きの
クリップ調整レイヤーへ変換し、読み込み時にマーカーを検出してベースレイヤーの
編集状態 (`PSDLayer.UI`) へ畳み戻す (`FoldBackToolAdjustmentClips`)。

ライター側は既にバージョン付きでマーカーを書いている
(`PSDAdjustmentInfoWriter.BuildClipMarkerBlock` — data = uint32 BE の `1`):

```csharp
internal static ExportExtraBlock BuildClipMarkerBlock()
    => new ExportExtraBlock { Key = ClipMarkerKey, Data = new byte[] { 0, 0, 0, 1 } };
```

しかしパーサー側はバージョンを**読まずに**フラグを立てるだけ:

```csharp
case "dPSE":
    layer.IsToolAdjustmentClip = true;
    break;
```

このままだと将来フォーマットを変えた (version 2 の) ファイルを旧実装が畳み戻して
データを壊す道が残る。また、他ソフトで編集されて調整キーだけ剥がされたマーカーを
検証なしで吸収・除去してしまう。

## 修正内容

### 1. パーサー: バージョンを読んで既知バージョンのみ受理

`PSDAdditionalInfoParser.cs` の `case "dPSE"`:

```csharp
case "dPSE": // 本ツール製クリップ調整レイヤーのマーカー (読み込み時に編集状態 (UI) へ畳み戻す)
{
    // version (uint32 BE)。既知バージョン (1) のみ畳み戻し対象にする。
    // 未知バージョンや長さ不足は通常の調整レイヤーとして残す (安全側)。
    uint dpseVersion = len >= 4 ? r.ReadUInt32() : 0;
    if (dpseVersion == 1)
        layer.IsToolAdjustmentClip = true;
    else
        Debug.LogWarning($"[PSDParser] 未知の dPSE バージョン ({dpseVersion})。" +
                         "このレイヤーは通常の調整レイヤーとして扱います。");
    break;
}
```

※ ブロック末尾への Seek は呼び出し側が行う既存構造のまま (`end = pos + len` ルール)。
`len < 4` でも読み過ぎない実装にすること (上記コードは 4 バイト未満なら読まない)。

### 2. 畳み戻し判定: 調整キーの存在を確認

`PSDLayerAssembler.cs` の `CanFoldBack` に 1 条件追加 (既存条件の並びの後ろでよい):

```csharp
// 他ソフトの編集で調整キーが剥がされたマーカーは吸収しない (情報が無いのに除去だけされるのを防ぐ)
if (m.Adjustment == null || !m.Adjustment.HasAny) return false;
```

`AdjustmentData.HasAny` はタスク 11 で追加するプロパティ。タスク 11 が未実施なら
同じ定義を先に `Core/PSDData.cs` の `AdjustmentData` へ追加すること (定義はタスク 11 参照)。

### 3. フォーマット仕様をライターのコメントに明文化

`PSDAdjustmentInfoWriter.cs` の `ClipMarkerKey` 付近のコメントを次の内容を含む形に更新:

- ブロック構造: `dPSE` = 追加情報ブロック (8BIM シグネチャ + キー + 長さ、PSD 仕様準拠)。
  データは **uint32 BE のバージョン番号のみ (現行 = 1)**。
- 互換性ルール:
  - パーサーは既知バージョンのみ畳み戻す。未知バージョンは通常の調整レイヤー扱い。
  - データを追加する変更をする場合はバージョンを上げること (旧実装は安全側へフォールバックする)。
  - Photoshop 等は未知キーを保存時に破棄するため、外部編集されたファイルは
    自動的に通常の調整レイヤーになる (既存挙動、変更なし)。

## 検証方法

1. 本ツールで非破壊調整付き PSD を書き出し → 再読み込み:
   従来どおり調整がベースレイヤーへ畳み戻され、マーカーレイヤーがパネルに現れないこと。
2. バイナリエディタ (または一時的にライターの version を 2 に変更して書き出し) で
   dPSE のバージョンを 2 にしたファイルを読み込み:
   - Console に「未知の dPSE バージョン」警告が出ること。
   - マーカーが通常のクリップ調整レイヤーとしてパネルに表示され、合成結果は変わらないこと。
   - 確認後、ライターの version は 1 に戻す。
3. Photoshop (または Krita 等) で一度上書き保存した PSD を読み込み、
   従来どおり「独立した調整レイヤー」として表示されること (退行がないこと)。

## 注意事項

- ライターの書き出すバージョンは **1 のまま変えない** (今回はパーサー側の防御のみ)。
- `case` 内でローカル変数を使うためブロック `{}` で囲むこと (switch の変数スコープ)。
- `Debug.LogWarning` は UnityEngine のもの (Core/ から使用可)。throw はしない。
