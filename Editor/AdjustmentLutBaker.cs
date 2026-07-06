using System.Collections.Generic;
using UnityEngine;

namespace PSDSimpleEditor
{
    /// <summary>
    /// 非破壊色調補正の LUT ベイク処理 (トーンカーブ / グラデーションマップ / GdFl)。
    /// UI から独立した純粋な計算ロジックで、PSD ロード直後 (BakeImportedLuts) と
    /// パラメーター編集時 (PSDSimpleEditorWindow.Adjustments / AdjustmentClipboard) の両方から呼ばれる。
    /// 生成する LUT テクスチャは layer.Runtime にキャッシュされ、HideAndDontSave のため
    /// 破棄はウィンドウ側 (Cleanup) の責務。
    /// </summary>
    internal static class AdjustmentLutBaker
    {
        internal static AnimationCurve CreateDefaultCurve()
        {
            var c = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 1f));
            for (int i = 0; i < c.length; i++) c.SmoothTangents(i, 0f);
            return c;
        }

        internal static Gradient CreateDefaultGradient()
        {
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.black, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f),          new GradientAlphaKey(1f, 1f) });
            return g;
        }

        /// <summary>PSD からインポートしたグラデーションマップ (grdm) / グラデーション塗りつぶし (GdFl) /
        /// トーンカーブ (curv、畳み戻されたものを含む) の LUT をロード直後に焼く (ツリー再帰)。</summary>
        internal static void BakeImportedLuts(List<PSDLayer> layers)
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

        // ── トーンカーブ ───────────────────────────────────────────────────

        /// <summary>トーンカーブ有効時に LUT が無ければ焼く。</summary>
        internal static void EnsureCurveLut(PSDLayer layer)
        {
            if (layer.UI.Curve == null) layer.UI.Curve = CreateDefaultCurve();
            if (layer.Runtime.CurveLut == null) BakeCurveLut(layer);
        }

        /// <summary>UI.Curve (+ パース済みチャンネル別カーブ) を 256×1 の LUT テクスチャに焼き込む。
        /// R/G/B 各チャンネル値 = 複合カーブ(チャンネルカーブ(入力))。チャンネルカーブが無い場合は R=G=B。</summary>
        internal static void BakeCurveLut(PSDLayer layer)
        {
            const int N = 256;
            if (layer.Runtime.CurveLut == null)
            {
                layer.Runtime.CurveLut = new Texture2D(N, 1, TextureFormat.RGBA32, false, linear: true)
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
            layer.Runtime.CurveLut.SetPixels32(px);
            layer.Runtime.CurveLut.Apply(false);
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

        // ── グラデーションマップ / GdFl ────────────────────────────────────

        /// <summary>グラデーション有効時に LUT が無ければ焼く。</summary>
        internal static void EnsureGradientLut(PSDLayer layer)
        {
            if (layer.UI.Gradient == null) layer.UI.Gradient = CreateDefaultGradient();
            if (layer.Runtime.GradientLut == null) BakeGradientLut(layer);
        }

        /// <summary>UI.Gradient を 256×1 の LUT テクスチャ (linear) に焼き込む。</summary>
        internal static void BakeGradientLut(PSDLayer layer)
        {
            const int N = 256;
            if (layer.Runtime.GradientLut == null)
            {
                layer.Runtime.GradientLut = new Texture2D(N, 1, TextureFormat.RGBA32, false, linear: true)
                {
                    hideFlags  = HideFlags.HideAndDontSave,
                    wrapMode   = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                };
            }
            var px = new Color32[N];
            for (int i = 0; i < N; i++)
                px[i] = layer.UI.Gradient.Evaluate(i / (float)(N - 1));
            layer.Runtime.GradientLut.SetPixels32(px);
            layer.Runtime.GradientLut.Apply(false);
        }

        /// <summary>GradientFillGradient を 256×1 の LUT テクスチャ (linear) に焼き込む (GdFl 用)。</summary>
        internal static void BakeGradientFillLut(PSDLayer layer)
        {
            const int N = 256;
            if (layer.Runtime.GradientFillLut == null)
            {
                layer.Runtime.GradientFillLut = new Texture2D(N, 1, TextureFormat.RGBA32, false, linear: true)
                {
                    hideFlags  = HideFlags.HideAndDontSave,
                    wrapMode   = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                };
            }
            var px = new Color32[N];
            for (int i = 0; i < N; i++)
                px[i] = layer.Adjustment.GradientFillGradient.Evaluate(i / (float)(N - 1));
            layer.Runtime.GradientFillLut.SetPixels32(px);
            layer.Runtime.GradientFillLut.Apply(false);
        }

        /// <summary>
        /// 輝度正規化トグル ON 時に、レイヤーの不透明画素から輝度の最小・最大を求めて layer にキャッシュする。
        /// (完全透明画素は未定義色のことがあるため範囲計算から除外。不透明画素が無ければ 0..1 のまま = 無効果)
        /// </summary>
        internal static void ComputeGradientLumRange(PSDLayer layer)
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
            layer.Runtime.GradientLumMin = min;
            layer.Runtime.GradientLumMax = max;
        }
    }
}
