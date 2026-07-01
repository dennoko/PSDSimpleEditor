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
            layer.UIAdjustExpanded = DrawSectionFoldout("色調補正", layer.UIAdjustExpanded, indent);
            if (!layer.UIAdjustExpanded) { RowSpace(); return; }
            RowSpace();

            int ci = indent + 1;

            // 明るさ・コントラスト・色相・彩度・明度 (シェーダーの正規化除数に合わせた範囲)
            float nb = IndentedSlider("明るさ",  layer.UIBrightness, -150f, 150f, ci);
            float nc = IndentedSlider("ｺﾝﾄﾗｽﾄ",  layer.UIContrast,   -50f, 100f, ci);
            float nh = IndentedSlider("色相",    layer.UIHue,        -180f, 180f, ci);
            float ns = IndentedSlider("彩度",    layer.UISaturation, -100f, 100f, ci);
            float nl = IndentedSlider("明度",    layer.UILightness,  -100f, 100f, ci);
            if (!Mathf.Approximately(nb, layer.UIBrightness) ||
                !Mathf.Approximately(nc, layer.UIContrast)   ||
                !Mathf.Approximately(nh, layer.UIHue)        ||
                !Mathf.Approximately(ns, layer.UISaturation) ||
                !Mathf.Approximately(nl, layer.UILightness))
            {
                layer.UIBrightness = nb;
                layer.UIContrast   = nc;
                layer.UIHue        = nh;
                layer.UISaturation = ns;
                layer.UILightness  = nl;
                _needsRecomposite  = true;
            }
            DrawColorizeToggle(layer, ci);
            DrawInvertToggle(layer, ci);
            DrawThresholdControls(layer, ci);
            DrawPosterizeControls(layer, ci);
            DrawLevelsControls(layer, ci);
            DrawCurveControls(layer, ci);

            DrawGradientMapControls(layer, ci);
            DrawImageClipControls(layer, ci);
        }

        /// <summary>「着色」トグル。ON で絶対値の色相・彩度を強制し、白黒 (彩度0) にも色が乗る。</summary>
        void DrawColorizeToggle(PSDLayer layer, int indent)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(indent * IndentWidth + 18f);
            bool en = EditorGUILayout.ToggleLeft("着色 (白黒にも色を入れる)", layer.UIColorize,
                                                 GUILayout.Height(RowH));
            EditorGUILayout.EndHorizontal();
            RowSpace();
            if (en != layer.UIColorize)
            {
                layer.UIColorize  = en;
                _needsRecomposite = true;
            }
        }

        /// <summary>「階調反転」トグル (非破壊。全ピクセルレイヤーに適用可)。</summary>
        void DrawInvertToggle(PSDLayer layer, int indent)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(indent * IndentWidth + 18f);
            bool en = EditorGUILayout.ToggleLeft("階調反転", layer.UIInvert, GUILayout.Height(RowH));
            EditorGUILayout.EndHorizontal();
            RowSpace();
            if (en != layer.UIInvert)
            {
                layer.UIInvert    = en;
                _needsRecomposite = true;
            }
        }

        /// <summary>「しきい値」有効トグル + レベルスライダー (非破壊。全ピクセルレイヤーに適用可)。</summary>
        void DrawThresholdControls(PSDLayer layer, int indent)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(indent * IndentWidth + 18f);
            bool en = EditorGUILayout.ToggleLeft("しきい値", layer.UIThresholdEnabled, GUILayout.Height(RowH));
            EditorGUILayout.EndHorizontal();
            RowSpace();
            if (en != layer.UIThresholdEnabled)
            {
                layer.UIThresholdEnabled = en;
                _needsRecomposite = true;
            }
            if (!en) return;

            float nl = IndentedSlider("レベル", layer.UIThresholdLevel, 0f, 255f, indent);
            if (!Mathf.Approximately(nl, layer.UIThresholdLevel))
            {
                layer.UIThresholdLevel = nl;
                _needsRecomposite = true;
            }
        }

        /// <summary>「ポスタリゼーション」有効トグル + 階調数スライダー (非破壊。全ピクセルレイヤーに適用可)。</summary>
        void DrawPosterizeControls(PSDLayer layer, int indent)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(indent * IndentWidth + 18f);
            bool en = EditorGUILayout.ToggleLeft("ポスタリゼーション", layer.UIPosterizeEnabled,
                                                 GUILayout.Height(RowH));
            EditorGUILayout.EndHorizontal();
            RowSpace();
            if (en != layer.UIPosterizeEnabled)
            {
                layer.UIPosterizeEnabled = en;
                _needsRecomposite = true;
            }
            if (!en) return;

            float nl = IndentedSlider("階調数", layer.UIPosterizeLevels, 2f, 255f, indent);
            if (!Mathf.Approximately(nl, layer.UIPosterizeLevels))
            {
                layer.UIPosterizeLevels = nl;
                _needsRecomposite = true;
            }
        }

        /// <summary>「レベル補正」5 スライダー (非破壊。既定値は恒等変換のため常時表示)。</summary>
        void DrawLevelsControls(PSDLayer layer, int indent)
        {
            float nib = IndentedSlider("入力シャドウ", layer.UILevelsInputBlack,  0f, 255f, indent);
            float niw = IndentedSlider("入力ハイライト", layer.UILevelsInputWhite,  0f, 255f, indent);
            float ng  = IndentedSlider("ガンマ",        layer.UILevelsGamma,       0.01f, 9.99f, indent);
            float nob = IndentedSlider("出力シャドウ",   layer.UILevelsOutputBlack, 0f, 255f, indent);
            float now = IndentedSlider("出力ハイライト", layer.UILevelsOutputWhite, 0f, 255f, indent);
            if (!Mathf.Approximately(nib, layer.UILevelsInputBlack)  ||
                !Mathf.Approximately(niw, layer.UILevelsInputWhite)  ||
                !Mathf.Approximately(ng,  layer.UILevelsGamma)       ||
                !Mathf.Approximately(nob, layer.UILevelsOutputBlack) ||
                !Mathf.Approximately(now, layer.UILevelsOutputWhite))
            {
                layer.UILevelsInputBlack  = nib;
                layer.UILevelsInputWhite  = niw;
                layer.UILevelsGamma       = ng;
                layer.UILevelsOutputBlack = nob;
                layer.UILevelsOutputWhite = now;
                _needsRecomposite = true;
            }
        }

        /// <summary>「トーンカーブ」有効トグル + カーブエディタ (非破壊。全ピクセルレイヤーに適用可)。</summary>
        void DrawCurveControls(PSDLayer layer, int indent)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(indent * IndentWidth + 18f);
            bool en = EditorGUILayout.ToggleLeft("トーンカーブ", layer.UICurveEnabled, GUILayout.Height(RowH));
            EditorGUILayout.EndHorizontal();
            RowSpace();
            if (en != layer.UICurveEnabled)
            {
                layer.UICurveEnabled = en;
                _needsRecomposite = true;
            }
            if (!en) return;

            // 初回描画時 (parse 済みレイヤーの初回表示含む) に LUT が無ければ焼く
            EnsureCurveLut(layer);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(indent * IndentWidth + 18f);
            GUILayout.Label("カーブ", PSDEditorTheme.ControlLabelStyle,
                            GUILayout.Width(48), GUILayout.Height(RowH));
            EditorGUI.BeginChangeCheck();
            AnimationCurve nc = EditorGUILayout.CurveField(layer.UICurve, GUILayout.Height(60));
            bool curveChanged = EditorGUI.EndChangeCheck();
            EditorGUILayout.EndHorizontal();
            RowSpace();
            if (curveChanged)
            {
                layer.UICurve = nc;
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
            if (layer.UICurve == null) layer.UICurve = CreateDefaultCurve();
            if (layer._curveLut == null) BakeCurveLut(layer);
        }

        /// <summary>UICurve を 256×1 の LUT テクスチャ (linear, R=G=B=出力値) に焼き込む。</summary>
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
                float v = Mathf.Clamp01(layer.UICurve.Evaluate(i / (float)(N - 1)));
                byte  b = (byte)Mathf.RoundToInt(v * 255f);
                px[i] = new Color32(b, b, b, 255);
            }
            layer._curveLut.SetPixels32(px);
            layer._curveLut.Apply(false);
        }

        /// <summary>画像クリップ合成: 任意画像をレイヤーα形状へクリップ・タイリング・ブレンド。</summary>
        void DrawImageClipControls(PSDLayer layer, int indent)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(indent * IndentWidth + 18f);
            bool en = EditorGUILayout.ToggleLeft("画像クリップ合成", layer.UIImageClipEnabled,
                                                 GUILayout.Height(RowH));
            EditorGUILayout.EndHorizontal();
            RowSpace();
            if (en != layer.UIImageClipEnabled)
            {
                layer.UIImageClipEnabled = en;
                _needsRecomposite = true;
            }
            if (!en) return;

            // 画像
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(indent * IndentWidth + 18f);
            GUILayout.Label("画像", PSDEditorTheme.ControlLabelStyle,
                            GUILayout.Width(48), GUILayout.Height(RowH));
            var tex = (Texture2D)EditorGUILayout.ObjectField(layer.UIImageClipTex, typeof(Texture2D), false,
                                                             GUILayout.Height(RowH));
            EditorGUILayout.EndHorizontal();
            RowSpace();
            if (tex != layer.UIImageClipTex)
            {
                layer.UIImageClipTex = tex;
                _needsRecomposite = true;
            }

            // タイル反復数 (X,Y)
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(indent * IndentWidth + 18f);
            GUILayout.Label("タイル", PSDEditorTheme.ControlLabelStyle,
                            GUILayout.Width(48), GUILayout.Height(RowH));
            Vector2 nt = EditorGUILayout.Vector2Field(GUIContent.none, layer.UIImageClipTile,
                                                      GUILayout.Height(RowH));
            EditorGUILayout.EndHorizontal();
            RowSpace();
            if (nt != layer.UIImageClipTile)
            {
                // 0 / 負値はタイリングが破綻するため下限でクランプ
                layer.UIImageClipTile = new Vector2(Mathf.Max(0.01f, nt.x), Mathf.Max(0.01f, nt.y));
                _needsRecomposite = true;
            }

            // 合成モード
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(indent * IndentWidth + 18f);
            GUILayout.Label("合成", PSDEditorTheme.ControlLabelStyle,
                            GUILayout.Width(48), GUILayout.Height(RowH));
            BlendMode[] modes  = _blendModesNormal;
            string[]    labels = _blendLabelsNormal ?? (_blendLabelsNormal = BuildBlendLabels(_blendModesNormal));
            int curIndex = Mathf.Max(0, Array.IndexOf(modes, layer.UIImageClipBlend));
            int newIndex = EditorGUILayout.Popup(curIndex, labels, GUILayout.Height(RowH));
            EditorGUILayout.EndHorizontal();
            RowSpace();
            if (newIndex != curIndex)
            {
                layer.UIImageClipBlend = modes[newIndex];
                _needsRecomposite = true;
            }

            // 不透明度
            float no = IndentedSlider("不透明度", layer.UIImageClipOpacity, 0f, 1f, indent);
            if (!Mathf.Approximately(no, layer.UIImageClipOpacity))
            {
                layer.UIImageClipOpacity = no;
                _needsRecomposite = true;
            }
        }

        /// <summary>グラデーションマップの有効トグル・グラデーション編集・適用率。</summary>
        void DrawGradientMapControls(PSDLayer layer, int indent)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(indent * IndentWidth + 18f);
            bool en = EditorGUILayout.ToggleLeft("グラデーションマップ", layer.UIGradientMapEnabled,
                                                 GUILayout.Height(RowH));
            EditorGUILayout.EndHorizontal();
            RowSpace();
            if (en != layer.UIGradientMapEnabled)
            {
                layer.UIGradientMapEnabled = en;
                if (en) EnsureGradientLut(layer);   // 初回有効化時に LUT を焼く
                _needsRecomposite = true;
            }
            if (!en) return;

            if (layer.UIGradient == null) layer.UIGradient = CreateDefaultGradient();

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(indent * IndentWidth + 18f);
            bool normalize = EditorGUILayout.ToggleLeft(
                new GUIContent("輝度を正規化", "レイヤーの最暗色〜最明色を 0..1 にストレッチしてからグラデーションを適用する"),
                layer.UIGradientMapNormalize, GUILayout.Height(RowH));
            EditorGUILayout.EndHorizontal();
            RowSpace();
            if (normalize != layer.UIGradientMapNormalize)
            {
                layer.UIGradientMapNormalize = normalize;
                if (normalize) ComputeGradientLumRange(layer);
                _needsRecomposite = true;
            }

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(indent * IndentWidth + 18f);
            GUILayout.Label("階調", PSDEditorTheme.ControlLabelStyle,
                            GUILayout.Width(48), GUILayout.Height(RowH));
            EditorGUI.BeginChangeCheck();
            Gradient ng = EditorGUILayout.GradientField(layer.UIGradient, GUILayout.Height(RowH));
            bool gradientChanged = EditorGUI.EndChangeCheck();
            EditorGUILayout.EndHorizontal();
            RowSpace();
            if (gradientChanged)
            {
                layer.UIGradient = ng;
                BakeGradientLut(layer);
                _needsRecomposite = true;
            }

            float no = IndentedSlider("適用率", layer.UIGradientMapOpacity, 0f, 1f, indent);
            if (!Mathf.Approximately(no, layer.UIGradientMapOpacity))
            {
                layer.UIGradientMapOpacity = no;
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

        /// <summary>グラデーション有効時に LUT が無ければ焼く。</summary>
        void EnsureGradientLut(PSDLayer layer)
        {
            if (layer.UIGradient == null) layer.UIGradient = CreateDefaultGradient();
            if (layer._gradientLut == null) BakeGradientLut(layer);
        }

        /// <summary>UIGradient を 256×1 の LUT テクスチャ (linear) に焼き込む。</summary>
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
                px[i] = layer.UIGradient.Evaluate(i / (float)(N - 1));
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

        /// <summary>インデント付きのラベル + スライダー 1 行 (行間の縦余白付き)。</summary>
        static float IndentedSlider(string label, float value, float min, float max, int indent)
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
