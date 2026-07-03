using System;
using UnityEngine;
using UnityEditor;

namespace PSDSimpleEditor
{
    // ── 色調補正 + グラデーションマップ + 画像クリップ (非破壊・全ピクセルレイヤー) ──
    public partial class PSDSimpleEditorWindow
    {
        /// <summary>「色調補正」フォールドアウト。明るさ/コントラスト/色相/彩度/明度 + グラデーションマップ。</summary>
        void DrawAdjustmentFoldout(PSDLayer layer, int indent)
        {
            layer.UI.AdjustExpanded = DrawSectionFoldout("色調補正", layer.UI.AdjustExpanded, indent, layer, ClipboardKind.FullAdjustmentSection);
            if (!layer.UI.AdjustExpanded) { RowSpace(); return; }
            RowSpace();

            int ci = indent + 1;

            // 明るさ・コントラスト・色相・彩度・明度 (シェーダーの正規化除数に合わせた範囲)
            float nb = IndentedSlider(new GUIContent("明るさ", "レイヤーの明るさを調整します（-150 〜 150）。"),  layer.UI.Brightness, -150f, 150f, ci);
            float nc = IndentedSlider(new GUIContent("ｺﾝﾄﾗｽﾄ", "レイヤーのコントラスト（明暗差）を調整します（-50 〜 100）。"),  layer.UI.Contrast,   -50f, 100f, ci);
            float nh = IndentedSlider(new GUIContent("色相", "レイヤーの色相（カラー）を調整します（-180度 〜 180度）。"),    layer.UI.Hue,        -180f, 180f, ci);
            float ns = IndentedSlider(new GUIContent("彩度", "レイヤーの彩度（鮮やかさ）を調整します（-100 〜 100）。"),    layer.UI.Saturation, -100f, 100f, ci);
            float nl = IndentedSlider(new GUIContent("明度", "レイヤーの明度を調整します（-100 〜 100）。"),    layer.UI.Lightness,  -100f, 100f, ci);
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
                _needsRecomposite  = true;
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
            bool en = EditorGUILayout.ToggleLeft(new GUIContent("着色 (白黒にも色を入れる)", "白黒（無彩色）の領域にも色相・彩度を適用して着色できるようにします。"), layer.UI.Colorize,
                                                 GUILayout.Height(RowH));
            GUILayout.FlexibleSpace();
            DrawAdjustmentGearMenu(ClipboardKind.Colorize, layer);
            EditorGUILayout.EndHorizontal();
            RowSpace();
            if (en != layer.UI.Colorize)
            {
                layer.UI.Colorize  = en;
                _needsRecomposite = true;
            }
        }

        /// <summary>「階調反転」トグル (非破壊。全ピクセルレイヤーに適用可)。</summary>
        void DrawInvertToggle(PSDLayer layer, int indent)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(indent * IndentWidth + 18f);
            bool en = EditorGUILayout.ToggleLeft(new GUIContent("階調反転", "レイヤーの色のRGB値を反転します。"), layer.UI.Invert, GUILayout.Height(RowH));
            GUILayout.FlexibleSpace();
            DrawAdjustmentGearMenu(ClipboardKind.Invert, layer);
            EditorGUILayout.EndHorizontal();
            RowSpace();
            if (en != layer.UI.Invert)
            {
                layer.UI.Invert    = en;
                _needsRecomposite = true;
            }
        }

        /// <summary>「しきい値」有効トグル + レベルスライダー (非破壊。全ピクセルレイヤーに適用可)。</summary>
        void DrawThresholdControls(PSDLayer layer, int indent)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(indent * IndentWidth + 18f);
            bool en = EditorGUILayout.ToggleLeft(new GUIContent("しきい値", "画像を白と黒の2階調に変換する機能を有効にします。"), layer.UI.ThresholdEnabled, GUILayout.Height(RowH));
            GUILayout.FlexibleSpace();
            DrawAdjustmentGearMenu(ClipboardKind.Threshold, layer);
            EditorGUILayout.EndHorizontal();
            RowSpace();
            if (en != layer.UI.ThresholdEnabled)
            {
                layer.UI.ThresholdEnabled = en;
                _needsRecomposite = true;
            }
            if (!en) return;

            float nl = IndentedSlider(new GUIContent("レベル", "2階調に分ける基準値（0 〜 255）を設定します。"), layer.UI.ThresholdLevel, 0f, 255f, indent);
            if (!Mathf.Approximately(nl, layer.UI.ThresholdLevel))
            {
                layer.UI.ThresholdLevel = nl;
                _needsRecomposite = true;
            }
        }

        /// <summary>「ポスタリゼーション」有効トグル + 階調数スライダー (非破壊。全ピクセルレイヤーに適用可)。</summary>
        void DrawPosterizeControls(PSDLayer layer, int indent)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(indent * IndentWidth + 18f);
            bool en = EditorGUILayout.ToggleLeft(new GUIContent("ポスタリゼーション", "画像の階調数を減らしてイラスト調（トーン減少）にする効果を有効にします。"), layer.UI.PosterizeEnabled,
                                                 GUILayout.Height(RowH));
            GUILayout.FlexibleSpace();
            DrawAdjustmentGearMenu(ClipboardKind.Posterize, layer);
            EditorGUILayout.EndHorizontal();
            RowSpace();
            if (en != layer.UI.PosterizeEnabled)
            {
                layer.UI.PosterizeEnabled = en;
                _needsRecomposite = true;
            }
            if (!en) return;

            float nl = IndentedSlider(new GUIContent("階調数", "表現する階調数（2 〜 255）を設定します。"), layer.UI.PosterizeLevels, 2f, 255f, indent);
            if (!Mathf.Approximately(nl, layer.UI.PosterizeLevels))
            {
                layer.UI.PosterizeLevels = nl;
                _needsRecomposite = true;
            }
        }

        /// <summary>「レベル補正」有効トグル + 5 スライダー (非破壊)。</summary>
        void DrawLevelsControls(PSDLayer layer, int indent)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(indent * IndentWidth + 18f);
            bool en = EditorGUILayout.ToggleLeft(new GUIContent("レベル補正", "ハイライト・シャドウや中間調の入力/出力レベルを調整して、画像の明暗のバランスを補正します。"), layer.UI.LevelsEnabled, GUILayout.Height(RowH));
            GUILayout.FlexibleSpace();
            DrawAdjustmentGearMenu(ClipboardKind.Levels, layer);
            EditorGUILayout.EndHorizontal();
            RowSpace();
            if (en != layer.UI.LevelsEnabled)
            {
                layer.UI.LevelsEnabled = en;
                _needsRecomposite = true;
            }
            if (!en) return;

            int ci = indent + 1;
            float nib = IndentedSlider(new GUIContent("入力シャドウ", "最も暗い部分をどの入力レベル（0〜255）から開始するかを設定します。"), layer.UI.LevelsInputBlack,  0f, 255f, ci);
            float niw = IndentedSlider(new GUIContent("入力ハイライト", "最も明るい部分をどの入力レベル（0〜255）で終了するかを設定します。"), layer.UI.LevelsInputWhite,  0f, 255f, ci);
            float ng  = IndentedSlider(new GUIContent("ガンマ", "中間調の明るさ（ガンマ値、0.01〜9.99）を調整します。1.0が基準です。"),        layer.UI.LevelsGamma,       0.01f, 9.99f, ci);
            float nob = IndentedSlider(new GUIContent("出力シャドウ", "出力される画像の最も暗い部分の明るさ下限（0〜255）を制限します。"),   layer.UI.LevelsOutputBlack, 0f, 255f, ci);
            float now = IndentedSlider(new GUIContent("出力ハイライト", "出力される画像の最も明るい部分の明るさ上限（0〜255）を制限します。"), layer.UI.LevelsOutputWhite, 0f, 255f, ci);
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
                _needsRecomposite = true;
            }
        }

        /// <summary>「カラーバランス」有効トグル + シャドウ/中間調/ハイライトの色シフト + 輝度保持。</summary>
        void DrawColorBalanceControls(PSDLayer layer, int indent)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(indent * IndentWidth + 18f);
            bool en = EditorGUILayout.ToggleLeft(
                new GUIContent("カラーバランス", "シャドウ・中間調・ハイライトごとに色味（シアン-赤/マゼンタ-緑/黄-青）を調整します。"),
                layer.UI.ColorBalanceEnabled, GUILayout.Height(RowH));
            GUILayout.FlexibleSpace();
            DrawAdjustmentGearMenu(ClipboardKind.ColorBalance, layer);
            EditorGUILayout.EndHorizontal();
            RowSpace();
            if (en != layer.UI.ColorBalanceEnabled)
            {
                layer.UI.ColorBalanceEnabled = en;
                _needsRecomposite = true;
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
                float cr = IndentedSlider(new GUIContent("ｼｱﾝ-赤",   "シアン ←→ 赤（-100 〜 100）。"),   v.x, -100f, 100f, indent + 1);
                float mg = IndentedSlider(new GUIContent("ﾏｾﾞﾝﾀ-緑", "マゼンタ ←→ 緑（-100 〜 100）。"), v.y, -100f, 100f, indent + 1);
                float yb = IndentedSlider(new GUIContent("黄-青",     "黄 ←→ 青（-100 〜 100）。"),       v.z, -100f, 100f, indent + 1);
                var nv = new Vector3(cr, mg, yb);
                if (!Mathf.Approximately(nv.x, v.x) || !Mathf.Approximately(nv.y, v.y) || !Mathf.Approximately(nv.z, v.z))
                    changed = true;
                return nv;
            }

            var s = DrawRange("シャドウ",   layer.UI.CBShadows);
            var m = DrawRange("中間調",     layer.UI.CBMidtones);
            var h = DrawRange("ハイライト", layer.UI.CBHighlights);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(indent * IndentWidth + 18f);
            bool pl = EditorGUILayout.ToggleLeft(new GUIContent("輝度を保持", "色を変えても明るさ（輝度）を維持します。"),
                                                 layer.UI.CBPreserveLuminosity, GUILayout.Height(RowH));
            EditorGUILayout.EndHorizontal();
            RowSpace();

            if (changed || pl != layer.UI.CBPreserveLuminosity)
            {
                layer.UI.CBShadows            = s;
                layer.UI.CBMidtones           = m;
                layer.UI.CBHighlights         = h;
                layer.UI.CBPreserveLuminosity = pl;
                _needsRecomposite            = true;
            }
        }

        /// <summary>「トーンカーブ」有効トグル + カーブエディタ (非破壊。全ピクセルレイヤーに適用可)。</summary>
        void DrawCurveControls(PSDLayer layer, int indent)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(indent * IndentWidth + 18f);
            bool en = EditorGUILayout.ToggleLeft(new GUIContent("トーンカーブ", "トーンカーブによる色調補正を有効にします。"), layer.UI.CurveEnabled, GUILayout.Height(RowH));
            GUILayout.FlexibleSpace();
            DrawAdjustmentGearMenu(ClipboardKind.Curve, layer);
            EditorGUILayout.EndHorizontal();
            RowSpace();
            if (en != layer.UI.CurveEnabled)
            {
                layer.UI.CurveEnabled = en;
                _needsRecomposite = true;
            }
            if (!en) return;

            // 初回描画時 (parse 済みレイヤーの初回表示含む) に LUT が無ければ焼く
            EnsureCurveLut(layer);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(indent * IndentWidth + 18f);
            GUILayout.Label(new GUIContent("カーブ", "入力レベルに対する出力レベルをグラフで編集して微調整します。"), PSDEditorTheme.ControlLabelStyle,
                            GUILayout.Width(48), GUILayout.Height(RowH));
            EditorGUI.BeginChangeCheck();
            AnimationCurve nc = EditorGUILayout.CurveField(layer.UI.Curve, GUILayout.Height(60));
            bool curveChanged = EditorGUI.EndChangeCheck();
            EditorGUILayout.EndHorizontal();
            RowSpace();
            if (curveChanged)
            {
                layer.UI.Curve = nc;
                BakeCurveLut(layer);
                _needsRecomposite = true;
            }
        }

        static AnimationCurve CreateDefaultCurve()
        {
            var c = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 1f));
            for (int i = 0; i < c.length; i++) c.SmoothTangents(i, 0f);
            return c;
        }

        /// <summary>トーンカーブ有効時に LUT が無ければ焼く。</summary>
        void EnsureCurveLut(PSDLayer layer)
        {
            if (layer.UI.Curve == null) layer.UI.Curve = CreateDefaultCurve();
            if (layer._curveLut == null) BakeCurveLut(layer);
        }

        /// <summary>UI.Curve (+ パース済みチャンネル別カーブ) を 256×1 の LUT テクスチャに焼き込む。
        /// R/G/B 各チャンネル値 = 複合カーブ(チャンネルカーブ(入力))。チャンネルカーブが無い場合は R=G=B。</summary>
        static void BakeCurveLut(PSDLayer layer)
        {
            const int N = 256;
            if (layer._curveLut == null)
            {
                layer._curveLut = new Texture2D(N, 1, TextureFormat.RGBA32, false, linear: true)
                {
                    hideFlags  = HideFlags.HideAndDontSave,
                    wrapMode   = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                };
            }
            var px = new Color32[N];
            for (int i = 0; i < N; i++)
            {
                float x = i / (float)(N - 1);
                px[i] = new Color32(
                    EvalCurveChannel(layer, 0, x),
                    EvalCurveChannel(layer, 1, x),
                    EvalCurveChannel(layer, 2, x),
                    255);
            }
            layer._curveLut.SetPixels32(px);
            layer._curveLut.Apply(false);
        }

        /// <summary>チャンネルカーブ → 複合カーブの順で評価した出力値 (0..255)。</summary>
        static byte EvalCurveChannel(PSDLayer layer, int channel, float x)
        {
            var chCurves = layer.UI.CurveChannels;
            if (chCurves != null && chCurves[channel] != null)
                x = Mathf.Clamp01(chCurves[channel].Evaluate(x));
            float v = Mathf.Clamp01(layer.UI.Curve.Evaluate(x));
            return (byte)Mathf.RoundToInt(v * 255f);
        }

        /// <summary>画像クリップ合成: 任意画像をレイヤーα形状へクリップ・タイリング・ブレンド。</summary>
        void DrawImageClipControls(PSDLayer layer, int indent)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(indent * IndentWidth + 18f);
            bool en = EditorGUILayout.ToggleLeft(new GUIContent("画像クリップ合成", "別の外部画像をこのレイヤーの不透明形状（アルファ）に合わせて合成します。"), layer.UI.ImageClipEnabled,
                                                 GUILayout.Height(RowH));
            GUILayout.FlexibleSpace();
            DrawAdjustmentGearMenu(ClipboardKind.ImageClip, layer);
            EditorGUILayout.EndHorizontal();
            RowSpace();
            if (en != layer.UI.ImageClipEnabled)
            {
                layer.UI.ImageClipEnabled = en;
                _needsRecomposite = true;
            }
            if (!en) return;

            // 画像
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(indent * IndentWidth + 18f);
            GUILayout.Label(new GUIContent("画像", "合成に使用するテクスチャ画像（Asset）を指定します。"), PSDEditorTheme.ControlLabelStyle,
                            GUILayout.Width(48), GUILayout.Height(RowH));
            var tex = (Texture2D)EditorGUILayout.ObjectField(
                layer.UI.ImageClipTex, typeof(Texture2D), false,
                GUILayout.Width(64), GUILayout.Height(64));
            EditorGUILayout.EndHorizontal();
            RowSpace();
            if (tex != layer.UI.ImageClipTex)
            {
                layer.UI.ImageClipTex = tex;
                _needsRecomposite = true;
            }

            // タイル反復数 (X,Y)
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(indent * IndentWidth + 18f);
            GUILayout.Label(new GUIContent("タイル", "合成画像の縦横タイリング反復回数を設定します。1.0で等倍です。"), PSDEditorTheme.ControlLabelStyle,
                            GUILayout.Width(48), GUILayout.Height(RowH));
            Vector2 nt = NarrowLabelField(() =>
                EditorGUILayout.Vector2Field(new GUIContent("", "合成画像の縦横タイリング反復回数"), layer.UI.ImageClipTile,
                                             GUILayout.Height(RowH)));
            EditorGUILayout.EndHorizontal();
            RowSpace();
            if (nt != layer.UI.ImageClipTile)
            {
                // 0 / 負値はタイリングが破綻するため下限でクランプ
                layer.UI.ImageClipTile = new Vector2(Mathf.Max(0.01f, nt.x), Mathf.Max(0.01f, nt.y));
                _needsRecomposite = true;
            }

            // 合成モード
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(indent * IndentWidth + 18f);
            GUILayout.Label(new GUIContent("合成", "クリップ画像を元画像とブレンドする際のモードを設定します。"), PSDEditorTheme.ControlLabelStyle,
                            GUILayout.Width(48), GUILayout.Height(RowH));
            BlendMode[] modes  = _blendModesNormal;
            string[]    labels = _blendLabelsNormal ?? (_blendLabelsNormal = BuildBlendLabels(_blendModesNormal));
            int curIndex = Mathf.Max(0, Array.IndexOf(modes, layer.UI.ImageClipBlend));
            int newIndex = NarrowLabelField(() =>
                EditorGUILayout.Popup(new GUIContent("", "ブレンドモード"), curIndex, labels, GUILayout.Height(RowH)));
            EditorGUILayout.EndHorizontal();
            RowSpace();
            if (newIndex != curIndex)
            {
                layer.UI.ImageClipBlend = modes[newIndex];
                _needsRecomposite = true;
            }

            // 不透明度
            float no = IndentedSlider(new GUIContent("不透明度", "合成するクリップ画像の重ね合わせ不透明度を調整します。"), layer.UI.ImageClipOpacity, 0f, 1f, indent);
            if (!Mathf.Approximately(no, layer.UI.ImageClipOpacity))
            {
                layer.UI.ImageClipOpacity = no;
                _needsRecomposite = true;
            }
        }

        /// <summary>グラデーションマップの有効トグル・グラデーション編集・適用率。</summary>
        void DrawGradientMapControls(PSDLayer layer, int indent)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(indent * IndentWidth + 18f);
            bool en = EditorGUILayout.ToggleLeft(new GUIContent("グラデーションマップ", "輝度（白黒の明るさ）に基づいて別のグラデーション色を適用する機能を有効にします。"), layer.UI.GradientMapEnabled,
                                                 GUILayout.Height(RowH));
            GUILayout.FlexibleSpace();
            DrawAdjustmentGearMenu(ClipboardKind.GradientMap, layer);
            EditorGUILayout.EndHorizontal();
            RowSpace();
            if (en != layer.UI.GradientMapEnabled)
            {
                layer.UI.GradientMapEnabled = en;
                if (en) EnsureGradientLut(layer);   // 初回有効化時に LUT を焼く
                _needsRecomposite = true;
            }
            if (!en) return;

            if (layer.UI.Gradient == null) layer.UI.Gradient = CreateDefaultGradient();

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(indent * IndentWidth + 18f);
            bool normalize = EditorGUILayout.ToggleLeft(
                new GUIContent("輝度を正規化", "レイヤー内の最も暗い色から最も明るい色の輝度範囲を0〜1に自動ストレッチして、グラデーションが均等にかかるように調整します。"),
                layer.UI.GradientMapNormalize, GUILayout.Height(RowH));
            EditorGUILayout.EndHorizontal();
            RowSpace();
            if (normalize != layer.UI.GradientMapNormalize)
            {
                layer.UI.GradientMapNormalize = normalize;
                if (normalize) ComputeGradientLumRange(layer);
                _needsRecomposite = true;
            }

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(indent * IndentWidth + 18f);
            GUILayout.Label(new GUIContent("階調", "グラデーションマップで使用するグラデーションを編集します。"), PSDEditorTheme.ControlLabelStyle,
                            GUILayout.Width(48), GUILayout.Height(RowH));
            EditorGUI.BeginChangeCheck();
            Gradient ng = NarrowLabelField(() =>
                EditorGUILayout.GradientField(new GUIContent("", "グラデーションマップで使用するグラデーション"), layer.UI.Gradient, GUILayout.Height(RowH)));
            bool gradientChanged = EditorGUI.EndChangeCheck();
            EditorGUILayout.EndHorizontal();
            RowSpace();
            if (gradientChanged)
            {
                layer.UI.Gradient = ng;
                BakeGradientLut(layer);
                _needsRecomposite = true;
            }

            float no = IndentedSlider(new GUIContent("適用率", "グラデーションマップを適用する強度（0.0 〜 1.0）を設定します。"), layer.UI.GradientMapOpacity, 0f, 1f, indent);
            if (!Mathf.Approximately(no, layer.UI.GradientMapOpacity))
            {
                layer.UI.GradientMapOpacity = no;
                _needsRecomposite = true;
            }
        }

        static Gradient CreateDefaultGradient()
        {
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.black, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f),          new GradientAlphaKey(1f, 1f) });
            return g;
        }

        /// <summary>PSD からインポートしたグラデーションマップ (grdm) / グラデーション塗りつぶし (GdFl) /
        /// トーンカーブ (curv、畳み戻されたものを含む) の LUT をロード直後に焼く (ツリー再帰)。</summary>
        void BakeImportedLuts(System.Collections.Generic.List<PSDLayer> layers)
        {
            if (layers == null) return;
            foreach (var l in layers)
            {
                if (l.UI.GradientMapEnabled && l.UI.Gradient != null)
                    EnsureGradientLut(l);
                if (l.Adjustment != null && l.Adjustment.HasGradientFill && l.Adjustment.GradientFillGradient != null)
                    BakeGradientFillLut(l);
                if (l.UI.CurveEnabled)
                    EnsureCurveLut(l);
                BakeImportedLuts(l.Children);
            }
        }

        /// <summary>GradientFillGradient を 256×1 の LUT テクスチャ (linear) に焼き込む (GdFl 用)。</summary>
        static void BakeGradientFillLut(PSDLayer layer)
        {
            const int N = 256;
            if (layer._gradientFillLut == null)
            {
                layer._gradientFillLut = new Texture2D(N, 1, TextureFormat.RGBA32, false, linear: true)
                {
                    hideFlags  = HideFlags.HideAndDontSave,
                    wrapMode   = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                };
            }
            var px = new Color32[N];
            for (int i = 0; i < N; i++)
                px[i] = layer.Adjustment.GradientFillGradient.Evaluate(i / (float)(N - 1));
            layer._gradientFillLut.SetPixels32(px);
            layer._gradientFillLut.Apply(false);
        }

        /// <summary>グラデーション有効時に LUT が無ければ焼く。</summary>
        void EnsureGradientLut(PSDLayer layer)
        {
            if (layer.UI.Gradient == null) layer.UI.Gradient = CreateDefaultGradient();
            if (layer._gradientLut == null) BakeGradientLut(layer);
        }

        /// <summary>UI.Gradient を 256×1 の LUT テクスチャ (linear) に焼き込む。</summary>
        static void BakeGradientLut(PSDLayer layer)
        {
            const int N = 256;
            if (layer._gradientLut == null)
            {
                layer._gradientLut = new Texture2D(N, 1, TextureFormat.RGBA32, false, linear: true)
                {
                    hideFlags  = HideFlags.HideAndDontSave,
                    wrapMode   = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                };
            }
            var px = new Color32[N];
            for (int i = 0; i < N; i++)
                px[i] = layer.UI.Gradient.Evaluate(i / (float)(N - 1));
            layer._gradientLut.SetPixels32(px);
            layer._gradientLut.Apply(false);
        }

        /// <summary>
        /// 輝度正規化トグル ON 時に、レイヤーの不透明画素から輝度の最小・最大を求めて layer にキャッシュする。
        /// (完全透明画素は未定義色のことがあるため範囲計算から除外。不透明画素が無ければ 0..1 のまま = 無効果)
        /// </summary>
        static void ComputeGradientLumRange(PSDLayer layer)
        {
            float min = 1f, max = 0f;
            var tex = layer.Texture;
            if (tex != null)
            {
                var px = tex.GetPixels32();
                for (int i = 0; i < px.Length; i++)
                {
                    if (px[i].a == 0) continue;
                    float lum = (0.3f * px[i].r + 0.59f * px[i].g + 0.11f * px[i].b) / 255f;
                    if (lum < min) min = lum;
                    if (lum > max) max = lum;
                }
            }
            if (min > max) { min = 0f; max = 1f; } // 不透明画素なし → フォールバック (正規化を実質無効化)
            layer._gradientLumMin = min;
            layer._gradientLumMax = max;
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
