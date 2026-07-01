using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace PSDSimpleEditor
{
    // ── レイヤーパネル (左): ツリー描画・スプリッター・ブレンドモード Popup ──
    public partial class PSDSimpleEditorWindow
    {
        void DrawLayerPanel()
        {
            // パネル外枠 (border 付き・padding/margin なし → スプリッター計算を単純化)
            EditorGUILayout.BeginVertical(PSDEditorTheme.PanelStyle,
                                          GUILayout.Width(_layerPanelWidth),
                                          GUILayout.ExpandHeight(true));

            // ヘッダ帯 (Surface2)
            EditorGUILayout.BeginHorizontal(PSDEditorTheme.ToolbarStyle);
            GUILayout.Label("レイヤー", PSDEditorTheme.SectionHeaderStyle);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            // 本体 (手動パディング)
            GUILayout.Space(6);
            _layerScroll = EditorGUILayout.BeginScrollView(_layerScroll, GUILayout.ExpandHeight(true));

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(8);
            EditorGUILayout.BeginVertical();
            DrawLayerListTopDown(_psdFile.Layers, 0);
            EditorGUILayout.EndVertical();
            GUILayout.Space(8);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndScrollView();
            GUILayout.Space(6);

            EditorGUILayout.EndVertical();
        }

        /// <summary>レイヤーパネルとプレビューパネルの境界をドラッグで調整するためのスプリッターを描画する。</summary>
        /// <param name="originX">メイン水平領域左端 (パネル開始) の X 座標。</param>
        void DrawSplitter(float originX)
        {
            // 10px 幅のホバー/インタラクション領域を確保
            Rect rect = GUILayoutUtility.GetRect(10f, 10f, GUILayout.Width(10f), GUILayout.ExpandHeight(true));

            // ホバー時のカーソル形状を左右矢印に変更
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.ResizeHorizontal);

            // ホバー状態の更新
            Event currentEvent = Event.current;
            bool isHovered = rect.Contains(currentEvent.mousePosition);
            if (isHovered != _isSplitterHovered)
            {
                _isSplitterHovered = isHovered;
                Repaint();
            }

            // スプリッターの線色 (テーマに合わせる)
            Color dividerColor;
            if (_isResizing)
                dividerColor = PSDEditorTheme.SemanticInfo;                       // ドラッグ中
            else if (_isSplitterHovered)
                dividerColor = Color.Lerp(PSDEditorTheme.Outline, Color.white, 0.4f); // ホバー時
            else
                dividerColor = PSDEditorTheme.Outline;                            // 通常時

            // 2px 幅の中央線として描画
            Rect visualRect = new Rect(rect.x + (rect.width - 2f) / 2f, rect.y, 2f, rect.height);
            EditorGUI.DrawRect(visualRect, dividerColor);

            // マウス入力によるリサイズ処理
            switch (currentEvent.type)
            {
                case EventType.MouseDown:
                    if (rect.Contains(currentEvent.mousePosition) && currentEvent.button == 0)
                    {
                        _isResizing = true;
                        currentEvent.Use();
                    }
                    break;

                case EventType.MouseDrag:
                    if (_isResizing)
                    {
                        // パネル左端 (originX) からの相対幅
                        _layerPanelWidth = currentEvent.mousePosition.x - originX;
                        currentEvent.Use();
                        Repaint();
                    }
                    break;

                case EventType.MouseUp:
                    if (_isResizing)
                    {
                        _isResizing = false;
                        currentEvent.Use();
                    }
                    break;
            }
        }

        /// <summary>
        /// レイヤーリストを「上が最上層」になるよう逆順に描画する
        /// (PSDFile.Layers は index 0 = 最下層)。depth は入れ子カードの深さ。
        /// </summary>
        void DrawLayerListTopDown(List<PSDLayer> layers, int depth)
        {
            if (layers == null) return;
            for (int i = layers.Count - 1; i >= 0; i--)
                DrawLayerNode(layers[i], depth);
        }

        // レイヤーコントロール行の縦間隔 (詰まり防止)
        const float RowGap = 5f;

        // 横並びコントロールの標準高さ。ラベル/入力/ボタンを同一高さに揃えて縦中央そろえし、
        // かつ行が縦方向に過剰に伸びる (stretchHeight 由来) のを防ぐ。
        internal const float RowH = 20f;

        /// <summary>コントロール 1 行分の縦余白を空ける。</summary>
        static void RowSpace() => EditorGUILayout.Space(RowGap);

        /// <summary>▸ / ▾ の展開ボタン。押されたら反転した状態を返す。</summary>
        bool DrawFoldoutButton(bool expanded)
        {
            if (GUILayout.Button(expanded ? "▾" : "▸", PSDEditorTheme.FoldoutButtonStyle,
                                 GUILayout.Width(18), GUILayout.Height(RowH)))
                return !expanded;
            return expanded;
        }

        /// <summary>
        /// テーマ管理の折りたたみ見出し (EditorGUILayout.Foldout の置き換え)。
        /// ライト/ダーク両モードで文字色が破綻しないよう ▸/▾ ボタン + クリック可能ラベルで構成する。
        /// </summary>
        bool DrawSectionFoldout(string label, bool expanded, int indent)
        {
            bool result = expanded;
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(indent * IndentWidth + 18f);
            if (GUILayout.Button(expanded ? "▾" : "▸", PSDEditorTheme.FoldoutButtonStyle,
                                 GUILayout.Width(18), GUILayout.Height(RowH)))
                result = !expanded;
            if (GUILayout.Button(label, PSDEditorTheme.FoldoutLabelStyle, GUILayout.ExpandWidth(true),
                                 GUILayout.Height(RowH)))
                result = !expanded;
            EditorGUILayout.EndHorizontal();
            return result;
        }

        /// <summary>表示トグル (目のチェックボックス相当)。変更時に再合成。</summary>
        void DrawVisibilityToggle(PSDLayer layer)
        {
            bool newVisible = GUILayout.Toggle(layer.UIVisible, "",
                                               GUILayout.Width(16), GUILayout.Height(RowH));
            if (newVisible != layer.UIVisible)
            {
                layer.UIVisible   = newVisible;
                _needsRecomposite = true;
            }
        }

        void DrawLayerNode(PSDLayer layer, int depth)
        {
            bool isGroup = layer.Children != null;
            if (isGroup) DrawGroupNode(layer, depth);
            else         DrawLeafNode(layer);
        }

        /// <summary>グループ: Surface2 のタイトル帯 + 入れ子ボディを持つカード。</summary>
        void DrawGroupNode(PSDLayer layer, int depth)
        {
            EditorGUILayout.BeginVertical(PSDEditorTheme.LayerGroupOuterStyle);

            // ── タイトル帯 ──
            EditorGUILayout.BeginHorizontal(PSDEditorTheme.LayerGroupHeaderStyle);
            layer.IsExpanded = DrawFoldoutButton(layer.IsExpanded);
            DrawVisibilityToggle(layer);
            string label = BuildLayerLabel(layer, true);
            GUILayout.Label(new GUIContent(label, label), PSDEditorTheme.LayerNameStyle,
                            GUILayout.ExpandWidth(true), GUILayout.Height(RowH));
            DrawBlendModePopup(layer, true);
            EditorGUILayout.EndHorizontal();

            // ── ボディ (表示中のみ) ──
            if (layer.UIVisible)
            {
                EditorGUILayout.BeginVertical(PSDEditorTheme.LayerGroupBodyStyle);

                DrawOpacitySlider(layer, 0);

                if (layer.IsExpanded)
                    DrawLayerListTopDown(layer.Children, depth + 1);

                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.EndVertical();
        }

        /// <summary>通常レイヤー: 1 枚のカード。</summary>
        void DrawLeafNode(PSDLayer layer)
        {
            EditorGUILayout.BeginVertical(PSDEditorTheme.LayerLeafCardStyle);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(18f);   // グループの折りたたみボタン位置に合わせる
            DrawVisibilityToggle(layer);
            string label = BuildLayerLabel(layer, false);
            GUILayout.Label(new GUIContent(label, label), PSDEditorTheme.LayerNameStyle,
                            GUILayout.ExpandWidth(true), GUILayout.Height(RowH));
            DrawBlendModePopup(layer, false);
            EditorGUILayout.EndHorizontal();

            if (layer.UIVisible)
                DrawLayerControls(layer, 0);

            EditorGUILayout.EndVertical();
        }

        /// <summary>種別プレフィックス付きのレイヤー名を組み立てる。</summary>
        static string BuildLayerLabel(PSDLayer layer, bool isGroup)
        {
            string prefix = "";

            if (isGroup)
                prefix += "[フォルダ] ";
            if (layer.IsClipping)
                prefix += "[クリップ] ";
            if (layer.HasMask)
                prefix += layer.MaskIsDisabled ? "[マスク無効] " : "[マスク] ";

            if (layer.Adjustment != null && layer.Adjustment.HasSolidColor)
                prefix += "[SoCo] ";
            else if (!isGroup && layer.IsAdjustmentLayer &&
                     layer.Adjustment != null &&
                     (layer.Adjustment.HasBrightnessContrast || layer.Adjustment.HasHueSaturation ||
                      layer.Adjustment.HasInvert || layer.Adjustment.HasThreshold || layer.Adjustment.HasPosterize ||
                      layer.Adjustment.HasLevels || layer.Adjustment.HasCurves))
                prefix += "[調整] ";

            if (layer.Effects != null && layer.Effects.HasColorOverlay)
                prefix += "[CO] ";

            return prefix + layer.Name;
        }

        /// <summary>非グループレイヤーの詳細コントロール。</summary>
        void DrawLayerControls(PSDLayer layer, int indent)
        {
            // 不透明度
            DrawOpacitySlider(layer, indent);

            // 明るさ・コントラスト (brit / CgEd)
            if (layer.Adjustment != null && layer.Adjustment.HasBrightnessContrast)
            {
                float nb = IndentedSlider("明るさ", layer.UIBrightness, -150f, 150f, indent);
                float nc = IndentedSlider("ｺﾝﾄﾗｽﾄ", layer.UIContrast,    -50f, 100f, indent);
                if (!Mathf.Approximately(nb, layer.UIBrightness) ||
                    !Mathf.Approximately(nc, layer.UIContrast))
                {
                    layer.UIBrightness = nb;
                    layer.UIContrast   = nc;
                    _needsRecomposite  = true;
                }
            }

            // 色相・彩度・明度 (hue2)
            if (layer.Adjustment != null && layer.Adjustment.HasHueSaturation)
            {
                float nh = IndentedSlider("色相", layer.UIHue,        -180f, 180f, indent);
                float ns = IndentedSlider("彩度", layer.UISaturation, -100f, 100f, indent);
                float nl = IndentedSlider("明度", layer.UILightness,  -100f, 100f, indent);
                if (!Mathf.Approximately(nh, layer.UIHue) ||
                    !Mathf.Approximately(ns, layer.UISaturation) ||
                    !Mathf.Approximately(nl, layer.UILightness))
                {
                    layer.UIHue        = nh;
                    layer.UISaturation = ns;
                    layer.UILightness  = nl;
                    _needsRecomposite  = true;
                }
                DrawColorizeToggle(layer, indent);
            }

            // 階調反転 (invr)
            if (layer.Adjustment != null && layer.Adjustment.HasInvert)
                DrawInvertToggle(layer, indent);

            // しきい値 (thrs)
            if (layer.Adjustment != null && layer.Adjustment.HasThreshold)
                DrawThresholdControls(layer, indent);

            // ポスタリゼーション (post)
            if (layer.Adjustment != null && layer.Adjustment.HasPosterize)
                DrawPosterizeControls(layer, indent);

            // レベル補正 (levl)
            if (layer.Adjustment != null && layer.Adjustment.HasLevels)
                DrawLevelsControls(layer, indent);

            // トーンカーブ (curv)
            if (layer.Adjustment != null && layer.Adjustment.HasCurves)
                DrawCurveControls(layer, indent);

            // ベタ塗りカラー (SoCo)
            if (layer.Adjustment != null && layer.Adjustment.HasSolidColor)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(indent * IndentWidth + 18f);
                GUILayout.Label("塗り色", PSDEditorTheme.ControlLabelStyle,
                                GUILayout.Width(48), GUILayout.Height(RowH));
                Color nc = EditorGUILayout.ColorField(GUIContent.none, layer.Adjustment.SolidColor,
                                                      GUILayout.Height(RowH));
                EditorGUILayout.EndHorizontal();
                RowSpace();
                if (nc != layer.Adjustment.SolidColor)
                {
                    layer.Adjustment.SolidColor = nc;
                    _needsRecomposite = true;
                }
            }

            // 全ピクセルレイヤー向け: 色調補正 + グラデーションマップ (非破壊)
            // (parse 済み調整レイヤー / SoCo は上の専用 UI が担当するため除外)
            if (!layer.IsAdjustmentLayer)
                DrawAdjustmentFoldout(layer, indent);

            // 色域選択マスク (ピクセルを持つレイヤーのみ)
            if (layer.Texture != null)
                DrawColorRangeMaskControls(layer, indent);

            // マスクの有効/無効表示
            if (layer.HasMask)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(indent * IndentWidth + 18f);
                GUILayout.Label("マスク: " + (layer.MaskIsDisabled ? "無効" : "有効"),
                                PSDEditorTheme.ControlLabelStyle, GUILayout.Height(RowH));
                EditorGUILayout.EndHorizontal();
                RowSpace();
            }
        }

        void DrawOpacitySlider(PSDLayer layer, int indent)
        {
            float newOpacity = IndentedSlider("不透明度", layer.UIOpacity, 0f, 1f, indent);
            if (!Mathf.Approximately(newOpacity, layer.UIOpacity))
            {
                layer.UIOpacity   = newOpacity;
                _needsRecomposite = true;
            }
        }

        /// <summary>ブレンドモードの短縮ラベル。</summary>
        static string BlendModeShortLabel(BlendMode mode)
        {
            switch (mode)
            {
                case BlendMode.Normal:       return "Norm";
                case BlendMode.Multiply:     return "Mul";
                case BlendMode.Screen:       return "Scrn";
                case BlendMode.Overlay:      return "Over";
                case BlendMode.Dissolve:     return "Diss";
                case BlendMode.Darken:       return "Dark";
                case BlendMode.ColorBurn:    return "CBrn";
                case BlendMode.LinearBurn:   return "LBrn";
                case BlendMode.DarkerColor:  return "DkCl";
                case BlendMode.Lighten:      return "Lite";
                case BlendMode.ColorDodge:   return "CDdg";
                case BlendMode.LinearDodge:  return "Add";
                case BlendMode.LighterColor: return "LtCl";
                case BlendMode.SoftLight:    return "SLit";
                case BlendMode.HardLight:    return "HLit";
                case BlendMode.VividLight:   return "VLit";
                case BlendMode.LinearLight:  return "LLit";
                case BlendMode.PinLight:     return "PLit";
                case BlendMode.HardMix:      return "HMix";
                case BlendMode.Difference:   return "Diff";
                case BlendMode.Exclusion:    return "Excl";
                case BlendMode.Subtract:     return "Sub";
                case BlendMode.Divide:       return "Div";
                case BlendMode.Hue:          return "Hue";
                case BlendMode.Saturation:   return "Sat";
                case BlendMode.Color:        return "Colr";
                case BlendMode.Luminosity:   return "Lum";
                case BlendMode.PassThrough:  return "Pass";
                default:                     return "?";
            }
        }

        // ── ブレンドモード Popup ────────────────────────────────────────────

        // Popup の候補 (Unknown は除外)。グループのみ PassThrough を先頭に含める。
        static readonly BlendMode[] _blendModesNormal =
        {
            BlendMode.Normal,      BlendMode.Multiply,    BlendMode.Screen,
            BlendMode.Overlay,     BlendMode.Dissolve,    BlendMode.Darken,
            BlendMode.ColorBurn,   BlendMode.LinearBurn,  BlendMode.DarkerColor,
            BlendMode.Lighten,     BlendMode.ColorDodge,  BlendMode.LinearDodge,
            BlendMode.LighterColor,BlendMode.SoftLight,   BlendMode.HardLight,
            BlendMode.VividLight,  BlendMode.LinearLight, BlendMode.PinLight,
            BlendMode.HardMix,     BlendMode.Difference,  BlendMode.Exclusion,
            BlendMode.Subtract,    BlendMode.Divide,      BlendMode.Hue,
            BlendMode.Saturation,  BlendMode.Color,       BlendMode.Luminosity,
        };
        static readonly BlendMode[] _blendModesGroup = BuildGroupBlendModes();
        static string[] _blendLabelsNormal;
        static string[] _blendLabelsGroup;

        static BlendMode[] BuildGroupBlendModes()
        {
            var list = new List<BlendMode> { BlendMode.PassThrough };
            list.AddRange(_blendModesNormal);
            return list.ToArray();
        }

        /// <summary>レイヤー/グループのブレンドモードを Popup で編集する。変更時に再合成を要求。</summary>
        void DrawBlendModePopup(PSDLayer layer, bool isGroup)
        {
            BlendMode[] modes  = isGroup ? _blendModesGroup : _blendModesNormal;
            string[]    labels = isGroup
                ? (_blendLabelsGroup  ?? (_blendLabelsGroup  = BuildBlendLabels(_blendModesGroup)))
                : (_blendLabelsNormal ?? (_blendLabelsNormal = BuildBlendLabels(_blendModesNormal)));

            BlendMode cur = isGroup ? layer.GroupBlendMode : layer.BlendMode;
            // Unknown 等で候補に無い場合は index 0 を仮表示 (ユーザー操作があるまで書き換えない)
            int curIndex = Mathf.Max(0, Array.IndexOf(modes, cur));

            int newIndex = EditorGUILayout.Popup(curIndex, labels,
                                                 GUILayout.Width(74), GUILayout.Height(RowH));
            if (newIndex != curIndex)
            {
                if (isGroup) layer.GroupBlendMode = modes[newIndex];
                else         layer.BlendMode      = modes[newIndex];
                _needsRecomposite = true;
            }
        }

        static string[] BuildBlendLabels(BlendMode[] modes)
        {
            var labels = new string[modes.Length];
            for (int i = 0; i < modes.Length; i++)
                labels[i] = BlendModeShortLabel(modes[i]);
            return labels;
        }
    }
}
