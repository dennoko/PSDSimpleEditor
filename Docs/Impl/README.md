# 実装指示書インデックス (軽微修正タスク)

2026-07-02 のコードレビューで見つかった問題のうち、作業量が軽微なものを
独立したタスクとして切り出した指示書集。各タスクは互いに依存しないため、
別々のエージェント/作業者が並行して着手できる。

| # | ファイル | 内容 | 優先度 | 規模 |
|---|---|---|---|---|
| 01 | [01_fix-thrs-post-parse.md](01_fix-thrs-post-parse.md) | しきい値(thrs)/ポスタリゼーション(post) のバイナリ解釈修正 | 高 | 極小 |
| 02 | [02_fix-curv-parse.md](02_fix-curv-parse.md) | トーンカーブ(curv) の構造ズレ修正 | 高 | 小 |
| 03 | [03_fix-grdm-version3.md](03_fix-grdm-version3.md) | グラデーションマップ(grdm) version 3 対応 | 高 | 極小 |
| 04 | [04_fix-export-dir-assets-prefix.md](04_fix-export-dir-assets-prefix.md) | 出力先 "Assets" プレフィックス誤判定の修正 | 低 | 極小 |
| 05 | [05_fix-writer-empty-channels.md](05_fix-writer-empty-channels.md) | PSD 書き出し: チャンネル0本レコードの回避 + 調整レイヤー消失の警告 | 中 | 小 |
| 06 | [06_shader-path-fallback.md](06_shader-path-fallback.md) | シェーダーパスのハードコード解消 (フォールバック追加) | 中 | 小 |
| 07 | [07_ui-adjustment-layer-gaps.md](07_ui-adjustment-layer-gaps.md) | grdm/blnc 調整レイヤーの UI 欠落の補完 | 低 | 小 |
| 08 | [08_misc-polish.md](08_misc-polish.md) | 細かい磨き込み (ソリッドテクスチャキャッシュ / パススルー不透明度 UI) | 低 | 極小 |

## 全タスク共通の注意事項

- **プロジェクト規約** (CLAUDE.md / REWRITE_SPEC.md):
  - パーサーの可変長ブロックは「`end = pos + len` を先に計算 → 処理 → 必ず `Seek(end)`」で境界管理する。
    ブロック単位の失敗は `Debug.LogWarning` + スキップで続行 (致命的エラー以外は throw しない)。
  - `BlendMode` enum の int 値は LayerBlend.shader の分岐番号と 1:1 対応。**変更禁止**。
  - ランタイム生成の Unity オブジェクト (Texture2D/RenderTexture/Material) は
    `HideFlags.HideAndDontSave` + 明示破棄。
  - UI テキスト・コードコメントは日本語。既存のコメント密度・スタイルに合わせる。
- **外部ライブラリの追加は禁止** (アセットストアのライセンス上の制約。from-scratch パーサーが前提)。
- ビルド/テストコマンドは無い。Unity Editor がコンパイルする。動作確認は
  メニュー **dennokoworks > Dennoko PSD Editor** からツールを開き、Unity Console の
  `[PSDParser]` / `[PSDSimpleEditor]` ログとプレビューの「マージ参照」小窓 (Photoshop 側の
  合成結果) との比較で行う。
- バイナリ形式の根拠: psd-tools (https://github.com/psd-tools/psd-tools) の
  `src/psd_tools/psd/adjustments.py` / `base.py` と照合済み。指示書内の形式記述を正とする。
