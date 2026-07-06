# Undo リファクタリング計画

## 概要

レイヤー編集の Undo 対応において、現在の実装には保守性上の大きな課題が 2 つ残っている。
本ドキュメントはこれらの課題と具体的な改善案を記録する。

---

## 課題 1: SerializableLayerState と LayerEditState の二重定義

### 現状

Undo の仕組みは `EditorWindow` (ScriptableObject) の `[SerializeField]` を利用して
レイヤー状態をシリアライズする。しかし `PSDLayer.UI` の型である `LayerEditState` は
`[NonSerialized]` なランタイムオブジェクトであるため、別途 `SerializableLayerState` クラスを
定義し、約 40 フィールドを手動でミラーリングしている。

この結果、フィールドを追加する際に 4 箇所の同期更新が必要：

1. `LayerEditState` — フィールド追加
2. `SerializableLayerState` — 同名フィールド追加
3. `CaptureLayerState()` — コピー処理追加
4. `ApplyLayerState()` — 復元処理追加

### リスク

- **同期漏れ**: いずれかの箇所を更新し忘れると、Undo/Redo で値が復元されないサイレントバグになる
- **コード量**: 既に 100 行超のボイラープレートがあり、新フィールド追加のたびに増加する

### 改善案: LayerEditState の [Serializable] 化

`LayerEditState` は既に Unity がシリアライズ可能な型のみを使用している
(`float`, `bool`, `AnimationCurve`, `Gradient`, `Texture2D`, `Vector2/3`, `Color`, enum)。

```csharp
[System.Serializable]
public class LayerEditState
{
    // 既存フィールドそのまま
    public bool  Visible;
    public float Opacity;
    // ...
}
```

`SerializableLayerState` を以下のようにスリム化：

```csharp
[System.Serializable]
class LayerSnapshot
{
    public string         LayerGuid;
    public LayerEditState EditState;  // 直接参照 — コピー不要
    // PSDLayer 直接フィールド (UI ではないもの)
    public BlendMode      BlendMode;
    public BlendMode      GroupBlendMode;
    public bool           IsExpanded;
    public bool           HasSolidColor;
    public Color          SolidColor;
}
```

`CaptureLayerState` / `ApplyLayerState` は `LayerEditState` 全体の参照代入に縮小される。

### 検討事項

- `LayerEditState` を `[Serializable]` にすると、`PSDLayer.UI` に `[NonSerialized]` 属性を
  付けていても `PSDLayer` 自体が Unity 管理下に無いため影響は無い。
- ただし `AnimationCurve` / `Gradient` は参照型のため、Undo スナップショットが
  「参照共有」にならないよう、コピー (deep clone) が必要。
  - `CopyAnimationCurve` / `CopyGradient` は引き続き `Capture` / `Apply` 時に使用する。
- `Texture2D` (ImageClipTex) は Unity オブジェクトなので参照で問題ない。

### 作業見積

- 影響ファイル: `PSDData.cs`, `PSDSimpleEditorWindow.cs`
- 変更行数: 約 120 行削減 (二重定義の解消)
- リスク: 中 (Undo 動作の回帰テスト必須)

---

## 課題 2: SaveStatesToSerialized() の MarkDirty() への統合

### 現状

レイヤーの値を変更する各箇所で、以下の 3 ステップを手動で呼ぶ必要がある：

```csharp
RegisterUndo("Action Name");     // ① Undo スナップショット
layer.UI.SomeProperty = newValue; // ② 値変更
SaveStatesToSerialized();         // ③ シリアライズ状態を更新
MarkDirty();                      // ④ 再合成フラグ
```

③ を忘れると、Undo スナップショットに最新状態が反映されず、
次の Undo 登録時に古い値がスナップショットされるバグになる。

### 改善案

`MarkDirty()` に `SaveStatesToSerialized()` を統合する：

```csharp
void MarkDirty()
{
    SaveStatesToSerialized();  // 常にシリアライズ状態を同期
    _needsRecomposite = true;
}
```

これにより呼び出し側は以下だけで済む：

```csharp
RegisterUndo("Action Name");
layer.UI.SomeProperty = newValue;
MarkDirty();  // SaveStatesToSerialized は内部で自動実行
```

### 検討事項

- `MarkDirty()` は PSD 読み込み直後 (L249) でも呼ばれるが、
  この時点では `InitializeSerializedStates()` 直後なので再実行は無害。
- パフォーマンス: `SaveStatesToSerialized()` は全レイヤーを走査するが、
  `MarkDirty` は元々毎フレームの `Repaint` に繋がるため実質的な追加コストは微小。
- 変更後、全ファイルの `SaveStatesToSerialized()` 呼び出し (約 30 箇所) を削除する。

### 作業見積

- 影響ファイル: `PSDSimpleEditorWindow.cs`, `.Adjustments.cs`, `.LayerPanel.cs`,
  `.UIToolkit.LayerTree.cs`, `.AdjustmentClipboard.cs`
- 変更行数: 約 30 行削除
- リスク: 低 (機械的な削除)

---

## 実施順序

1. **課題 2 (MarkDirty 統合)** を先に実施 — 低リスクでコード量削減
2. **課題 1 (LayerEditState Serializable 化)** を後に実施 — 構造変更を伴うため慎重に
