using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace PSDSimpleEditor
{
    // ── レイヤーパネル (左): ツリー描画・スプリッター・ブレンドモード Popup ──
    // ─── partial 見取り図 ───────────────────────────────────────────
    // 責務   : レイヤーパネル UI (左側) のツリー構造・各レイヤーの補正スライダー等の描画
    // 宣言   : RowGap, RowH
    // 参照   : _needsRecomposite (RW), _blendModesNormal (R), _blendModesGroup (R),
    //          _blendLabelsNormal (R), _blendLabelsGroup (R), _eyedropperTarget (RW)
    // 依存   : DrawAdjustmentGearMenu (.AdjustmentClipboard.cs), IndentedSlider (.Adjustments.cs),
    //          DrawColorizeToggle (.Adjustments.cs), ForEachCoTarget/AddClamped (.Selection.cs) 等
    // ────────────────────────────────────────────────────────────────
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
                prefix += PSDTranslation.Get("FolderPrefix", "[フォルダ] ");
            if (layer.IsClipping)
                prefix += PSDTranslation.Get("ClipPrefix", "[クリップ] ");
            if (layer.HasMask)
                prefix += layer.MaskIsDisabled ? PSDTranslation.Get("MaskDisabledPrefix", "[マスク無効] ") : PSDTranslation.Get("MaskPrefix", "[マスク] ");

            if (layer.Adjustment != null && layer.Adjustment.HasSolidColor)
                prefix += "[SoCo] ";
            else if (layer.Adjustment != null && layer.Adjustment.HasGradientFill)
                prefix += PSDTranslation.Get("GradFillPrefix", "[グラデ塗り] ");
            else if (!isGroup && layer.IsAdjustmentLayer &&
                     layer.Adjustment != null &&
                     (layer.Adjustment.HasBrightnessContrast || layer.Adjustment.HasHueSaturation ||
                      layer.Adjustment.HasInvert || layer.Adjustment.HasThreshold || layer.Adjustment.HasPosterize ||
                      layer.Adjustment.HasLevels || layer.Adjustment.HasCurves ||
                      layer.Adjustment.HasGradientMap || layer.Adjustment.HasColorBalance))
                prefix += PSDTranslation.Get("AdjustPrefix", "[調整] ");

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
                float nb = IndentedSlider(new GUIContent(PSDTranslation.Get("Brightness", "明るさ"), PSDTranslation.Get("BrightnessTooltip", "レイヤーの明るさを調整します（-150 〜 150）。")), layer.UI.Brightness, -150f, 150f, indent);
                float nc = IndentedSlider(new GUIContent(PSDTranslation.Get("Contrast", "ｺﾝﾄﾗｽﾄ"), PSDTranslation.Get("ContrastTooltip", "レイヤーのコントラスト（明暗差）を調整します（-50 〜 100）。")), layer.UI.Contrast,    -50f, 100f, indent);
                if (!Mathf.Approximately(nb, layer.UI.Brightness) ||
                    !Mathf.Approximately(nc, layer.UI.Contrast))
                {
                    float db = nb - layer.UI.Brightness;
                    float dc = nc - layer.UI.Contrast;
                    RegisterUndo("Modify Brightness/Contrast");
                    layer.UI.Brightness = nb;
                    layer.UI.Contrast   = nc;
                    ForEachCoTarget(layer, t => {
                        t.UI.Brightness = AddClamped(t.UI.Brightness, db, -150f, 150f);
                        t.UI.Contrast   = AddClamped(t.UI.Contrast,   dc,  -50f, 100f);
                    });
                    SaveStatesToSerialized();
                    MarkDirty();
                }
            }

            // 色相・彩度・明度 (hue2)
            if (layer.Adjustment != null && layer.Adjustment.HasHueSaturation)
            {
                float nh = IndentedSlider(new GUIContent(PSDTranslation.Get("Hue", "色相"), PSDTranslation.Get("HueTooltip", "レイヤーの色相（カラー）を調整します（-180度 〜 180度）。")), layer.UI.Hue,        -180f, 180f, indent);
                float ns = IndentedSlider(new GUIContent(PSDTranslation.Get("Saturation", "彩度"), PSDTranslation.Get("SaturationTooltip", "レイヤーの彩度（鮮やかさ）を調整します（-100 〜 100）。")), layer.UI.Saturation, -100f, 100f, indent);
                float nl = IndentedSlider(new GUIContent(PSDTranslation.Get("Lightness", "明度"), PSDTranslation.Get("LightnessTooltip", "レイヤーの明度を調整します（-100 〜 100）。")), layer.UI.Lightness,  -100f, 100f, indent);
                if (!Mathf.Approximately(nh, layer.UI.Hue) ||
                    !Mathf.Approximately(ns, layer.UI.Saturation) ||
                    !Mathf.Approximately(nl, layer.UI.Lightness))
                {
                    float dh = nh - layer.UI.Hue;
                    float ds = ns - layer.UI.Saturation;
                    float dl = nl - layer.UI.Lightness;
                    RegisterUndo("Modify Hue/Saturation/Lightness");
                    layer.UI.Hue        = nh;
                    layer.UI.Saturation = ns;
                    layer.UI.Lightness  = nl;
                    ForEachCoTarget(layer, t => {
                        t.UI.Hue        = AddClamped(t.UI.Hue,        dh, -180f, 180f);
                        t.UI.Saturation = AddClamped(t.UI.Saturation, ds, -100f, 100f);
                        t.UI.Lightness  = AddClamped(t.UI.Lightness,  dl, -100f, 100f);
                    });
                    SaveStatesToSerialized();
                    MarkDirty();
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
                GUILayout.Label(new GUIContent(PSDTranslation.Get("SolidColor", "塗り色"), PSDTranslation.Get("SolidColorTooltip", "ベタ塗りレイヤーの塗りつぶし色を設定します。")), PSDEditorTheme.ControlLabelStyle,
                                GUILayout.Width(48), GUILayout.Height(RowH));
                Color nc = EditorGUILayout.ColorField(new GUIContent("", PSDTranslation.Get("SolidColorTooltip", "ベタ塗りレイヤーの塗りつぶし色を設定します。")), layer.Adjustment.SolidColor,
                                                      GUILayout.Width(80), GUILayout.Height(RowH));
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
                RowSpace();
                if (nc != layer.Adjustment.SolidColor)
                {
                    RegisterUndo("Modify Solid Color");
                    layer.Adjustment.SolidColor = nc;
                    SaveStatesToSerialized();
                    MarkDirty();
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
                GUILayout.Label(PSDTranslation.Get("MaskStatusLabel", "マスク: ") + (layer.MaskIsDisabled ? PSDTranslation.Get("MaskStatusDisabled", "無効") : PSDTranslation.Get("MaskStatusEnabled", "有効")),
                                PSDEditorTheme.ControlLabelStyle, GUILayout.Height(RowH));
                EditorGUILayout.EndHorizontal();
                RowSpace();
            }
        }

        void DrawOpacitySlider(PSDLayer layer, int indent)
        {
            float newOpacity = IndentedSlider(new GUIContent(PSDTranslation.Get("Opacity", "不透明度"), PSDTranslation.Get("OpacityTooltip", "レイヤーの不透明度（アルファ）を 0.0（完全透明）から 1.0（完全不透明）の間で調整します。")), layer.UI.Opacity, 0f, 1f, indent);
            if (!Mathf.Approximately(newOpacity, layer.UI.Opacity))
            {
                RegisterUndo("Change Opacity");
                layer.UI.Opacity   = newOpacity;
                SaveStatesToSerialized();
                MarkDirty();
            }
        }

        /// <summary>ブレンドモードの短縮ラベル。</summary>
        static string BlendModeShortLabel(BlendMode mode)
        {
            switch (mode)
            {
                case BlendMode.Normal:       return PSDTranslation.Get("BlendNormal", "通常");
                case BlendMode.Multiply:     return PSDTranslation.Get("BlendMultiply", "乗算");
                case BlendMode.Screen:       return PSDTranslation.Get("BlendScreen", "スクリーン");
                case BlendMode.Overlay:      return PSDTranslation.Get("BlendOverlay", "オーバーレイ");
                case BlendMode.Dissolve:     return PSDTranslation.Get("BlendDissolve", "ディゾルブ");
                case BlendMode.Darken:       return PSDTranslation.Get("BlendDarken", "比較(暗)");
                case BlendMode.ColorBurn:    return PSDTranslation.Get("BlendColorBurn", "焼き込み(カラー)");
                case BlendMode.LinearBurn:   return PSDTranslation.Get("BlendLinearBurn", "焼き込み(リニア)");
                case BlendMode.DarkerColor:  return PSDTranslation.Get("BlendDarkerColor", "カラー比較(暗)");
                case BlendMode.Lighten:      return PSDTranslation.Get("BlendLighten", "比較(明)");
                case BlendMode.ColorDodge:   return PSDTranslation.Get("BlendColorDodge", "覆い焼き(カラー)");
                case BlendMode.LinearDodge:  return PSDTranslation.Get("BlendLinearDodge", "覆い焼き(リニア)");
                case BlendMode.LighterColor: return PSDTranslation.Get("BlendLighterColor", "カラー比較(明)");
                case BlendMode.SoftLight:    return PSDTranslation.Get("BlendSoftLight", "ソフトライト");
                case BlendMode.HardLight:    return PSDTranslation.Get("BlendHardLight", "ハードライト");
                case BlendMode.VividLight:   return PSDTranslation.Get("BlendVividLight", "ビビッドライト");
                case BlendMode.LinearLight:  return PSDTranslation.Get("BlendLinearLight", "リニアライト");
                case BlendMode.PinLight:     return PSDTranslation.Get("BlendPinLight", "ピンライト");
                case BlendMode.HardMix:      return PSDTranslation.Get("BlendHardMix", "ハードミックス");
                case BlendMode.Difference:   return PSDTranslation.Get("BlendDifference", "差の絶対値");
                case BlendMode.Exclusion:    return PSDTranslation.Get("BlendExclusion", "除外");
                case BlendMode.Subtract:     return PSDTranslation.Get("BlendSubtract", "減算");
                case BlendMode.Divide:       return PSDTranslation.Get("BlendDivide", "除算");
                case BlendMode.Hue:          return PSDTranslation.Get("BlendHue", "色相");
                case BlendMode.Saturation:   return PSDTranslation.Get("BlendSaturation", "彩度");
                case BlendMode.Color:        return PSDTranslation.Get("BlendColor", "カラー");
                case BlendMode.Luminosity:   return PSDTranslation.Get("BlendLuminosity", "輝度");
                case BlendMode.PassThrough:  return PSDTranslation.Get("BlendPassThrough", "通過");
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
