# 実装指示書インデックス (設計レビュー Issue #5 対応)

[Issue #5「設計レビュー」](https://github.com/dennoko/PSDSimpleEditor/issues/5) の指摘のうち、
作業量が小さく独立して着手できるものをタスクとして切り出した指示書集。
テストは導入しない方針 (動作確認は Unity Editor 上の目視 + Console ログ)。

| # | ファイル | 内容 | Issue 指摘 | 優先度 | 規模 |
|---|---|---|---|---|---|
| 10 | [10_blendmode-shader-mapping-guard.md](10_blendmode-shader-mapping-guard.md) | BlendMode enum ⇔ シェーダー分岐番号の整合を起動時に機械検証 | 指摘2 | 中 | 小 |
| 11 | [11_explicit-adjustment-layer-detection.md](11_explicit-adjustment-layer-detection.md) | IsAdjustmentLayer のゼロ面積ヒューリスティック廃止 (調整キー有無で判定) | 指摘4 | 高 | 小 |
| 12 | [12_dpse-version-validation.md](12_dpse-version-validation.md) | dPSE マーカーのバージョン検証 + 畳み戻し前の内容検証 | 指摘5 | 中 | 小 |
| 13 | [13_partial-class-shared-state-map.md](13_partial-class-shared-state-map.md) | partial class 各ファイルへ共有状態の見取り図コメントを追加 | 指摘3 | 低 | 極小 |
| 14 | [14_layer-runtime-cache-extraction.md](14_layer-runtime-cache-extraction.md) | PSDLayer のランタイム一時データ (LUT 等) を LayerRuntime へ分離 | 指摘1 (Step A) | 中 | 中 |

## 着手順の推奨

- 10〜13 は互いに独立しており並行作業できる (11 と 12 は両方 `AdjustmentData.HasAny` を
  追加するため、片方が先に入ったらもう片方はそれを再利用すること)。
- **14 は最後に単独で行う** (PSDLayer のフィールド移動で他タスクと差分が衝突しやすいため)。

## このバッチの対象外 (指示書なし)

- **指摘1 Step B: `UI*` 約 30 フィールドの `LayerEditState` への分離** — **実施済 (2026-07-03、本編作業)**。
  `PSDLayer.UI` (LayerEditState 型) 経由の `layer.UI.Opacity` 形式へ全参照 (約 430 箇所/12 ファイル) を
  移行済み。描画用 LUT キャッシュ (`_curveLut` 等) は Step A (タスク 14) の対象として PSDLayer に残っている。
- **指摘6: シェーダーパスのハードコード** — 3 段フォールバック実装済み (旧タスク 06) のため対応不要。
- **指摘7: テスト導入** — 方針によりスキップ。
- **PSB / TGA / 3D 同期** — 設計改善ではなく新機能のため別 Issue で管理。

## 全タスク共通の注意事項

- **プロジェクト規約** (CLAUDE.md):
  - パーサーの可変長ブロックは「`end = pos + len` を先に計算 → 処理 → 必ず `Seek(end)`」で境界管理する。
    ブロック単位の失敗は `Debug.LogWarning` + スキップで続行 (致命的エラー以外は throw しない)。
  - `BlendMode` enum の int 値は LayerBlend.shader の分岐番号と 1:1 対応。**変更禁止**。
  - ランタイム生成の Unity オブジェクト (Texture2D/RenderTexture/Material) は
    `HideFlags.HideAndDontSave` + 明示破棄。
  - Core/ は UnityEngine のみ依存 (UnityEditor を参照しない)。依存は単一方向のまま保つ。
  - UI テキスト・コードコメントは日本語。既存のコメント密度・スタイルに合わせる。
- **外部ライブラリの追加は禁止** (アセットストアのライセンス上の制約。from-scratch パーサーが前提)。
- ビルド/テストコマンドは無い。Unity Editor がコンパイルする。動作確認は
  メニュー **dennokoworks > Dennoko PSD Editor** からツールを開き、Unity Console の
  `[PSDParser]` / `[PSDSimpleEditor]` ログとプレビューの「マージ参照」小窓 (Photoshop 側の
  合成結果) との比較で行う。
