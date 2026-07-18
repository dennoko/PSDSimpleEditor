using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace PSDSimpleEditor
{
    // ── レイヤー複数選択 + 一括編集の伝播 ──────────────────────────────────
    // ─── partial 見取り図 ───────────────────────────────────────────
    // 責務   : レイヤー行の複数選択状態の管理 (Ctrl/Shift クリック・ハイライト・選択数表示)、
    //          選択中レイヤーへの編集一括伝播ヘルパー (スカラーは delta、bool/非スカラーは絶対適用)
    // 宣言   : _selectedLayerGuids, _selectionAnchorGuid, _visibleLeafOrder, _leafRowByGuid,
    //          _visibleGroups, _groupRowByGuid,
    //          _selectionCountLabel, _selectionHintLabel, _selectionClearButton
    // 参照   : _layerByGuid (R), _layerTreeScrollView (R)
    // 依存   : CloneGradient/CloneCurve (.AdjustmentClipboard.cs), AdjustmentLutBaker (LUT ベイク),
    //          RebuildLayerTree (.UIToolkit.LayerTree.cs) が行レジストリを構築する
    // ────────────────────────────────────────────────────────────────
    public partial class PSDSimpleEditorWindow
    {
        const string SelectedRowClass = "layer-selected";

        // 選択状態は UI の一時状態。Undo 対象にせず、ドメインリロードでは PSD データと一緒に消える。
        // レイヤーの恒等性は Undo 機構と同じ GUID (layer.Guid / _layerByGuid) で扱う。
        [NonSerialized] readonly HashSet<string> _selectedLayerGuids = new HashSet<string>();
        [NonSerialized] string _selectionAnchorGuid;   // Shift 範囲選択の起点

        // RebuildLayerTree が構築する「表示順のリーフ一覧」と「ハイライト付け替え用の行要素」。
        // 選択集合に入るのはリーフのみ (色調補正はリーフ専用)。グループ行は配下リーフが
        // 全選択のときにハイライトのみ追随する (UpdateGroupHighlights)。
        [NonSerialized] readonly List<PSDLayer> _visibleLeafOrder = new List<PSDLayer>();
        [NonSerialized] readonly Dictionary<string, VisualElement> _leafRowByGuid = new Dictionary<string, VisualElement>();
        [NonSerialized] readonly List<PSDLayer> _visibleGroups = new List<PSDLayer>();
        [NonSerialized] readonly Dictionary<string, VisualElement> _groupRowByGuid = new Dictionary<string, VisualElement>();

        Label  _selectionCountLabel;   // レイヤーパネルヘッダの選択数表示
        Label  _selectionHintLabel;    // 同・複数選択操作の補助文
        Button _selectionClearButton;  // 同・全解除ボタン

        // ── 選択操作 ────────────────────────────────────────────────────────

        /// <summary>リーフ行ヘッダのクリック。Shift=範囲選択 (選択済みの場合は解除) / Ctrl(Cmd)=トグル / 通常=単一選択。</summary>
        void OnLeafRowPointerDown(PointerDownEvent evt, PSDLayer layer)
        {
            if (evt.shiftKey)
            {
                if (_selectedLayerGuids.Contains(layer.Guid)) DeselectLeaf(layer);
                else                                         SelectRange(layer);
            }
            else if (evt.ctrlKey || evt.commandKey)  ToggleSelect(layer);
            else                                     SelectSingle(layer);

            // Esc での解除がツリーにフォーカスがある間は効くようにする (best-effort)
            _layerTreeScrollView?.Focus();
        }

        void DeselectLeaf(PSDLayer layer)
        {
            if (_selectedLayerGuids.Remove(layer.Guid))
            {
                SetRowHighlight(layer.Guid, false);
                if (_selectionAnchorGuid == layer.Guid) _selectionAnchorGuid = null;
                UpdateSelectionUI();
            }
        }

        void SelectSingle(PSDLayer layer)
        {
            // 唯一の選択行を再クリックしたら解除
            if (_selectedLayerGuids.Count == 1 && _selectedLayerGuids.Contains(layer.Guid))
            {
                ClearSelection();
                return;
            }
            foreach (string g in _selectedLayerGuids) SetRowHighlight(g, false);
            _selectedLayerGuids.Clear();
            _selectedLayerGuids.Add(layer.Guid);
            SetRowHighlight(layer.Guid, true);
            _selectionAnchorGuid = layer.Guid;
            UpdateSelectionUI();
        }

        void ToggleSelect(PSDLayer layer)
        {
            if (_selectedLayerGuids.Remove(layer.Guid))
            {
                SetRowHighlight(layer.Guid, false);
                if (_selectionAnchorGuid == layer.Guid) _selectionAnchorGuid = null;
            }
            else
            {
                _selectedLayerGuids.Add(layer.Guid);
                SetRowHighlight(layer.Guid, true);
                _selectionAnchorGuid = layer.Guid;
            }
            UpdateSelectionUI();
        }

        /// <summary>アンカー〜クリック行を表示順リストの区間で置換選択する。アンカーは動かさない。</summary>
        void SelectRange(PSDLayer to)
        {
            int anchorIdx = -1, toIdx = -1;
            for (int i = 0; i < _visibleLeafOrder.Count; i++)
            {
                if (_visibleLeafOrder[i].Guid == _selectionAnchorGuid) anchorIdx = i;
                if (ReferenceEquals(_visibleLeafOrder[i], to))         toIdx     = i;
            }
            if (toIdx < 0) return;
            if (anchorIdx < 0) { SelectSingle(to); return; }

            foreach (string g in _selectedLayerGuids) SetRowHighlight(g, false);
            _selectedLayerGuids.Clear();
            int lo = Mathf.Min(anchorIdx, toIdx), hi = Mathf.Max(anchorIdx, toIdx);
            for (int i = lo; i <= hi; i++)
            {
                _selectedLayerGuids.Add(_visibleLeafOrder[i].Guid);
                SetRowHighlight(_visibleLeafOrder[i].Guid, true);
            }
            UpdateSelectionUI();
        }

        /// <summary>
        /// グループ行ヘッダ (タイトル〜ブレンドモード左までの余白全体) のクリック。
        /// ・折りたたみ中          : 展開して配下リーフを選択に加える
        /// ・展開中 + 通常クリック : 折りたたむ (非表示になった子行は Prune で選択から自動的に外れる)
        /// ・展開中 + 修飾キー     : 折りたたみ状態は変えず、配下リーフの選択をまとめてトグル
        /// </summary>
        void OnGroupRowPointerDown(PointerDownEvent evt, PSDLayer group)
        {
            bool modifier = evt.ctrlKey || evt.commandKey || evt.shiftKey;

            if (!group.IsExpanded)
            {
                // 畳んでいるフォルダの選択: 展開してから配下を選択状態にする
                group.IsExpanded = true;
                RebuildLayerTree(); // 子行を生成して _leafRowByGuid / _visibleLeafOrder を更新
                SelectDescendantLeaves(group);
            }
            else if (modifier)
            {
                ToggleDescendantSelection(group);
            }
            else
            {
                group.IsExpanded = false;
                RebuildLayerTree(); // Prune が畳まれた配下を選択から外す
            }
            _layerTreeScrollView?.Focus();
        }

        /// <summary>配下の (行が見えている) リーフを選択に加える (既存の選択は保持)。</summary>
        void SelectDescendantLeaves(PSDLayer group)
        {
            var leaves = new List<PSDLayer>();
            CollectVisibleDescendantLeaves(group, leaves);
            if (leaves.Count == 0) return;

            foreach (var l in leaves)
            {
                _selectedLayerGuids.Add(l.Guid);
                SetRowHighlight(l.Guid, true);
            }
            _selectionAnchorGuid = leaves[0].Guid;
            UpdateSelectionUI();
        }

        /// <summary>
        /// 配下の (行が見えている) リーフ全体の選択状態をまとめてトグルする:
        /// 全員選択済みなら解除、そうでなければ全員選択に加える。
        /// </summary>
        void ToggleDescendantSelection(PSDLayer group)
        {
            var leaves = new List<PSDLayer>();
            CollectVisibleDescendantLeaves(group, leaves);
            if (leaves.Count == 0) return;

            bool allSelected = true;
            foreach (var l in leaves)
                if (!_selectedLayerGuids.Contains(l.Guid)) { allSelected = false; break; }

            if (allSelected)
            {
                foreach (var l in leaves)
                {
                    _selectedLayerGuids.Remove(l.Guid);
                    SetRowHighlight(l.Guid, false);
                    if (_selectionAnchorGuid == l.Guid) _selectionAnchorGuid = null;
                }
            }
            else
            {
                foreach (var l in leaves)
                {
                    _selectedLayerGuids.Add(l.Guid);
                    SetRowHighlight(l.Guid, true);
                }
                _selectionAnchorGuid = leaves[0].Guid;
            }
            UpdateSelectionUI();
        }

        /// <summary>配下のリーフのうち、ツリーに行が存在する (見えている) ものを再帰収集する。</summary>
        void CollectVisibleDescendantLeaves(PSDLayer group, List<PSDLayer> result)
        {
            if (group.Children == null) return;
            foreach (var child in group.Children)
            {
                if (child.Children != null) CollectVisibleDescendantLeaves(child, result);
                else if (_leafRowByGuid.ContainsKey(child.Guid)) result.Add(child);
            }
        }

        void ClearSelection()
        {
            foreach (string g in _selectedLayerGuids) SetRowHighlight(g, false);
            _selectedLayerGuids.Clear();
            _selectionAnchorGuid = null;
            UpdateSelectionUI();
        }

        /// <summary>
        /// RebuildLayerTree 直後に呼ぶ。ツリーに行が存在しなくなった GUID (グループ折りたたみ /
        /// 非表示化で消えた行) を選択から外し、「見えている行だけが同期対象」の不変条件を保つ。
        /// </summary>
        void PruneSelectionToVisibleRows()
        {
            _selectedLayerGuids.RemoveWhere(g => !_leafRowByGuid.ContainsKey(g));
            if (_selectionAnchorGuid != null && !_leafRowByGuid.ContainsKey(_selectionAnchorGuid))
                _selectionAnchorGuid = null;
            UpdateSelectionUI();
        }

        void SetRowHighlight(string guid, bool on)
        {
            if (!_leafRowByGuid.TryGetValue(guid, out var row) || row == null) return;
            if (on) row.AddToClassList(SelectedRowClass);
            else    row.RemoveFromClassList(SelectedRowClass);
        }

        /// <summary>選択変更後の表示更新 (選択数ラベル・補助文・解除ボタン・グループハイライト)。</summary>
        void UpdateSelectionUI()
        {
            int n = _selectedLayerGuids.Count;
            var display = n > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (_selectionCountLabel != null)
            {
                _selectionCountLabel.text = PSDTranslation.GetFormat("SelectionCountFormat", n);
                _selectionCountLabel.style.display = display;
            }
            if (_selectionHintLabel != null)
                _selectionHintLabel.style.display = display;
            if (_selectionClearButton != null)
                _selectionClearButton.style.display = display;
            UpdateGroupHighlights();
        }

        /// <summary>配下の見えているリーフがすべて選択されているグループ行にハイライトを追随させる。</summary>
        void UpdateGroupHighlights()
        {
            foreach (var group in _visibleGroups)
            {
                if (!_groupRowByGuid.TryGetValue(group.Guid, out var row) || row == null) continue;

                var leaves = new List<PSDLayer>();
                CollectVisibleDescendantLeaves(group, leaves);
                bool all = leaves.Count > 0;
                foreach (var l in leaves)
                    if (!_selectedLayerGuids.Contains(l.Guid)) { all = false; break; }

                if (all) row.AddToClassList(SelectedRowClass);
                else     row.RemoveFromClassList(SelectedRowClass);
            }
        }

        /// <summary>
        /// 行クリックの target がヘッダ内の既存コントロール (表示 Toggle / ブレンド Dropdown 等) の
        /// 場合は選択処理をしない。target から stopAt まで親を遡って判定する。
        /// </summary>
        static bool IsInteractiveTarget(VisualElement target, VisualElement stopAt)
        {
            for (var ve = target; ve != null && ve != stopAt; ve = ve.parent)
                if (ve is Toggle || ve is DropdownField || ve is Button || ve is IMGUIContainer)
                    return true;
            return false;
        }

        // ── 一括編集の伝播 ──────────────────────────────────────────────────

        /// <summary>編集行が選択に含まれ、かつ 2 レイヤー以上選択中のとき true (選択外の行は従来通り単独編集)。</summary>
        bool IsMultiEditActive(PSDLayer edited) =>
            _selectedLayerGuids.Count >= 2 && _selectedLayerGuids.Contains(edited.Guid);

        /// <summary>
        /// 選択中の他リーフレイヤーへ apply を実行する (一括編集が非アクティブなら何もしない)。
        /// 必ず RegisterUndo() の後・SaveStatesToSerialized() の前に呼ぶこと
        /// (1 回の Undo.RecordObject の差分に全レイヤー分が入り、Ctrl+Z 一発で戻る)。
        /// </summary>
        void ForEachCoTarget(PSDLayer edited, Action<PSDLayer> apply)
        {
            if (!IsMultiEditActive(edited) || _layerByGuid == null) return;
            foreach (string g in _selectedLayerGuids)
            {
                if (g == edited.Guid) continue;
                if (_layerByGuid.TryGetValue(g, out var t) && t.Children == null)
                    apply(t);
            }
        }

        /// <summary>相対値 (差分) 適用: 現在値 + delta をスライダーレンジでクランプ。</summary>
        static float AddClamped(float cur, float delta, float min, float max) =>
            Mathf.Clamp(cur + delta, min, max);

        static Vector3 AddClamped(Vector3 cur, Vector3 delta, float min, float max) =>
            new Vector3(
                Mathf.Clamp(cur.x + delta.x, min, max),
                Mathf.Clamp(cur.y + delta.y, min, max),
                Mathf.Clamp(cur.z + delta.z, min, max));

        /// <summary>グラデーションのみディープコピーで差し替えて LUT を焼き直す (Enabled/Opacity は触らない)。</summary>
        static void ApplyGradientToLayer(PSDLayer t, Gradient src)
        {
            t.UI.Gradient = CloneGradient(src);
            AdjustmentLutBaker.BakeGradientLut(t);
            if (t.UI.GradientMapNormalize) AdjustmentLutBaker.ComputeGradientLumRange(t);
        }

        /// <summary>選択中リーフのスナップショット (表示順)。列挙中に選択集合を変更しても安全なよう List で返す。</summary>
        List<PSDLayer> EnumerateSelectedLeaves()
        {
            var result = new List<PSDLayer>();
            foreach (var layer in _visibleLeafOrder)
                if (_selectedLayerGuids.Contains(layer.Guid))
                    result.Add(layer);
            return result;
        }
    }
}
