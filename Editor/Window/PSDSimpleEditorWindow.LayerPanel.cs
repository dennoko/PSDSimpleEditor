using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace PSDSimpleEditor
{
    // ── レイヤーパネル (左): ツリー描画・スプリッター・ブレンドモード Popup ──
    public partial class PSDSimpleEditorWindow
    {




        // レイヤーコントロール行の縦間隔 (詰まり防止)
        const float RowGap = 5f;

        // 横並びコントロールの標準高さ。ラベル/入力/ボタンを同一高さに揃えて縦中央そろえし、
        // かつ行が縦方向に過剰に伸びる (stretchHeight 由来) のを防ぐ。
        internal const float RowH = 20f;

        /// <summary>コントロール 1 行分の縦余白を空ける。</summary>
        static void RowSpace() => EditorGUILayout.Space(RowGap);

        /// <summary>
        /// テーマ管理の折りたたみ見出し (EditorGUILayout.Foldout の置き換え)。
        /// ライト/ダーク両モードで文字色が破綻しないよう ▸/▾ ボタン + クリック可能ラベルで構成する。
        /// </summary>
        bool DrawSectionFoldout(GUIContent label, bool expanded, int indent, PSDLayer layer, ClipboardKind kind)
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
            DrawAdjustmentGearMenu(kind, layer);
            EditorGUILayout.EndHorizontal();
            return result;
        }

        bool DrawSectionFoldout(string label, bool expanded, int indent, PSDLayer layer, ClipboardKind kind)
        {
            return DrawSectionFoldout(new GUIContent(label), expanded, indent, layer, kind);
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
                      layer.Adjustment.HasLevels || layer.Adjustment.HasCurves ||
                      layer.Adjustment.HasGradientMap || layer.Adjustment.HasColorBalance))
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
                float nb = IndentedSlider(new GUIContent("明るさ", "レイヤーの明るさを調整します（-150 〜 150）。"), layer.UIBrightness, -150f, 150f, indent);
                float nc = IndentedSlider(new GUIContent("ｺﾝﾄﾗｽﾄ", "レイヤーのコントラスト（明暗差）を調整します（-50 〜 100）。"), layer.UIContrast,    -50f, 100f, indent);
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
                float nh = IndentedSlider(new GUIContent("色相", "レイヤーの色相（カラー）を調整します（-180度 〜 180度）。"), layer.UIHue,        -180f, 180f, indent);
                float ns = IndentedSlider(new GUIContent("彩度", "レイヤーの彩度（鮮やかさ）を調整します（-100 〜 100）。"), layer.UISaturation, -100f, 100f, indent);
                float nl = IndentedSlider(new GUIContent("明度", "レイヤーの明度を調整します（-100 〜 100）。"), layer.UILightness,  -100f, 100f, indent);
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

            // カラーバランス (blnc)
            if (layer.Adjustment != null && layer.Adjustment.HasColorBalance)
                DrawColorBalanceControls(layer, indent);

            // グラデーションマップ (grdm)
            if (layer.Adjustment != null && layer.Adjustment.HasGradientMap)
                DrawGradientMapControls(layer, indent);

            // ベタ塗りカラー (SoCo)
            if (layer.Adjustment != null && layer.Adjustment.HasSolidColor)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(indent * IndentWidth + 18f);
                GUILayout.Label(new GUIContent("塗り色", "ベタ塗りレイヤーの塗りつぶし色を設定します。"), PSDEditorTheme.ControlLabelStyle,
                                GUILayout.Width(48), GUILayout.Height(RowH));
                Color nc = EditorGUILayout.ColorField(new GUIContent("", "ベタ塗りレイヤーの塗りつぶし色を設定します。"), layer.Adjustment.SolidColor,
                                                      GUILayout.Width(80), GUILayout.Height(RowH));
                GUILayout.FlexibleSpace();
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
            float newOpacity = IndentedSlider(new GUIContent("不透明度", "レイヤーの不透明度（アルファ）を 0.0（完全透明）から 1.0（完全不透明）の間で調整します。"), layer.UIOpacity, 0f, 1f, indent);
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



        static string[] BuildBlendLabels(BlendMode[] modes)
        {
            var labels = new string[modes.Length];
            for (int i = 0; i < modes.Length; i++)
                labels[i] = BlendModeShortLabel(modes[i]);
            return labels;
        }
    }
}
