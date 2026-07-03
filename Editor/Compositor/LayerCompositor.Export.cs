using UnityEngine;

namespace PSDSimpleEditor
{
    // ════════════════════════════════════════════════════════════════
    //  書き出し用: レイヤー単独を色調補正・グラデーションマップ込みで焼き込む /
    //  クリップマスク用: 実効 α の単独描画
    // ════════════════════════════════════════════════════════════════
    // ─── partial 見取り図 ───────────────────────────────────────────
    // 責務   : レイヤー単独の色調補正等の焼き込み書き出し、および実効 α の単独描画処理
    // 宣言   : なし
    // 参照   : _mat (R)
    // 依存   : AcquireRT (RT.cs), ReleaseToPool (RT.cs), NewParams (Params.cs), ApplyParams (Params.cs) 等
    // ────────────────────────────────────────────────────────────────
    public partial class LayerCompositor
    {
        /// <summary>
        /// レイヤーを透明 RT へ Normal ブレンドで単独描画し、実効 α
        /// (レイヤー α × マスク × 不透明度 × clipMask) を持つ RT を返す。
        /// 返却 RT は使用後 ReleaseToPool すること。
        /// </summary>
        RenderTexture RenderLayerAlpha(PSDLayer layer, RenderTexture clipMask)
        {
            Texture tex;
            Vector4 rect;
            bool    gradFill = false;
            if (layer.Adjustment.HasSolidColor)
            {
                tex  = GetSolidTexture(layer.Adjustment.SolidColor);
                rect = FullCanvasRect;
            }
            else if (layer.Adjustment.HasGradientFill && layer.Runtime.GradientFillLut != null)
            {
                // GdFl: α はグラデーション LUT (透明ストップ) が決める
                tex      = Texture2D.whiteTexture;
                rect     = FullCanvasRect;
                gradFill = true;
            }
            else if (layer.Texture != null)
            {
                tex  = layer.Texture;
                rect = LayerRectOf(layer);
            }
            else
            {
                // ピクセルを持たないベース層 (調整レイヤー等) → 実効 α は全面 0
                return AcquireRT(clearToTransparent: true);
            }

            var clearBg = AcquireRT(clearToTransparent: true);
            var result  = AcquireRT(clearToTransparent: false);

            var p = NewParams();
            p.LayerTex  = tex;
            p.LayerRect = rect;
            p.Opacity   = layer.UI.Opacity;
            p.BlendMode = 0; // Normal (α の算出にブレンド関数は影響しない)
            SetMaskFrom(ref p, layer);
            p.ClipMaskTex = clipMask;
            if (gradFill) SetGradientFillFrom(ref p, layer);
            ApplyParams(p);
            Graphics.Blit(clearBg, result, _mat);

            ReleaseToPool(clearBg);
            return result;
        }

        /// <summary>
        /// 平坦化済みグループなど全面テクスチャの実効 α (× 不透明度 × マスク) を持つ RT を返す。
        /// </summary>
        RenderTexture RenderTextureAlpha(Texture tex, PSDLayer layer, RenderTexture clipMask)
        {
            var clearBg = AcquireRT(clearToTransparent: true);
            var result  = AcquireRT(clearToTransparent: false);

            var p = NewParams();
            p.LayerTex  = tex;
            p.LayerRect = FullCanvasRect;
            p.Opacity   = layer.UI.Opacity;
            p.BlendMode = 0; // Normal
            SetMaskFrom(ref p, layer);
            p.ClipMaskTex = clipMask;
            ApplyParams(p);
            Graphics.Blit(clearBg, result, _mat);

            ReleaseToPool(clearBg);
            return result;
        }

        /// <summary>
        /// ピクセルレイヤーを「自身のサイズ (1:1)」のキャンバスへ Normal・不透明度 1・マスク/クリップ無しで
        /// 単独描画し、色調補正とグラデーションマップを焼き込んだ結果を
        /// トップダウン (PSD 向き = 行 0 が上端) の Color32[] (Width×Height) で返す。
        /// ブレンドモード/不透明度/マスクは PSD 側プロパティとして保持するため、ここでは適用しない。
        /// ピクセルを持たないレイヤーは null。
        /// </summary>
        public Color32[] RenderLayerForExport(PSDLayer layer)
        {
            if (!IsValid || layer == null || layer.Texture == null) return null;
            int lw = layer.Width, lh = layer.Height;
            if (lw <= 0 || lh <= 0) return null;

            bool prevSRGBWrite = GL.sRGBWrite;
            var  prevActive    = RenderTexture.active;
            GL.sRGBWrite = false;

            RenderTexture dst   = null, clear = null;
            Texture2D     tmp   = null;
            try
            {
                dst   = RenderTexture.GetTemporary(lw, lh, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                clear = RenderTexture.GetTemporary(lw, lh, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                ClearRT(clear);

                // レイヤーを 1:1 で全面配置。補正・グラデーションマップのみ適用。
                var p = NewParams();
                p.LayerTex  = layer.Texture;
                p.LayerRect = new Vector4(0, 0, lw, lh);
                p.Opacity   = 1f;
                p.BlendMode = 0; // Normal
                SetAdjustmentsFrom(ref p, layer); // 色調補正 + グラデーション LUT
                ApplyParams(p, new Vector4(lw, lh, 0, 0));
                Graphics.Blit(clear, dst, _mat);

                tmp = new Texture2D(lw, lh, TextureFormat.RGBA32, false, linear: true)
                { hideFlags = HideFlags.HideAndDontSave };
                RenderTexture.active = dst;
                tmp.ReadPixels(new Rect(0, 0, lw, lh), 0, 0);
                tmp.Apply(false);

                // GetPixels32 はボトムアップ (行 0 = 下端)。PSD 向きのトップダウンへ反転。
                var bottomUp = tmp.GetPixels32();
                var topDown  = new Color32[lw * lh];
                for (int y = 0; y < lh; y++)
                    System.Array.Copy(bottomUp, (lh - 1 - y) * lw, topDown, y * lw, lw);
                return topDown;
            }
            finally
            {
                RenderTexture.active = prevActive;
                if (tmp   != null) Object.DestroyImmediate(tmp);
                if (dst   != null) RenderTexture.ReleaseTemporary(dst);
                if (clear != null) RenderTexture.ReleaseTemporary(clear);
                GL.sRGBWrite = prevSRGBWrite;
            }
        }

        /// <summary>
        /// 画像クリップ合成の元画像を「ベースレイヤーのサイズ (Width×Height)」へタイル展開し、
        /// トップダウン (PSD 向き = 行 0 が上端) の Color32[] で返す。
        /// ブレンドモード/不透明度/クリッピングは PSD 側プロパティとして保持するため、ここでは
        /// 適用しない (Normal・不透明度 1・素のタイル画像)。クリップ無効/ピクセル無しは null。
        /// </summary>
        public Color32[] RenderImageClipForExport(PSDLayer baseLayer)
        {
            if (!IsValid || baseLayer == null) return null;
            if (!baseLayer.UI.ImageClipEnabled || baseLayer.UI.ImageClipTex == null) return null;
            int lw = baseLayer.Width, lh = baseLayer.Height;
            if (lw <= 0 || lh <= 0) return null;

            bool prevSRGBWrite = GL.sRGBWrite;
            var  prevActive    = RenderTexture.active;
            GL.sRGBWrite = false;

            RenderTexture dst   = null, clear = null;
            Texture2D     tmp   = null;
            try
            {
                dst   = RenderTexture.GetTemporary(lw, lh, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                clear = RenderTexture.GetTemporary(lw, lh, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                ClearRT(clear);

                // クリップ画像を全面 (0,0,lw,lh) へタイル展開。ブレンド/不透明度/クリップは適用しない。
                var p = NewParams();
                p.LayerTex  = baseLayer.UI.ImageClipTex;
                p.LayerRect = new Vector4(0, 0, lw, lh);
                p.LayerWrap = true;
                p.LayerTile = baseLayer.UI.ImageClipTile;
                p.Opacity   = 1f;
                p.BlendMode = 0; // Normal
                ApplyParams(p, new Vector4(lw, lh, 0, 0));
                Graphics.Blit(clear, dst, _mat);

                tmp = new Texture2D(lw, lh, TextureFormat.RGBA32, false, linear: true)
                { hideFlags = HideFlags.HideAndDontSave };
                RenderTexture.active = dst;
                tmp.ReadPixels(new Rect(0, 0, lw, lh), 0, 0);
                tmp.Apply(false);

                // GetPixels32 はボトムアップ (行 0 = 下端)。PSD 向きのトップダウンへ反転。
                var bottomUp = tmp.GetPixels32();
                var topDown  = new Color32[lw * lh];
                for (int y = 0; y < lh; y++)
                    System.Array.Copy(bottomUp, (lh - 1 - y) * lw, topDown, y * lw, lw);
                return topDown;
            }
            finally
            {
                RenderTexture.active = prevActive;
                if (tmp   != null) Object.DestroyImmediate(tmp);
                if (dst   != null) RenderTexture.ReleaseTemporary(dst);
                if (clear != null) RenderTexture.ReleaseTemporary(clear);
                GL.sRGBWrite = prevSRGBWrite;
            }
        }
    }
}
