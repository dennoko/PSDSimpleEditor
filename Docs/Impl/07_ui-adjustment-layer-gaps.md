# 07: grdm / blnc 調整レイヤーの UI 欠落の補完

- 優先度: 低 (合成には効いているが、ユーザーが確認・調整できない)
- 規模: 小
- 対象ファイル: `Editor/Window/PSDSimpleEditorWindow.LayerPanel.cs`

## 背景

PSD からパースしたグラデーションマップ (grdm) とカラーバランス (blnc) の
調整レイヤーは、合成 (`LayerCompositor.DrawLayer` の調整パス) には正しく反映される。
しかしレイヤーパネル側に 2 つの欠落がある:

1. **`BuildLayerLabel` の [調整] タグ判定に `HasGradientMap` / `HasColorBalance` が漏れている**
   → grdm / blnc の調整レイヤーが「無印の空レイヤー」に見える。
2. **`DrawLayerControls` に grdm / blnc の編集 UI が無い**
   → 調整レイヤー (ゼロ面積) は `DrawAdjustmentFoldout` の対象外
   (`if (!layer.IsAdjustmentLayer)` で除外) のため、パースされた grdm / blnc は
   不透明度スライダーしか表示されず、内容の確認も無効化もできない。
   brit / hue2 / levl / curv / thrs / post には専用行があるのに不整合。

## 修正内容

### 1. BuildLayerLabel (現在 230 行付近)

[調整] 判定の条件に 2 つを追加:

```csharp
else if (!isGroup && layer.IsAdjustmentLayer &&
         layer.Adjustment != null &&
         (layer.Adjustment.HasBrightnessContrast || layer.Adjustment.HasHueSaturation ||
          layer.Adjustment.HasInvert || layer.Adjustment.HasThreshold || layer.Adjustment.HasPosterize ||
          layer.Adjustment.HasLevels || layer.Adjustment.HasCurves ||
          layer.Adjustment.HasGradientMap || layer.Adjustment.HasColorBalance))   // ← 追加
    prefix += "[調整] ";
```

### 2. DrawLayerControls (現在 257 行付近)

トーンカーブ行 (`HasCurves`) の直後、SoCo 行の前あたりに追加:

```csharp
// カラーバランス (blnc)
if (layer.Adjustment != null && layer.Adjustment.HasColorBalance)
    DrawColorBalanceControls(layer, indent);

// グラデーションマップ (grdm)
if (layer.Adjustment != null && layer.Adjustment.HasGradientMap)
    DrawGradientMapControls(layer, indent);
```

`DrawColorBalanceControls` / `DrawGradientMapControls` は
`PSDSimpleEditorWindow.Adjustments.cs` にある既存メソッドをそのまま流用する
(どちらも `UI*` フィールドを編集して `_needsRecomposite` を立てる作りで、
調整レイヤーに対しても安全に動く)。

## 検証方法

1. グラデーションマップ調整レイヤーとカラーバランス調整レイヤーを含む PSD を読み込む。
2. レイヤーパネルで両レイヤーに `[調整]` プレフィックスが付くこと。
3. 両レイヤーを展開すると、それぞれグラデーション編集 / カラーバランスの
   スライダー群が表示され、値が PSD の設定を反映していること。
4. トグルを OFF にすると合成結果から効果が消え、ON に戻すと復帰すること。
5. 通常のピクセルレイヤーの「色調補正」フォールドアウト内の表示が
   従来どおりであること (回帰確認)。

## 注意事項

- grdm の「輝度を正規化」トグルは対象レイヤーのピクセルから範囲を計算するが、
  調整レイヤーはピクセルを持たないため `ComputeGradientLumRange` は 0..1 フォールバック
  (実質無効) になる。これは既存挙動どおりで問題ない。
- `DrawGradientMapControls` 内の歯車メニュー (コピー&ペースト) も調整レイヤーで
  そのまま機能する。特別対応不要。
