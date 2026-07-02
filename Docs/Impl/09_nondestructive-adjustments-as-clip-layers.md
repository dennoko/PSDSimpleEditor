# 09: ピクセルレイヤーの非破壊調整をクリップ調整レイヤーとして書き出し / 読み込みで畳み戻し

> **【済】** 本ドキュメント作成時に実装済み。

- 優先度: **高** (書き出した PSD からの作業再開・他ソフト互換)
- 規模: 中
- 対象ファイル:
  - `Core/PSDData.cs` (`AdjustmentData.HueColorize` / `PSDLayer.IsToolAdjustmentClip`)
  - `Core/Parser/PSDAdditionalInfoParser.cs` (`hue2` colorization / `dPSE` マーカー)
  - `Core/Parser/PSDLayerAssembler.cs` (`FoldBackToolAdjustmentClips` / `UIColorize` 初期化)
  - `Core/Parser/PSDParser.cs` (畳み戻しフック)
  - `Editor/Writer/PSDAdjustmentInfoWriter.cs` (`EncodeHue2` colorize / マーカーブロック / Encode\* internal 化)
  - `Editor/Writer/PSDExportRecordBuilder.cs` (`AppendAdjustmentClipRecords` / `WillBakeAdjustments` / Color Overlay 変換)
  - `Editor/Window/PSDSimpleEditorWindow.Export.cs` (書き出しダイアログの件数表示)
  - `Editor/Window/PSDSimpleEditorWindow.Adjustments.cs` (`BakeImportedLuts` — カーブ LUT もロード時に焼く)

## 背景

従来、ピクセルレイヤーに設定した非破壊調整 (UI\*) は PSD 書き出し時に
`RenderLayerForExport` で **画素へ焼き込まれ**、元画像と調整パラメーターの両方が
失われていた。書き出した PSD を読み直しても続きから編集できない。

## 実装内容

### 書き出し (PSDExportRecordBuilder)

有効な調整 1 種類ごとに **ゼロ面積 + `Clipping=1` + 調整キー + `dPSE` マーカー** の
クリップ調整レイヤーレコードをベースの直上へ積む。積み順はシェーダー
(`LayerBlend.shader` `ApplyAdjustments`) の適用順:

```
nvrt → levl → curv → post → thrs → brit/CgEd → blnc → hue2 → grdm
```

- グラデーションマップの適用率はレイヤー不透明度へ載せる。
- 着色 (Colorize) は `hue2` の colorization モードで書く
  (hue: -180..180 → 0..360、sat: -100..100 → 0..100 に変換)。
- その上に (あれば) Color Overlay 変換の SoCo クリップレイヤー → 画像クリップレイヤー。

**焼き込みフォールバック** (`WillBakeAdjustments`、ダイアログで件数警告):

- クリップメンバー自身の補正 (調整レイヤー化すると適用対象がクリップ群全体に変わる)
- `clbl=false` のベースの補正 (メンバーが背景へ直接ブレンドされるため再現不能)
- 輝度正規化グラデーションマップ (PSD に対応表現なし)

**Color Overlay 変換**: 非クリップのピクセルレイヤーの lfx2 Color Overlay は
ベタ塗り (SoCo) クリッピングレイヤーへ変換 (マーカーなし = 再読み込み後も
レイヤーのまま)。クリップメンバー上の効果のみ消失が残る。

### 読み込み (PSDLayerAssembler.FoldBackToolAdjustmentClips)

`InitUIState` 後、`dPSE` マーカー付きでベース直上に連続するクリップ調整レイヤーを
ベースの UI\* へ吸収してツリーから除去する。外部ソフトで **マスク付与 / 非表示化 /
ブレンド変更 / 不透明度変更** されたマーカーは情報を壊さないよう畳み戻さず
通常の調整レイヤーとして残す (Photoshop は未知キー `dPSE` を再保存時に破棄する
ため、外部編集済みファイルは自然に全レイヤー扱いへ戻る)。

チャンネル別レベル/カーブは `Adjustment` 側へコピー (合成・再書き出しの参照元)。
ロード時 LUT ベイク (`BakeImportedLuts`) にトーンカーブを追加し、畳み戻した
カーブが初回合成から効くようにした。

## 制限・トレードオフ

- 本ツール内の再現は同一シェーダーのため完全一致。他ソフトでは補正演算の実装差で
  見た目がわずかに異なることがある (焼き込み時代は「見た目固定・編集不可」だった)。
- `curv` は PSD 仕様上 19 点まで (既存実装で間引き)。
- `hue2` の 6 色域レコードは既定値のみ (色域別補正は未対応)。
