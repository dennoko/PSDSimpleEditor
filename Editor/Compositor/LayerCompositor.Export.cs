using System.Collections.Generic;
using UnityEngine;

namespace PSDSimpleEditor
{
    // ════════════════════════════════════════════════════════════════
    //  書き出し用: レイヤー単独を色調補正・グラデーションマップ込みで焼き込む /
    //  クリップマスク用: 実効 α の単独描画
    // ════════════════════════════════════════════════════════════════
    // ─── partial 見取り図 ───────────────────────────────────────────
    // 責務   : レイヤー単独の色調補正等の焼き込み書き出し、実効 α の単独描画処理、
    //          選択フラッシュ用ハイライト RT の生成
    // 宣言   : なし
    // 参照   : _mat (R), _selectionHighlightRT (RW)
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
        /// 選択フラッシュ用: 指定レイヤー群の「ピクセルを持つ部分」を color で塗った全面 RT を返す。
        /// 各レイヤーを Normal・不透明度 1 で透明バッファへ重ね描きして α の和集合を作り、
        /// その α をクリップマスクにして単色を切り抜く。マスク・不透明度・表示状態は反映しない
        /// (「レイヤーがどこにあるか」の提示が目的のため)。
        /// 返却 RT はコンポジターが所有する。内容は次回呼び出しで上書きされ、Dispose で破棄される。
        /// ピクセルを持つレイヤーが 1 枚も無い場合は null。
        /// </summary>
        public RenderTexture RenderSelectionHighlight(IReadOnlyList<PSDLayer> layers, Color color)
        {
            if (!IsValid || layers == null) return null;

            bool anyPixels = false;
            foreach (var l in layers)
                if (l != null && l.Texture != null) { anyPixels = true; break; }
            if (!anyPixels) return null;

            bool prevSRGBWrite = GL.sRGBWrite;
            var  prevActive    = RenderTexture.active;
            GL.sRGBWrite = false;

            RenderTexture cur = null, next = null;
            try
            {
                // 1) α の和集合: 各レイヤーを Normal・不透明度 1 で透明バッファへ重ね描き
                cur  = AcquireRT(clearToTransparent: true);
                next = AcquireRT(clearToTransparent: false);
                foreach (var layer in layers)
                {
                    if (layer == null || layer.Texture == null) continue;
                    var p = NewParams();
                    p.LayerTex  = layer.Texture;
                    p.LayerRect = LayerRectOf(layer);
                    p.Opacity   = 1f;
                    p.BlendMode = 0; // Normal (α の和集合を作るだけ)
                    ApplyParams(p);
                    Graphics.Blit(cur, next, _mat);
                    Swap(ref cur, ref next);
                }

                // 2) 和集合 α をクリップマスクにして単色を全面へ描く
                if (_selectionHighlightRT == null)
                {
                    _selectionHighlightRT = new RenderTexture(_canvasW, _canvasH, 0,
                        RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
                    { hideFlags = HideFlags.HideAndDontSave };
                    _selectionHighlightRT.Create();
                }
                ClearRT(next);
                var sp = NewParams();
                sp.LayerTex    = GetSolidTexture(color);
                sp.LayerRect   = FullCanvasRect;
                sp.Opacity     = 1f;
                sp.BlendMode   = 0; // Normal
                sp.ClipMaskTex = cur;
                ApplyParams(sp);
                Graphics.Blit(next, _selectionHighlightRT, _mat);
                return _selectionHighlightRT;
            }
            finally
            {
                ReleaseToPool(cur);
                ReleaseToPool(next);
                RenderTexture.active = prevActive;
                GL.sRGBWrite = prevSRGBWrite;
            }
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
