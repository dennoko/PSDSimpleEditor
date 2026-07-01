using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace PSDSimpleEditor
{
    // ── レイヤーパネル (左): ツリー描画・スプリッター・ブレンドモード Popup ──
    public partial class PSDSimpleEditorWindow
    {
        void DrawLayerPanel(float panelHeight)
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(_layerPanelWidth),
                                          GUILayout.Height(panelHeight));
            GUILayout.Label("レイヤー", EditorStyles.boldLabel);

            _layerScroll = EditorGUILayout.BeginScrollView(_layerScroll,
                                                           GUILayout.ExpandHeight(true));
            DrawLayerListTopDown(_psdFile.Layers, 0);
            EditorGUILayout.EndScrollView();

            EditorGUILayout.EndVertical();
        }

        /// <summary>レイヤーパネルとプレビューパネルの境界をドラッグで調整するためのスプリッターを描画する。</summary>
        void DrawSplitter(float height)
        {
            // 8px 幅のホバー/インタラクション領域を確保
            Rect rect = GUILayoutUtility.GetRect(8f, height, GUILayout.Width(8f), GUILayout.Height(height));

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

            // スプリッターの背景/線の描画色を決定
            Color dividerColor;
            if (_isResizing)
            {
                dividerColor = new Color(0.24f, 0.48f, 0.9f); // ドラッグ中のアクセントブルー
            }
            else if (_isSplitterHovered)
            {
                dividerColor = EditorGUIUtility.isProSkin ? new Color(0.4f, 0.4f, 0.4f) : new Color(0.7f, 0.7f, 0.7f); // ホバー時
            }
            else
            {
                dividerColor = EditorGUIUtility.isProSkin ? new Color(0.18f, 0.18f, 0.18f) : new Color(0.65f, 0.65f, 0.65f); // 通常時
            }

            // 視覚的に美しい 2px 幅の中央線として描画
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
                        _layerPanelWidth = currentEvent.mousePosition.x;
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
        /// (PSDFile.Layers は index 0 = 最下層)。
        /// </summary>
        void DrawLayerListTopDown(List<PSDLayer> layers, int indent)
        {
            if (layers == null) return;
            for (int i = layers.Count - 1; i >= 0; i--)
                DrawLayerNode(layers[i], indent);
        }

        void DrawLayerNode(PSDLayer layer, int indent)
        {
            bool isGroup = layer.Children != null;

            EditorGUILayout.BeginVertical(GUI.skin.box);

            // ── ヘッダ行: [折りたたみ] [表示] 名前 ... ブレンド ──
            EditorGUILayout.BeginHorizontal();

            if (indent > 0)
                GUILayout.Space(indent * IndentWidth);

            if (isGroup)
            {
                // Foldout は ExpandWidth しないよう固定幅で配置
                layer.IsExpanded = EditorGUILayout.Foldout(layer.IsExpanded, GUIContent.none, true);
            }
            else
            {
                GUILayout.Space(14f);
            }

            // 表示トグル → 再合成
            bool newVisible = EditorGUILayout.Toggle(layer.UIVisible, GUILayout.Width(16));
            if (newVisible != layer.UIVisible)
            {
                layer.UIVisible   = newVisible;
                _needsRecomposite = true;
            }

            // 名前 + 種別プレフィックス
            string label = BuildLayerLabel(layer, isGroup);
            GUILayout.Label(new GUIContent(label, label), GUILayout.ExpandWidth(true));

            // ブレンドモード (編集可能な Popup)
            DrawBlendModePopup(layer, isGroup);

            EditorGUILayout.EndHorizontal();

            // ── 詳細 (表示中レイヤーのみ) ──
            if (layer.UIVisible)
            {
                if (isGroup)
                {
                    // グループ自身の不透明度 (PassThrough 以外で合成に効く)
                    DrawOpacitySlider(layer, indent);

                    if (layer.IsExpanded)
                        DrawLayerListTopDown(layer.Children, indent + 1);
                }
                else
                {
                    DrawLayerControls(layer, indent);
                }
            }

            EditorGUILayout.EndVertical();
            GUILayout.Space(1);
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
                      layer.Adjustment.HasInvert || layer.Adjustment.HasThreshold || layer.Adjustment.HasPosterize))
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

            // ベタ塗りカラー (SoCo)
            if (layer.Adjustment != null && layer.Adjustment.HasSolidColor)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(indent * IndentWidth + 18f);
                GUILayout.Label("塗り色", EditorStyles.miniLabel, GUILayout.Width(44));
                Color nc = EditorGUILayout.ColorField(layer.Adjustment.SolidColor);
                EditorGUILayout.EndHorizontal();
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
                                EditorStyles.miniLabel);
                EditorGUILayout.EndHorizontal();
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

            int newIndex = EditorGUILayout.Popup(curIndex, labels, GUILayout.Width(74));
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
