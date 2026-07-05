using UnityEngine;

namespace PSDSimpleEditor
{
    /// <summary>
    /// 色域選択マスク生成 (Photoshop の「色域指定」相当)。
    /// 対象レイヤー自身の画素を走査し、対象色との RGB 距離が閾値内なら選択 (白)、
    /// それ以外は非選択 (黒) としたグレースケールマスクを CPU で生成する。
    /// プレビュー用にはハイライト色のオーバーレイ画素も生成できる。
    /// </summary>
    public static class ColorRangeMask
    {
        // RGB ユークリッド距離の最大値 (255,255,255 同士) を正規化するための除数。
        // sqrt(3) * 255。dist/__ で 0..1 に収める。
        const float MaxDist = 441.6729559f; // Mathf.Sqrt(3f) * 255f

        /// <summary>
        /// layer.Texture (RGBA32・ボトムアップ) を走査し、対象色 ± 閾値の選択範囲を
        /// グレースケール (rgb = 選択値, a = 255) の Color32[] として返す (ボトムアップ順のまま)。
        /// 透明画素 (a == 0) は常に非選択。閾値 0 = 完全一致のみ、大きいほど広く選択。
        /// 対象が走査不能 (テクスチャなし / サイズ 0) のときは null を返す。
        /// cachedSrc に GetSourcePixels の結果を渡すと GetPixels32 (テクスチャ全体の読み出し)
        /// を省略できる (スライダードラッグ中の連続更新用)。
        /// </summary>
        public static Color32[] BuildMaskPixels(PSDLayer layer, Color target, float threshold,
                                                out int w, out int h, Color32[] cachedSrc = null)
        {
            if (!TryGetSource(layer, cachedSrc, out var src, out w, out h)) return null;

            float thSq = Sq(Mathf.Clamp01(threshold) * MaxDist);
            Color32 t  = target;

            int n = w * h;
            var dst = new Color32[n];
            for (int i = 0; i < n; i++)
            {
                byte v = IsSelected(src[i], t, thSq) ? (byte)255 : (byte)0;
                dst[i] = new Color32(v, v, v, 255);
            }
            return dst;
        }

        /// <summary>
        /// プレビュー用: 選択画素を highlight 色、非選択画素を透明としたオーバーレイの
        /// Color32[] を返す (ボトムアップ順のまま)。null 条件・cachedSrc は BuildMaskPixels と同じ。
        /// </summary>
        public static Color32[] BuildHighlightPixels(PSDLayer layer, Color target, float threshold,
                                                     Color highlight, out int w, out int h,
                                                     Color32[] cachedSrc = null)
        {
            if (!TryGetSource(layer, cachedSrc, out var src, out w, out h)) return null;

            float thSq        = Sq(Mathf.Clamp01(threshold) * MaxDist);
            Color32 t         = target;
            Color32 hi        = highlight;
            Color32 clear     = new Color32(0, 0, 0, 0);

            int n = w * h;
            var dst = new Color32[n];
            for (int i = 0; i < n; i++)
                dst[i] = IsSelected(src[i], t, thSq) ? hi : clear;
            return dst;
        }

        /// <summary>
        /// レイヤーの走査元ピクセル (ボトムアップ) を取得する。呼び出し側でキャッシュして
        /// cachedSrc へ渡すことで、パラメータ変更のたびの GetPixels32 を回避できる。
        /// 走査不能のときは null。
        /// </summary>
        public static Color32[] GetSourcePixels(PSDLayer layer)
        {
            return TryGetSource(layer, null, out var src, out _, out _) ? src : null;
        }

        // ── 内部 ──────────────────────────────────────────────────────────

        static bool TryGetSource(PSDLayer layer, Color32[] cachedSrc, out Color32[] src, out int w, out int h)
        {
            src = null; w = 0; h = 0;
            if (layer == null || layer.Texture == null) return false;
            w = layer.Width;
            h = layer.Height;
            if (w <= 0 || h <= 0) return false;
            src = cachedSrc != null && cachedSrc.Length >= w * h
                ? cachedSrc
                : layer.Texture.GetPixels32(); // ボトムアップ
            if (src == null || src.Length < w * h) { src = null; return false; }
            return true;
        }

        // 透明は非選択。それ以外は RGB ユークリッド距離の平方で閾値比較 (sqrt を省く)。
        static bool IsSelected(Color32 c, Color32 t, float thSq)
        {
            if (c.a == 0) return false;
            float dr = c.r - t.r;
            float dg = c.g - t.g;
            float db = c.b - t.b;
            return dr * dr + dg * dg + db * db <= thSq;
        }

        static float Sq(float x) => x * x;
    }
}
