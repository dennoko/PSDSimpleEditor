# 13: partial class 各ファイルへ共有状態の見取り図コメントを追加

- Issue #5 指摘3「partial class による不透明性」
- 優先度: 低 (コードの挙動は一切変えない、ドキュメントのみ)
- 規模: 極小 (各ファイル先頭へのコメント追加のみ)
- 対象ファイル:
  - `Editor/Compositor/LayerCompositor*.cs` (4 ファイル)
  - `Editor/Window/PSDSimpleEditorWindow*.cs` (9 ファイル)

## 背景

`LayerCompositor` と `PSDSimpleEditorWindow` は partial class として責務ごとに
ファイル分割されているが、**どのファイルがどの共有 (private) フィールドを
読み書きするか**が暗黙的で、1 ファイルだけ読んだ作業者が状態の出所を追えない。

クラス統合や状態オブジェクト化は行わず (指摘1 Step A/B で共有状態自体を減らす方針)、
まずは各ファイル先頭に「見取り図コメント」を置いて可視化する。

## 修正内容

### 書式 (統一テンプレート)

各 partial ファイルの `partial class` 宣言直上 (既存のファイル概要コメントがあれば
その一部として統合) に、次の形式で記載する:

```csharp
// ─── partial 見取り図 ───────────────────────────────────────────
// 責務   : レイヤーツリー UI の描画 + ブレンドモード Popup
// 宣言   : (このファイルで宣言する共有フィールドがあれば列挙。なければ「なし」)
// 参照   : _psd (R), _needsRecomposite (W), _scrollPos (RW)
// 依存   : DrawAdjustmentGearMenu (AdjustmentClipboard.cs), Recomposite (本体 .cs)
// ────────────────────────────────────────────────────────────────
```

- **宣言**: そのファイルで宣言している共有フィールド/定数。
- **参照**: 他ファイル宣言の共有フィールドのうち、このファイルが読む (R) / 書く (W) もの。
  数が多い場合は代表的なもの + 「ほか UI 状態一式」のような要約で可 (網羅性より読み手の即応性を優先)。
- **依存**: 他の partial ファイルに実装があるメソッドを呼んでいる場合の代表例。

### 対象と作業手順

1. まず本体ファイル (`LayerCompositor.cs` / `PSDSimpleEditorWindow.cs`) の
   フィールド宣言部を読み、共有フィールドの一覧を把握する。
2. 各 partial ファイルを開き、共有フィールドの読み書きを検索 (`_` プレフィックスの
   フィールド参照を目視 + エディタの参照検索) して見取り図を書く。
3. 本体ファイルには「このクラスは N ファイルに分割されている」旨と全ファイルの
   一覧 (ファイル名 + 一言の責務) を書く。

対象ファイル一覧 (2026-07 時点):

- LayerCompositor: `LayerCompositor.cs` (本体)、`.Params.cs`、`.Export.cs`、`.RT.cs`
- PSDSimpleEditorWindow: `PSDSimpleEditorWindow.cs` (本体)、`.Toolbar.cs`、`.LayerPanel.cs`、
  `.Adjustments.cs`、`.Preview.cs`、`.ColorRangeMask.cs`、`.Export.cs`、
  `.AdjustmentClipboard.cs`、`.UIToolkit.cs`

## 検証方法

- コード変更はコメントのみなので、コンパイルが通り Console にエラーが出ないことを確認するだけでよい。
- セルフチェック: 各ファイルの「参照」欄に挙げたフィールドが実際にそのファイル内に登場すること。

## 注意事項

- **コードは 1 行も変更しない** (フィールドの移動・リネーム・アクセス修飾子変更は禁止。
  それはタスク 14 / 本編作業の領分)。
- コメントは日本語。既存のセクション見出しコメント (`// ─── … ───`) と同じ罫線スタイルに合わせる。
- CLAUDE.md の Folder structure 記述が古い場合 (UIToolkit.cs が未記載など) は、
  ついでに実ファイル構成へ更新してよい。
