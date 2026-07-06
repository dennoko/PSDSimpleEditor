using System;
using UnityEngine;
using UnityEditor;

namespace PSDSimpleEditor
{
    // ── 色調補正 + グラデーションマップ + 画像クリップ (非破壊・全ピクセルレイヤー) ──
    // ─── partial 見取り図 ───────────────────────────────────────────
    // 責務   : 非破壊色調補正・トーンカーブ・グラデーションマップ・画像クリップ合成のパラメータ制御 (UI)
    // 宣言   : なし
    // 参照   : _needsRecomposite (RW), _blendModesNormal (R), _blendLabelsNormal (R)
    // 依存   : DrawSectionFoldout (.LayerPanel.cs), DrawAdjustmentGearMenu (.AdjustmentClipboard.cs),
    //          RowSpace (.LayerPanel.cs), AdjustmentLutBaker (LUT ベイク処理)
    // ────────────────────────────────────────────────────────────────
    public partial class PSDSimpleEditorWindow
    {
        /// <summary>「色調補正」フォールドアウト。明るさ/コントラスト/色相/彩度/明度 + グラデーションマップ。</summary>
        void DrawAdjustmentFoldout(PSDLayer layer, int indent)
        {
            layer.UI.AdjustExpanded = DrawSectionFoldout(PSDTranslation.Get("AdjustmentSection", "色調補正"), layer.UI.AdjustExpanded, indent, layer, ClipboardKind.FullAdjustmentSection);
            if (!layer.UI.AdjustExpanded) { RowSpace(); return; }
            RowSpace();

            int ci = indent + 1;

            // 明るさ・コントラスト・色相・彩度・明度 (シェーダーの正規化除数に合わせた範囲)
            float nb = IndentedSlider(new GUIContent(PSDTranslation.Get("Brightness", "明るさ"), PSDTranslation.Get("BrightnessTooltip", "レイヤーの明るさを調整します（-150 〜 150）。")),  layer.UI.Brightness, -150f, 150f, ci);
            float nc = IndentedSlider(new GUIContent(PSDTranslation.Get("Contrast", "ｺﾝﾄﾗｽﾄ"), PSDTranslation.Get("ContrastTooltip", "レイヤーのコントラスト（明暗差）を調整します（-50 〜 100）。")),  layer.UI.Contrast,   -50f, 100f, ci);
            float nh = IndentedSlider(new GUIContent(PSDTranslation.Get("Hue", "色相"), PSDTranslation.Get("HueTooltip", "レイヤーの色相（カラー）を調整します（-180度 〜 180度）。")),    layer.UI.Hue,        -180f, 180f, ci);
            float ns = IndentedSlider(new GUIContent(PSDTranslation.Get("Saturation", "彩度"), PSDTranslation.Get("SaturationTooltip", "レイヤーの彩度（鮮やかさ）を調整します（-100 〜 100）。")),    layer.UI.Saturation, -100f, 100f, ci);
            float nl = IndentedSlider(new GUIContent(PSDTranslation.Get("Lightness", "明度"), PSDTranslation.Get("LightnessTooltip", "レイヤーの明度を調整します（-100 〜 100）。")),    layer.UI.Lightness,  -100f, 100f, ci);
            if (!Mathf.Approximately(nb, layer.UI.Brightness) ||
                !Mathf.Approximately(nc, layer.UI.Contrast)   ||
                !Mathf.Approximately(nh, layer.UI.Hue)        ||
                !Mathf.Approximately(ns, layer.UI.Saturation) ||
                !Mathf.Approximately(nl, layer.UI.Lightness))
            {
                layer.UI.Brightness = nb;
                layer.UI.Contrast   = nc;
                layer.UI.Hue        = nh;
                layer.UI.Saturation = ns;
                layer.UI.Lightness  = nl;
                MarkDirty();
            }
            DrawColorizeToggle(layer, ci);
            DrawInvertToggle(layer, ci);
            DrawThresholdControls(layer, ci);
            DrawPosterizeControls(layer, ci);
            DrawLevelsControls(layer, ci);
            DrawCurveControls(layer, ci);
            DrawColorBalanceControls(layer, ci);

            DrawGradientMapControls(layer, ci);
            DrawImageClipControls(layer, ci);
        }

        /// <summary>「着色」トグル。ON で絶対値の色相・彩度を強制し、白黒 (彩度0) にも色が乗る。</summary>
        void DrawColorizeToggle(PSDLayer layer, int indent)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(indent * IndentWidth + 18f);
            bool en = EditorGUILayout.ToggleLeft(new GUIContent(PSDTranslation.Get("Colorize", "着色 (白黒にも色を入れる)"), PSDTranslation.Get("ColorizeTooltip", "白黒（無彩色）の領域にも色相・彩度を適用して着色できるようにします。")), layer.UI.Colorize,
                                                 GUILayout.Height(RowH));
            GUILayout.FlexibleSpace();
            DrawAdjustmentGearMenu(ClipboardKind.Colorize, layer);
            EditorGUILayout.EndHorizontal();
            RowSpace();
            if (en != layer.UI.Colorize)
            {
                layer.UI.Colorize  = en;
                MarkDirty();
            }
        }

        /// <summary>「階調反転」トグル (非破壊。全ピクセルレイヤーに適用可)。</summary>
        void DrawInvertToggle(PSDLayer layer, int indent)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(indent * IndentWidth + 18f);
            bool en = EditorGUILayout.ToggleLeft(new GUIContent(PSDTranslation.Get("Invert", "階調反転"), PSDTranslation.Get("InvertTooltip", "レイヤーの色のRGB値を反転します。")), layer.UI.Invert, GUILayout.Height(RowH));
            GUILayout.FlexibleSpace();
            DrawAdjustmentGearMenu(ClipboardKind.Invert, layer);
            EditorGUILayout.EndHorizontal();
            RowSpace();
            if (en != layer.UI.Invert)
            {
                layer.UI.Invert    = en;
                MarkDirty();
            }
        }

        /// <summary>「しきい値」有効トグル + レベルスライダー (非破壊。全ピクセルレイヤーに適用可)。</summary>
        void DrawThresholdControls(PSDLayer layer, int indent)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(indent * IndentWidth + 18f);
            bool en = EditorGUILayout.ToggleLeft(new GUIContent(PSDTranslation.Get("Threshold", "しきい値"), PSDTranslation.Get("ThresholdTooltip", "画像を白と黒の2階調に変換する機能を有効にします。")), layer.UI.ThresholdEnabled, GUILayout.Height(RowH));
            GUILayout.FlexibleSpace();
            DrawAdjustmentGearMenu(ClipboardKind.Threshold, layer);
            EditorGUILayout.EndHorizontal();
            RowSpace();
            if (en != layer.UI.ThresholdEnabled)
            {
                layer.UI.ThresholdEnabled = en;
                MarkDirty();
            }
            if (!en) return;

            float nl = IndentedSlider(new GUIContent(PSDTranslation.Get("ThresholdLevel", "レベル"), PSDTranslation.Get("ThresholdLevelTooltip", "2階調に分ける基準値（0 〜 255）を設定します。")), layer.UI.ThresholdLevel, 0f, 255f, indent);
            if (!Mathf.Approximately(nl, layer.UI.ThresholdLevel))
            {
                layer.UI.ThresholdLevel = nl;
                MarkDirty();
            }
        }

        /// <summary>「ポスタリゼーション」有効トグル + 階調数スライダー (非破壊。全ピクセルレイヤーに適用可)。</summary>
        void DrawPosterizeControls(PSDLayer layer, int indent)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(indent * IndentWidth + 18f);
            bool en = EditorGUILayout.ToggleLeft(new GUIContent(PSDTranslation.Get("Posterize", "ポスタリゼーション"), PSDTranslation.Get("PosterizeTooltip", "画像の階調数を減らしてイラスト調（トーン減少）にする効果を有効にします。")), layer.UI.PosterizeEnabled,
                                                 GUILayout.Height(RowH));
            GUILayout.FlexibleSpace();
            DrawAdjustmentGearMenu(ClipboardKind.Posterize, layer);
            EditorGUILayout.EndHorizontal();
            RowSpace();
            if (en != layer.UI.PosterizeEnabled)
            {
                layer.UI.PosterizeEnabled = en;
                MarkDirty();
            }
            if (!en) return;

            float nl = IndentedSlider(new GUIContent(PSDTranslation.Get("PosterizeLevels", "階調数"), PSDTranslation.Get("PosterizeLevelsTooltip", "表現する階調数（2 〜 255）を設定します。")), layer.UI.PosterizeLevels, 2f, 255f, indent);
            if (!Mathf.Approximately(nl, layer.UI.PosterizeLevels))
            {
                layer.UI.PosterizeLevels = nl;
                MarkDirty();
            }
        }

        /// <summary>「レベル補正」有効トグル + 5 スライダー (非破壊)。</summary>
        void DrawLevelsControls(PSDLayer layer, int indent)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(indent * IndentWidth + 18f);
            bool en = EditorGUILayout.ToggleLeft(new GUIContent(PSDTranslation.Get("Levels", "レベル補正"), PSDTranslation.Get("LevelsTooltip", "ハイライト・シャドウや中間調の入力/出力レベルを調整して、画像の明暗のバランスを補正します。")), layer.UI.LevelsEnabled, GUILayout.Height(RowH));
            GUILayout.FlexibleSpace();
            DrawAdjustmentGearMenu(ClipboardKind.Levels, layer);
            EditorGUILayout.EndHorizontal();
            RowSpace();
            if (en != layer.UI.LevelsEnabled)
            {
                layer.UI.LevelsEnabled = en;
                MarkDirty();
            }
            if (!en) return;

            int ci = indent + 1;
            float nib = IndentedSlider(new GUIContent(PSDTranslation.Get("LevelsInputBlack", "入力シャドウ"), PSDTranslation.Get("LevelsInputBlackTooltip", "最も暗い部分をどの入力レベル（0〜255）から開始するかを設定します。")), layer.UI.LevelsInputBlack,  0f, 255f, ci);
            float niw = IndentedSlider(new GUIContent(PSDTranslation.Get("LevelsInputWhite", "入力ハイライト"), PSDTranslation.Get("LevelsInputWhiteTooltip", "最も明るい部分をどの入力レベル（0〜255）で終了するかを設定します。")), layer.UI.LevelsInputWhite,  0f, 255f, ci);
            float ng  = IndentedSlider(new GUIContent(PSDTranslation.Get("LevelsGamma", "ガンマ"), PSDTranslation.Get("LevelsGammaTooltip", "中間調の明るさ（ガンマ値、0.01〜9.99）を調整します。1.0が基準です。")),        layer.UI.LevelsGamma,       0.01f, 9.99f, ci);
            float nob = IndentedSlider(new GUIContent(PSDTranslation.Get("LevelsOutputBlack", "出力シャドウ"), PSDTranslation.Get("LevelsOutputBlackTooltip", "出力される画像の最も暗い部分の明るさ下限（0〜255）を制限します。")),   layer.UI.LevelsOutputBlack, 0f, 255f, ci);
            float now = IndentedSlider(new GUIContent(PSDTranslation.Get("LevelsOutputWhite", "出力ハイライト"), PSDTranslation.Get("LevelsOutputWhiteTooltip", "出力される画像の最も明るい部分の明るさ上限（0〜255）を制限します。")), layer.UI.LevelsOutputWhite, 0f, 255f, ci);
            if (!Mathf.Approximately(nib, layer.UI.LevelsInputBlack)  ||
                !Mathf.Approximately(niw, layer.UI.LevelsInputWhite)  ||
                !Mathf.Approximately(ng,  layer.UI.LevelsGamma)       ||
                !Mathf.Approximately(nob, layer.UI.LevelsOutputBlack) ||
                !Mathf.Approximately(now, layer.UI.LevelsOutputWhite))
            {
                layer.UI.LevelsInputBlack  = nib;
                layer.UI.LevelsInputWhite  = niw;
                layer.UI.LevelsGamma       = ng;
                layer.UI.LevelsOutputBlack = nob;
                layer.UI.LevelsOutputWhite = now;
                MarkDirty();
            }
        }

        /// <summary>「カラーバランス」有効トグル + シャドウ/中間調/ハイライトの色シフト + 輝度保持。</summary>
        void DrawColorBalanceControls(PSDLayer layer, int indent)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(indent * IndentWidth + 18f);
            bool en = EditorGUILayout.ToggleLeft(
                new GUIContent(PSDTranslation.Get("ColorBalance", "カラーバランス"), PSDTranslation.Get("ColorBalanceTooltip", "シャドウ・中間調・ハイライトごとに色味（シアン-赤/マゼンタ-緑/黄-青）を調整します。")),
                layer.UI.ColorBalanceEnabled, GUILayout.Height(RowH));
            GUILayout.FlexibleSpace();
            DrawAdjustmentGearMenu(ClipboardKind.ColorBalance, layer);
            EditorGUILayout.EndHorizontal();
            RowSpace();
            if (en != layer.UI.ColorBalanceEnabled)
            {
                layer.UI.ColorBalanceEnabled = en;
                MarkDirty();
            }
            if (!en) return;

            bool changed = false;
            Vector3 DrawRange(string title, Vector3 v)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(indent * IndentWidth + 18f);
                EditorGUILayout.LabelField(title, GUILayout.Height(RowH));
                EditorGUILayout.EndHorizontal();
                RowSpace();
                float cr = IndentedSlider(new GUIContent(PSDTranslation.Get("CyanRed", "ｼｱﾝ-赤"),   PSDTranslation.Get("CyanRedTooltip", "シアン ←→ 赤（-100 〜 100）。")),   v.x, -100f, 100f, indent + 1);
                float mg = IndentedSlider(new GUIContent(PSDTranslation.Get("MagentaGreen", "ﾏｾﾞﾝﾀ-緑"), PSDTranslation.Get("MagentaGreenTooltip", "マゼンタ ←→ 緑（-100 〜 100）。")), v.y, -100f, 100f, indent + 1);
                float yb = IndentedSlider(new GUIContent(PSDTranslation.Get("YellowBlue", "黄-青"),     PSDTranslation.Get("YellowBlueTooltip", "黄 ←→ 青（-100 〜 100）。")),       v.z, -100f, 100f, indent + 1);
                var nv = new Vector3(cr, mg, yb);
                if (!Mathf.Approximately(nv.x, v.x) || !Mathf.Approximately(nv.y, v.y) || !Mathf.Approximately(nv.z, v.z))
                    changed = true;
                return nv;
            }

            var s = DrawRange(PSDTranslation.Get("Shadows", "シャドウ"),   layer.UI.CBShadows);
            var m = DrawRange(PSDTranslation.Get("Midtones", "中間調"),     layer.UI.CBMidtones);
            var h = DrawRange(PSDTranslation.Get("Highlights", "ハイライト"), layer.UI.CBHighlights);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(indent * IndentWidth + 18f);
            bool pl = EditorGUILayout.ToggleLeft(new GUIContent(PSDTranslation.Get("PreserveLuminosity", "輝度を保持"), PSDTranslation.Get("PreserveLuminosityTooltip", "色を変えても明るさ（輝度）を維持します。")),
                                                 layer.UI.CBPreserveLuminosity, GUILayout.Height(RowH));
            EditorGUILayout.EndHorizontal();
            RowSpace();

            if (changed || pl != layer.UI.CBPreserveLuminosity)
            {
                layer.UI.CBShadows            = s;
                layer.UI.CBMidtones           = m;
                layer.UI.CBHighlights         = h;
                layer.UI.CBPreserveLuminosity = pl;
                MarkDirty();
            }
        }

        /// <summary>「トーンカーブ」有効トグル + カーブエディタ (非破壊。全ピクセルレイヤーに適用可)。</summary>
        void DrawCurveControls(PSDLayer layer, int indent)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(indent * IndentWidth + 18f);
            bool en = EditorGUILayout.ToggleLeft(new GUIContent(PSDTranslation.Get("ToneCurve", "トーンカーブ"), PSDTranslation.Get("ToneCurveTooltip", "トーンカーブによる色調補正を有効にします。")), layer.UI.CurveEnabled, GUILayout.Height(RowH));
            GUILayout.FlexibleSpace();
            DrawAdjustmentGearMenu(ClipboardKind.Curve, layer);
            EditorGUILayout.EndHorizontal();
            RowSpace();
            if (en != layer.UI.CurveEnabled)
            {
                layer.UI.CurveEnabled = en;
                MarkDirty();
            }
            if (!en) return;

            // 初回描画時 (parse 済みレイヤーの初回表示含む) に LUT が無ければ焼く
            AdjustmentLutBaker.EnsureCurveLut(layer);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(indent * IndentWidth + 18f);
            GUILayout.Label(new GUIContent(PSDTranslation.Get("Curve", "カーブ"), PSDTranslation.Get("CurveTooltip", "入力レベルに対する出力レベルをグラフで編集して微調整します。")), PSDEditorTheme.ControlLabelStyle,
                            GUILayout.Width(48), GUILayout.Height(RowH));
            EditorGUI.BeginChangeCheck();
            AnimationCurve nc = EditorGUILayout.CurveField(layer.UI.Curve, GUILayout.Height(60));
            bool curveChanged = EditorGUI.EndChangeCheck();
            EditorGUILayout.EndHorizontal();
            RowSpace();
            if (curveChanged)
            {
                layer.UI.Curve = nc;
                AdjustmentLutBaker.BakeCurveLut(layer);
                MarkDirty();
            }
        }

        /// <summary>画像クリップ合成: 任意画像をレイヤーα形状へクリップ・タイリング・ブレンド。</summary>
        void DrawImageClipControls(PSDLayer layer, int indent)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(indent * IndentWidth + 18f);
            bool en = EditorGUILayout.ToggleLeft(new GUIContent(PSDTranslation.Get("ImageClip", "画像クリップ合成"), PSDTranslation.Get("ImageClipTooltip", "別の外部画像をこのレイヤーの不透明形状（アルファ）に合わせて合成します。")), layer.UI.ImageClipEnabled,
                                                 GUILayout.Height(RowH));
            GUILayout.FlexibleSpace();
            DrawAdjustmentGearMenu(ClipboardKind.ImageClip, layer);
            EditorGUILayout.EndHorizontal();
            RowSpace();
            if (en != layer.UI.ImageClipEnabled)
            {
                layer.UI.ImageClipEnabled = en;
                MarkDirty();
            }
            if (!en) return;

            // 画像
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(indent * IndentWidth + 18f);
            GUILayout.Label(new GUIContent(PSDTranslation.Get("Image", "画像"), PSDTranslation.Get("ImageTooltip", "合成に使用するテクスチャ画像（Asset）を指定します。")), PSDEditorTheme.ControlLabelStyle,
                            GUILayout.Width(48), GUILayout.Height(RowH));
            var tex = (Texture2D)EditorGUILayout.ObjectField(
                layer.UI.ImageClipTex, typeof(Texture2D), false,
                GUILayout.Width(64), GUILayout.Height(64));
            EditorGUILayout.EndHorizontal();
            RowSpace();
            if (tex != layer.UI.ImageClipTex)
            {
                layer.UI.ImageClipTex = tex;
                MarkDirty();
            }

            // タイル反復数 (X,Y)
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(indent * IndentWidth + 18f);
            GUILayout.Label(new GUIContent(PSDTranslation.Get("Tile", "タイル"), PSDTranslation.Get("TileTooltip", "合成画像の縦横タイリング反復回数を設定します。1.0で等倍です。")), PSDEditorTheme.ControlLabelStyle,
                            GUILayout.Width(48), GUILayout.Height(RowH));
            Vector2 nt = NarrowLabelField(() =>
                EditorGUILayout.Vector2Field(new GUIContent("", PSDTranslation.Get("TileTooltip2", "合成画像の縦横タイリング反復回数")), layer.UI.ImageClipTile,
                                             GUILayout.Height(RowH)));
            EditorGUILayout.EndHorizontal();
            RowSpace();
            if (nt != layer.UI.ImageClipTile)
            {
                // 0 / 負値はタイリングが破綻するため下限でクランプ
                layer.UI.ImageClipTile = new Vector2(Mathf.Max(0.01f, nt.x), Mathf.Max(0.01f, nt.y));
                MarkDirty();
            }

            // 合成モード
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(indent * IndentWidth + 18f);
            GUILayout.Label(new GUIContent(PSDTranslation.Get("Blend", "合成"), PSDTranslation.Get("BlendTooltip", "クリップ画像を元画像とブレンドする際のモードを設定します。")), PSDEditorTheme.ControlLabelStyle,
                            GUILayout.Width(48), GUILayout.Height(RowH));
            BlendMode[] modes  = _blendModesNormal;
            string[]    labels = _blendLabelsNormal ?? (_blendLabelsNormal = BuildBlendLabels(_blendModesNormal));
            int curIndex = Mathf.Max(0, Array.IndexOf(modes, layer.UI.ImageClipBlend));
            int newIndex = NarrowLabelField(() =>
                EditorGUILayout.Popup(new GUIContent("", PSDTranslation.Get("BlendModeTooltip2", "ブレンドモード")), curIndex, labels, GUILayout.Height(RowH)));
            EditorGUILayout.EndHorizontal();
            RowSpace();
            if (newIndex != curIndex)
            {
                layer.UI.ImageClipBlend = modes[newIndex];
                MarkDirty();
            }

            // 不透明度
            float no = IndentedSlider(new GUIContent(PSDTranslation.Get("Opacity", "不透明度"), PSDTranslation.Get("OpacityTooltip", "合成するクリップ画像の重ね合わせ不透明度を調整します。")), layer.UI.ImageClipOpacity, 0f, 1f, indent);
            if (!Mathf.Approximately(no, layer.UI.ImageClipOpacity))
            {
                layer.UI.ImageClipOpacity = no;
                MarkDirty();
            }
        }

        /// <summary>グラデーションマップの有効トグル・グラデーション編集・適用率。</summary>
        void DrawGradientMapControls(PSDLayer layer, int indent)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(indent * IndentWidth + 18f);
            bool en = EditorGUILayout.ToggleLeft(new GUIContent(PSDTranslation.Get("GradientMap", "グラデーションマップ"), PSDTranslation.Get("GradientMapTooltip", "輝度（白黒の明るさ）に基づいて別のグラデーション色を適用する機能を有効にします。")), layer.UI.GradientMapEnabled,
                                                 GUILayout.Height(RowH));
            GUILayout.FlexibleSpace();
            DrawAdjustmentGearMenu(ClipboardKind.GradientMap, layer);
            EditorGUILayout.EndHorizontal();
            RowSpace();
            if (en != layer.UI.GradientMapEnabled)
            {
                layer.UI.GradientMapEnabled = en;
                if (en) AdjustmentLutBaker.EnsureGradientLut(layer);   // 初回有効化時に LUT を焼く
                MarkDirty();
            }
            if (!en) return;

            if (layer.UI.Gradient == null) layer.UI.Gradient = AdjustmentLutBaker.CreateDefaultGradient();

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(indent * IndentWidth + 18f);
            bool normalize = EditorGUILayout.ToggleLeft(
                new GUIContent(PSDTranslation.Get("NormalizeLuminosity", "輝度を正規化"), PSDTranslation.Get("NormalizeLuminosityTooltip", "レイヤー内の最も暗い色から最も明るい色の輝度範囲を0〜1に自動ストレッチして、グラデーションが均等にかかるように調整します。")),
                layer.UI.GradientMapNormalize, GUILayout.Height(RowH));
            EditorGUILayout.EndHorizontal();
            RowSpace();
            if (normalize != layer.UI.GradientMapNormalize)
            {
                layer.UI.GradientMapNormalize = normalize;
                if (normalize) AdjustmentLutBaker.ComputeGradientLumRange(layer);
                MarkDirty();
            }

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(indent * IndentWidth + 18f);
            GUILayout.Label(new GUIContent(PSDTranslation.Get("GradientMapGrad", "階調"), PSDTranslation.Get("GradientMapGradTooltip", "グラデーションマップで使用するグラデーションを編集します。")), PSDEditorTheme.ControlLabelStyle,
                            GUILayout.Width(48), GUILayout.Height(RowH));
            EditorGUI.BeginChangeCheck();
            Gradient ng = NarrowLabelField(() =>
                EditorGUILayout.GradientField(new GUIContent("", PSDTranslation.Get("GradientMapGradTooltip2", "グラデーションマップで使用するグラデーション")), layer.UI.Gradient, GUILayout.Height(RowH)));
            bool gradientChanged = EditorGUI.EndChangeCheck();
            EditorGUILayout.EndHorizontal();
            RowSpace();
            if (gradientChanged)
            {
                layer.UI.Gradient = ng;
                AdjustmentLutBaker.BakeGradientLut(layer);
                MarkDirty();
            }

            float no = IndentedSlider(new GUIContent(PSDTranslation.Get("GradientMapOpacity", "適用率"), PSDTranslation.Get("GradientMapOpacityTooltip", "グラデーションマップを適用する強度（0.0 〜 1.0）を設定します。")), layer.UI.GradientMapOpacity, 0f, 1f, indent);
            if (!Mathf.Approximately(no, layer.UI.GradientMapOpacity))
            {
                layer.UI.GradientMapOpacity = no;
                MarkDirty();
            }
        }

        /// <summary>
        /// EditorGUILayout 系フィールドの最小レイアウト幅には labelWidth (既定 ~150px) が常に含まれるため、
        /// ラベル列を自前描画している行では labelWidth を最小化して描く。
        /// これを怠るとレイヤーパネルが狭いときにフィールドが右へはみ出す。
        /// </summary>
        static T NarrowLabelField<T>(Func<T> draw)
        {
            float originalWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 1f;
            try { return draw(); }
            finally { EditorGUIUtility.labelWidth = originalWidth; }
        }

        /// <summary>インデント付きのラベル + スライダー 1 行 (行間の縦余白付き)。</summary>
        static float IndentedSlider(GUIContent label, float value, float min, float max, int indent)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(indent * IndentWidth + 18f);
            GUILayout.Label(label, PSDEditorTheme.ControlLabelStyle,
                            GUILayout.Width(48), GUILayout.Height(RowH));

            float originalWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 1f;
            float result = EditorGUILayout.Slider(value, min, max, GUILayout.Height(RowH));
            EditorGUIUtility.labelWidth = originalWidth;

            EditorGUILayout.EndHorizontal();
            RowSpace();
            return result;
        }
    }
}
