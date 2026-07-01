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
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(indent * IndentWidth + 18f);
            layer.UIAdjustExpanded = EditorGUILayout.Foldout(layer.UIAdjustExpanded, "色調補正", true);
            EditorGUILayout.EndHorizontal();
            if (!layer.UIAdjustExpanded) return;

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

            DrawGradientMapControls(layer, ci);
            DrawImageClipControls(layer, ci);
        }

        /// <summary>「着色」トグル。ON で絶対値の色相・彩度を強制し、白黒 (彩度0) にも色が乗る。</summary>
        void DrawColorizeToggle(PSDLayer layer, int indent)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(indent * IndentWidth + 18f);
            bool en = EditorGUILayout.ToggleLeft("着色 (白黒にも色を入れる)", layer.UIColorize);
            EditorGUILayout.EndHorizontal();
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
            bool en = EditorGUILayout.ToggleLeft("階調反転", layer.UIInvert);
            EditorGUILayout.EndHorizontal();
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
            bool en = EditorGUILayout.ToggleLeft("しきい値", layer.UIThresholdEnabled);
            EditorGUILayout.EndHorizontal();
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
            bool en = EditorGUILayout.ToggleLeft("ポスタリゼーション", layer.UIPosterizeEnabled);
            EditorGUILayout.EndHorizontal();
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

        /// <summary>画像クリップ合成: 任意画像をレイヤーα形状へクリップ・タイリング・ブレンド。</summary>
        void DrawImageClipControls(PSDLayer layer, int indent)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(indent * IndentWidth + 18f);
            bool en = EditorGUILayout.ToggleLeft("画像クリップ合成", layer.UIImageClipEnabled);
            EditorGUILayout.EndHorizontal();
            if (en != layer.UIImageClipEnabled)
            {
                layer.UIImageClipEnabled = en;
                _needsRecomposite = true;
            }
            if (!en) return;

            // 画像
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(indent * IndentWidth + 18f);
            GUILayout.Label("画像", EditorStyles.miniLabel, GUILayout.Width(48));
            var tex = (Texture2D)EditorGUILayout.ObjectField(layer.UIImageClipTex, typeof(Texture2D), false);
            EditorGUILayout.EndHorizontal();
            if (tex != layer.UIImageClipTex)
            {
                layer.UIImageClipTex = tex;
                _needsRecomposite = true;
            }

            // タイル反復数 (X,Y)
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(indent * IndentWidth + 18f);
            GUILayout.Label("タイル", EditorStyles.miniLabel, GUILayout.Width(48));
            Vector2 nt = EditorGUILayout.Vector2Field(GUIContent.none, layer.UIImageClipTile);
            EditorGUILayout.EndHorizontal();
            if (nt != layer.UIImageClipTile)
            {
                // 0 / 負値はタイリングが破綻するため下限でクランプ
                layer.UIImageClipTile = new Vector2(Mathf.Max(0.01f, nt.x), Mathf.Max(0.01f, nt.y));
                _needsRecomposite = true;
            }

            // 合成モード
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(indent * IndentWidth + 18f);
            GUILayout.Label("合成", EditorStyles.miniLabel, GUILayout.Width(48));
            BlendMode[] modes  = _blendModesNormal;
            string[]    labels = _blendLabelsNormal ?? (_blendLabelsNormal = BuildBlendLabels(_blendModesNormal));
            int curIndex = Mathf.Max(0, Array.IndexOf(modes, layer.UIImageClipBlend));
            int newIndex = EditorGUILayout.Popup(curIndex, labels);
            EditorGUILayout.EndHorizontal();
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
            bool en = EditorGUILayout.ToggleLeft("グラデーションマップ", layer.UIGradientMapEnabled);
            EditorGUILayout.EndHorizontal();
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
            GUILayout.Label("階調", EditorStyles.miniLabel, GUILayout.Width(48));
            EditorGUI.BeginChangeCheck();
            Gradient ng = EditorGUILayout.GradientField(layer.UIGradient);
            bool gradientChanged = EditorGUI.EndChangeCheck();
            EditorGUILayout.EndHorizontal();
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

        /// <summary>インデント付きのラベル + スライダー 1 行。</summary>
        static float IndentedSlider(string label, float value, float min, float max, int indent)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(indent * IndentWidth + 18f);
            GUILayout.Label(label, EditorStyles.miniLabel, GUILayout.Width(48));
            float result = EditorGUILayout.Slider(value, min, max);
            EditorGUILayout.EndHorizontal();
            return result;
        }
    }
}
