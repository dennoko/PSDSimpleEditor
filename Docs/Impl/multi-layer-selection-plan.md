# レイヤー複数選択 + 一括編集 実装計画

## 概要

レイヤーツリーに複数選択を導入し、選択中のいずれかの行で色調補正を編集すると
選択された全レイヤーへ反映されるようにする。あわせて調整コピペの一括ペーストと、
フォルダ行のクリックによる開閉 / 配下一括選択を提供する。

本ドキュメントは UX 仕様と設計判断の記録。実装の中心は
`Editor/Window/PSDSimpleEditorWindow.Selection.cs` (新規 partial)。

---

## UX 仕様

### リーフ行の選択操作

行ヘッダの「名前部分〜ブレンドモード左までの余白」がクリック当たり判定。
表示 Toggle / ブレンド Dropdown / 歯車ボタンのクリックは選択に化けない
(`IsInteractiveTarget` が target から header まで親を遡って除外)。

| 操作 | 挙動 |
|---|---|
| 通常クリック | 単一選択 (唯一の選択行を再クリックで解除) |
| Ctrl (Cmd) + クリック | トグル選択。追加時はアンカーを更新 |
| Shift + クリック | アンカー〜クリック行を表示順リストの区間で置換選択 (対象が選択済みの場合は選択解除)。アンカー不変 |
| 余白クリック / Esc / 選択解除ボタン | 全解除 (Esc はフォーカス依存の best-effort) |

選択行は `.layer-leaf-card.layer-selected` (USS) でカード全体がハイライトされる。
選択中はレイヤーパネルヘッダに「選択中: N」+ 補助文「[Shiftを押しながら複数選択]」+
解除ボタンを表示する。

### フォルダ行の操作

フォルダヘッダの「タイトル〜ブレンドモード左までの余白全体」が開閉トグルの当たり判定
(▾/▸ ボタンだけでは狭い、という UX 改善要望による)。

| 状態 + 操作 | 挙動 |
|---|---|
| 折りたたみ中 + クリック | **展開して配下リーフを選択に加える** (畳んだままでは選択できない不便の解消) |
| 展開中 + 通常クリック | 折りたたむ。非表示になった子行は Prune で選択から自動的に外れる |
| 展開中 + 修飾キー (Ctrl/Shift) | 折りたたみ状態は変えず、配下リーフの選択をまとめてトグル (全員選択済みなら解除) |
| ▾/▸ ボタン | 従来通り開閉のみ (選択には触れない) |

フォルダ自体は選択集合に入らない (色調補正はリーフ専用のため)。代わりに
**配下の見えているリーフが全選択のときハイライトが追随**する (`UpdateGroupHighlights`)。

### 一括編集の伝播 (選択行で編集 → 全選択に反映)

編集行が選択に含まれ、かつ 2 レイヤー以上選択中のときのみ発動 (`IsMultiEditActive`)。
選択外の行の編集は従来通り単独編集。

| 対象 | 適用方式 |
|---|---|
| 明るさ/コントラスト/色相/彩度/明度、しきい値レベル、階調数、レベル 5 値、カラーバランス Vector3×3、各適用率 | **相対値 (差分)**: 編集行での変化量を各レイヤーの現在値に加算し、スライダーレンジでクランプ (`AddClamped`) |
| 着色/反転/各 Enabled トグル/輝度保持/輝度正規化 | 新しい bool を絶対適用 (Curve/GradientMap 有効化時は `EnsureCurveLut`/`EnsureGradientLut`) |
| トーンカーブ / グラデーション | delta が定義できないためディープコピーで絶対適用 + LUT 再ベイク |
| 画像クリップの Tex/Tile/Blend | 絶対適用 (Tex はアセット参照の共有で可) |
| **表示 / 不透明度 / ブレンドモード / 塗り色 / 色域マスク** | **同期しない** (従来通り行単位) |

相対値方式は「各行が自分の値をインライン表示し続ける」現 UI と相性が良く、
混在値表示の問題が発生しない。端でクランプされたレイヤーは値差が詰まる
(相対編集の標準挙動として許容)。

### 一括ペースト

歯車メニューに「選択中の全レイヤーにペースト (N)」を追加。歯車の行が選択外でも
選択集合に適用する。クリップボード空 or 選択 0 のときは Disabled 表示。

---

## 設計

### 選択状態の管理 (Selection.cs)

```csharp
[NonSerialized] readonly HashSet<string> _selectedLayerGuids;   // 選択集合 (リーフのみ)
[NonSerialized] string _selectionAnchorGuid;                    // Shift 範囲選択の起点
[NonSerialized] readonly List<PSDLayer> _visibleLeafOrder;      // 表示順リーフ一覧
[NonSerialized] readonly Dictionary<string, VisualElement> _leafRowByGuid;   // リーフ行要素
[NonSerialized] readonly List<PSDLayer> _visibleGroups;                      // グループ一覧
[NonSerialized] readonly Dictionary<string, VisualElement> _groupRowByGuid;  // グループ行要素
```

- **GUID をキーにする理由**: Undo 機構が既に `layer.Guid` / `_layerByGuid` を
  レイヤー恒等性の唯一のキーとして使っており、`RebuildLayerTree` を跨いでも不変。
- **Undo 対象にしない**: 選択は表示状態であり、クリックのたびに Undo スタックが
  汚れるのを避ける。`[NonSerialized]` のためドメインリロードで PSD データと一緒に
  消え、整合が自動で取れる。`SerializableLayerState` への同期作業も不要。
- **不変条件「見えている行だけが同期対象」**: `RebuildLayerTree` 末尾の
  `PruneSelectionToVisibleRows()` が、行が存在しない GUID を選択から除去する。
  グループの折りたたみ / 非表示化で見えなくなったレイヤーへ編集が伝播する事故を防ぐ。
- PSD 再読み込み時は `Cleanup()` の `ClearSelection()` で全解除。

### 行クリックのイベント設計 (UIToolkit.LayerTree.cs)

- 行ヘッダに `PointerDownEvent` を**バブルアップ登録** (TrickleDown 不使用)。
  TrickleDown だと Toggle/Dropdown より先に処理してしまい既存操作が選択に化ける。
- `IsInteractiveTarget(target, header)`: target から header まで親を遡り、途中に
  Toggle / DropdownField / Button / IMGUIContainer があれば選択処理をスキップ。
- `_visibleLeafOrder` は `BuildLayerListTopDown` が上→下順に構築するため
  追加順 = 画面表示順が保証される (Shift 範囲選択はこの順序に依存)。
- フォルダの開閉を伴う操作はハンドラ内で `IsExpanded` を書き換えて
  `RebuildLayerTree()` を呼ぶ (▾/▸ ボタンの既存実装と同じ流儀、Undo 登録なし)。

### ハイライト (USS)

選択変更時は `RebuildLayerTree` を呼ばず、`_leafRowByGuid` / `_groupRowByGuid` 経由で
`layer-selected` クラスの付け替えのみ行う (IMGUIContainer 再生成によるスライダーの
ドラッグ状態切断と再レイアウトコストを回避)。ツリー再構築時は
`BuildLeafNodeElement` 内でクラスを復元し、グループは Prune → `UpdateSelectionUI` →
`UpdateGroupHighlights` で復元する。

```css
.layer-leaf-card.layer-selected  { background-color: var(--surface-2); border-color: var(--accent); }
.layer-group-outer.layer-selected { border-color: var(--accent); }
.layer-group-outer.layer-selected > .layer-group-header { background-color: var(--surface-2); }
```

### 編集の伝播機構

既存の定型「新値取得 → 比較 → RegisterUndo → 代入 → SaveStatesToSerialized → MarkDirty」
に対し、**代入直前に delta を捕捉し、代入直後に `ForEachCoTarget` を 1 ブロック挿入**する。

```csharp
float db = nb - layer.UI.Brightness;   // delta 捕捉 (代入前)
RegisterUndo("Modify Brightness/Contrast");
layer.UI.Brightness = nb;
ForEachCoTarget(layer, t => {
    t.UI.Brightness = AddClamped(t.UI.Brightness, db, -150f, 150f);
});
SaveStatesToSerialized();
MarkDirty();
```

- **Undo 1 グループ化**: `RegisterUndo` は「全レイヤーの編集前状態を `_serializedStates`
  へ確定 → `Undo.RecordObject(this)`」なので、1 回の呼び出しで編集行 + 全コターゲットの
  変更が 1 Undo にまとまる。**`RegisterUndo` より前にコターゲットへ書き込まないこと**
  (Undo から漏れる)。
- **ドラッグ中の delta 累積**: IMGUI は毎フレーム「新値 − 現在値」を delta として
  加算するため、コターゲットにもドラッグ総移動量が累積される。ドラッグ 1 回 = 1 Undo
  (RecordObject の差分記録の性質)。
- 変化しなかったスライダーは delta = 0 で自然に no-op になるため、複数スライダーの
  連結ブロックでも個別判定は不要。

### 一括ペースト (AdjustmentClipboard.cs)

`PasteAdjustment` の switch 本体を `PasteAdjustmentCore(kind, layer, raw, allowPreview)`
に抽出 (Undo / Save / MarkDirty 抜き)。`PasteAdjustmentToSelection` は
`RegisterUndo` を 1 回だけ呼んでから `EnumerateSelectedLeaves()` (スナップショット List)
をループする。ColorRangeMask の `BeginColorRangePreview` はプレビュースロットが
単一のため一括時は `allowPreview: false` で抑止。

---

## ファイル構成

| ファイル | 役割 |
|---|---|
| `Editor/Window/PSDSimpleEditorWindow.Selection.cs` (新規) | 選択状態・選択操作・フォルダ開閉連動・伝播ヘルパー |
| `Editor/Window/PSDSimpleEditorWindow.UIToolkit.LayerTree.cs` | 行クリック登録・レジストリ構築・ハイライト復元 |
| `Editor/Window/PSDSimpleEditorWindow.UIToolkit.cs` | 選択数ラベル・補助文・解除ボタン・余白クリック / Esc 解除 |
| `Editor/Window/PSDSimpleEditorWindow.LayerPanel.cs` | 明るさ/コントラスト・HSL への delta 伝播 |
| `Editor/Window/PSDSimpleEditorWindow.Adjustments.cs` | 全 Draw*Controls への伝播挿入 |
| `Editor/Window/PSDSimpleEditorWindow.AdjustmentClipboard.cs` | `PasteAdjustmentCore` 分割 + 一括ペースト |
| `Editor/Window/PSDSimpleEditorWindow.cs` | `Cleanup()` で `ClearSelection()` |
| `Editor/USS/PSDEditorTheme.uss` | `.layer-selected` / `.selection-count` / `.selection-hint` |
| `Editor/Translations/translation_{ja,en}.json` | Selection* / PasteToSelection* キー |

---

## 既知の制約

- 非表示グループ配下は行が生成されないため、展開しても選択対象にならない
  (不変条件「見えている行だけが同期対象」による意図した挙動)。
- Esc での選択解除は UI Toolkit の KeyDown がフォーカス依存のため best-effort。
  確実な解除手段は余白クリックと解除ボタン。
- 相対値適用で端にクランプされたレイヤーは元の値差が保存されない
  (Lightroom 等と同じ相対編集の標準挙動として許容)。

## 検証観点

1. 選択操作: 通常 / Ctrl / Shift クリック、再クリック解除、余白クリック・ボタン解除。
   表示 Toggle・ブレンド Dropdown・歯車のクリックが選択に化けないこと。
2. フォルダ: タイトル領域クリックで開閉すること。畳んだフォルダのクリックで
   展開 + 配下選択になること。展開中の修飾キークリックで配下選択がトグルすること。
   配下全選択時にフォルダがハイライトされること。
3. 伝播: 2 レイヤー選択で片方の色相をドラッグ → 両方が同量動く (相対適用)。
   選択外の行の編集は単独編集のまま。カーブ / グラデーション編集が全選択に反映され
   LUT が再ベイクされる。
4. 一括ペースト: コピー → 複数選択 → 歯車から一括ペースト → 全対象に反映。
5. Undo: ドラッグ 1 回・一括ペースト 1 回が Ctrl+Z 一発で全レイヤー分戻る。
   Undo 後も選択が維持される (Prune 経由で整理)。
6. ライフサイクル: 折りたたみ / 非表示化で選択が Prune される。PSD 再読み込みで
   選択クリア。言語切替 (RebuildUI) 後も選択維持。
