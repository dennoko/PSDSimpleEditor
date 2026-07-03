# 14: PSDLayer のランタイム一時データを LayerRuntime へ分離

- Issue #5 指摘1「PSDLayer のゴッドオブジェクト化」の第 1 段階 (Step A)
- 優先度: 中 (最大の構造問題の仕上げ。Step B = UI 状態分離は実施済)
- 規模: 中 (機械的なフィールド移動 + 参照の置換 約 30 箇所 / 5 ファイル)
- **他タスク (10〜13) がすべて取り込まれた後に、単独ブランチで行うこと** (差分衝突を避けるため)
- 対象ファイル:
  - `Core/PSDData.cs` (`PSDLayer` 238〜243 行付近のフィールド移動 + `LayerRuntime` クラス新設)
  - `Editor/Window/PSDSimpleEditorWindow.Adjustments.cs` (LUT の焼き込み/破棄)
  - `Editor/Window/PSDSimpleEditorWindow.cs` (クリーンアップ)
  - `Editor/Compositor/LayerCompositor.Params.cs` (LUT の参照)
  - `Editor/Compositor/LayerCompositor.cs` / `LayerCompositor.Export.cs` (参照があれば)

## 背景

`PSDLayer` には性質の異なる 3 種のデータが同居している:

1. パース済みの不変データ (バウンズ、チャンネル、`Adjustment` など)
2. UI 可変状態 — **分離済み** (Step B 実施済: `LayerEditState` 型の `UI` フィールドに集約されている。今回は触らない)
3. **ランタイム一時データ** — window/compositor が生成・管理する描画用キャッシュ。今回の対象
   (PSDLayer 238〜243 行付近にまとまっている):

```csharp
// ── 描画用ランタイムキャッシュ (window が生成・管理する焼き込み LUT 等) ──
[System.NonSerialized] public Texture2D _curveLut;         // トーンカーブ 256×1 LUT
[System.NonSerialized] public Texture2D _gradientLut;      // グラデーションマップ 256×1 LUT
[System.NonSerialized] public Texture2D _gradientFillLut;  // グラデーション塗りつぶし (GdFl) 256×1 LUT (α 込み)
[System.NonSerialized] public float     _gradientLumMin = 0f;  // グラデーションマップ正規化レンジ (window が非透明画素から計算)
[System.NonSerialized] public float     _gradientLumMax = 1f;
```

これらは「データモデル」ではなく描画キャッシュであり、生成/破棄の責任が
フィールド名とコメントでしか表現されていない。専用クラスに括り出して境界を型で示す。

※ `_rawPixels` / `_rawMaskPixels` はパーサー内部の短命データ (テクスチャ構築後に null 化)
なので**今回は移動しない**。

## 修正内容

### 1. `LayerRuntime` クラス新設 (`Core/PSDData.cs` 内、`PSDLayer` の直前)

```csharp
// ─── レイヤーの描画用ランタイムキャッシュ ─────────────────────────────
// パース結果ではなく、Editor 側 (window) が生成・更新・破棄する一時データ。
// Texture2D は HideFlags.HideAndDontSave で生成し、レイヤー破棄時に明示破棄すること。
public class LayerRuntime
{
    public Texture2D CurveLut;         // トーンカーブ 256×1 焼き込み LUT
    public Texture2D GradientLut;      // グラデーションマップ 256×1 LUT
    public Texture2D GradientFillLut;  // GdFl グラデーション塗りつぶし 256×1 LUT (α 込み)
    public float     GradientLumMin = 0f;  // グラデーションマップ正規化レンジ
    public float     GradientLumMax = 1f;
}
```

### 2. `PSDLayer` 側の置き換え

上記 5 フィールドを削除し、代わりに 1 フィールド:

```csharp
// ── 描画用ランタイムキャッシュ (Editor 側が管理。常に非 null) ──
[System.NonSerialized] public LayerRuntime Runtime = new LayerRuntime();
```

インライン初期化により **null チェックは不要** (参照側で `layer.Runtime?.` としないこと)。

### 3. 参照の一括置換

| 旧 | 新 |
|---|---|
| `layer._curveLut` | `layer.Runtime.CurveLut` |
| `layer._gradientLut` | `layer.Runtime.GradientLut` |
| `layer._gradientFillLut` | `layer.Runtime.GradientFillLut` |
| `layer._gradientLumMin` | `layer.Runtime.GradientLumMin` |
| `layer._gradientLumMax` | `layer.Runtime.GradientLumMax` |

置換後、`_curveLut|_gradientLut|_gradientFillLut|_gradientLumM` でリポジトリ全体を検索し、
残存参照がないことを確認する (ドキュメント内の言及は除く)。

### 4. コメントの整合

- `PSDData.cs` 内の「(window が管理)」系コメントは `LayerRuntime` のクラスコメントへ集約。
- 破棄処理 (window のクリーンアップで LUT を `Object.DestroyImmediate` している箇所) の
  対象が `layer.Runtime.*` に変わるだけで、**破棄のタイミング・網羅性は変えない**こと。
  3 つの LUT すべてが破棄経路に乗っていることを置換後に再確認する。

## 検証方法

1. トーンカーブ・グラデーションマップ・GdFl グラデーション塗りつぶしを含む PSD を読み込み、
   プレビューが従来と同一であること (LUT が正しく焼かれて参照されている)。
2. カーブ/グラデーションを UI で編集 → プレビューが追従すること (LUT の再焼き込み経路)。
3. グラデーションマップの「正規化」トグルを操作し、効果が従来どおりであること
   (`GradientLumMin/Max` の計算・参照経路)。
4. 別の PSD を続けて読み込み直し、メモリリーク警告や Console エラーが出ないこと
   (破棄経路の確認)。ウィンドウを閉じても同様。
5. PNG / PSD 書き出しが従来どおり成功すること。

## 注意事項

- **挙動変更ゼロの機械的リファクタリング**であること。ロジックの「ついで修正」はしない。
- `LayerRuntime` は Core/ に置くため UnityEngine のみに依存させる (UnityEditor 禁止)。
- `Runtime` フィールドの再代入 (`layer.Runtime = new ...`) はしない。破棄時も
  フィールドを null にせず、中の Texture2D を破棄して参照を null にする既存方式を踏襲。
- Step B (UI 状態の `LayerEditState` 分離) は実施済のため本タスクの対象外。`UI` フィールドには触れないこと。
