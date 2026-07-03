# 11: IsAdjustmentLayer のゼロ面積ヒューリスティック廃止

- Issue #5 指摘4「IsAdjustmentLayer のヒューリスティック判定」
- 優先度: 高 (実在する「空のピクセルレイヤー」を調整レイヤーと誤判定する)
- 規模: 小 (プロパティ定義 1 箇所 + `AdjustmentData` にヘルパー 1 つ)
- 対象ファイル:
  - `Core/PSDData.cs` (`IsAdjustmentLayer` 定義、251 行付近 / `AdjustmentData` クラス、57〜114 行)

## 背景

現在の判定はゼロ面積バウンズのみ:

```csharp
public bool IsAdjustmentLayer => Width <= 0 || Height <= 0;
```

しかし PSD では「新規作成しただけで何も描いていないピクセルレイヤー」も
バウンズがゼロ面積になる。このため空レイヤーが調整レイヤー扱いされ、

- レイヤーパネルで通常の補正フォルドアウト (`DrawAdjustmentFoldout`) が出ない
  (`PSDSimpleEditorWindow.LayerPanel.cs` 169 行付近の `!layer.IsAdjustmentLayer` ガード)
- 合成 (`LayerCompositor.DrawLayer` 362 行付近) で調整レイヤー分岐に入る
  (実害は `hasAdj == false` で素通りするため現状なし)

といったズレが起きる。本来の意味は「調整データを持つ非ピクセルレイヤー」なので、
**パース済みの調整キーの有無**を判定に組み込む。

## 修正内容

### 1. `AdjustmentData` に集約プロパティを追加

`Core/PSDData.cs` の `AdjustmentData` クラス末尾に追加:

```csharp
/// <summary>いずれかの調整/塗りつぶしデータをパース済みか (調整レイヤー判定用)。</summary>
public bool HasAny =>
    HasBrightnessContrast || HasHueSaturation || HasSolidColor || HasInvert ||
    HasThreshold || HasPosterize || HasLevels || HasCurves ||
    HasGradientMap || HasGradientFill || HasColorBalance;
```

※ タスク 12 (dPSE 検証) も同じ `HasAny` を使う。どちらかが先に追加していたら再利用すること。

### 2. `IsAdjustmentLayer` の定義変更

```csharp
/// <summary>調整レイヤー判定: ピクセルを持たず (ゼロ面積)、かつ何らかの調整キーを
/// パース済みのレイヤー。キーを持たないゼロ面積レイヤーは「空のピクセルレイヤー」であり対象外。</summary>
public bool IsAdjustmentLayer =>
    (Width <= 0 || Height <= 0) &&
    SectionType == LayerSectionType.Normal &&
    Adjustment != null && Adjustment.HasAny;
```

### 3. 参照箇所は原則そのままでよい (確認のみ)

定義変更で挙動が変わるのは「ゼロ面積 & 調整キーなし」のレイヤーだけ。全参照 (13 箇所) を
確認済みで、コード変更は不要:

| 参照箇所 | 変更後の挙動 |
|---|---|
| `LayerCompositor.cs` 362 行付近 (調整レイヤー分岐) | 空レイヤーは分岐に入らず、SoCo/GdFl も該当せず、`Texture == null` (425 行付近) で素通り。**見た目は従来と同一** |
| `PSDExportRecordBuilder.cs` 211 行付近 `IsPlainPixelLayer` | `Texture == null` が先に false を返すため変化なし |
| `PSDAdjustmentInfoWriter.cs` 37 行付近 | 空レイヤーは従来もブロック 0 件 → null だったため結果は同じ |
| `PSDSimpleEditorWindow.LayerPanel.cs` 68 行付近 (名前プレフィックス) | `Has*` 併用ガードのため変化なし |
| `PSDSimpleEditorWindow.LayerPanel.cs` 169 行付近 | 空レイヤーにも補正フォルドアウトが表示されるようになる (意図どおり。ただしテクスチャが無いので効果はない) |
| `PSDLayerAssembler.cs` 192 行付近 (畳み戻し先の除外) | `Texture == null` が先に continue するため変化なし |
| `PSDLayerAssembler.cs` 207 行付近 `CanFoldBack` | dPSE マーカーは調整キー付きで書き出されるため引き続き true。キーを剥がされた壊れマーカーは false になる (安全側) |

## 検証方法

1. Photoshop で「新規レイヤー (何も描かない)」を含む PSD を作って読み込み:
   - 空レイヤーがパネルで通常レイヤーとして表示され、補正フォルドアウトが出ること。
   - プレビューが従来と同一であること。
2. 調整レイヤー (色相・彩度、トーンカーブ等) を含む既存 PSD を読み込み:
   - 調整レイヤーが従来どおり `[補正]` 系プレフィックスで表示され、合成結果が変わらないこと。
3. 本ツールで PSD 書き出し → 再読み込みし、非破壊調整が従来どおり畳み戻されること
   (dPSE ラウンドトリップが壊れていないこと)。
4. SoCo べた塗り / GdFl グラデーション塗りつぶしを含む PSD の表示・書き出しが従来どおりであること。

## 注意事項

- `SectionType == Normal` の条件は「グループ開始レコードもゼロ面積」であるため必要。
  ただしグループは全参照箇所で `Children != null` により先に弾かれているので、
  これは防御的な明示に過ぎない (入れておくこと)。
- `Effects` (Color Overlay) しか持たないゼロ面積レイヤーは理論上「空レイヤー」扱いになるが、
  ピクセルの無いレイヤーへの Color Overlay は表示対象が無く実害がないため考慮しない。
- Core/ のファイルなので UnityEditor に依存するコードを入れないこと。
