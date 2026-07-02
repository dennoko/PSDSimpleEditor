using UnityEngine;

namespace PSDSimpleEditor
{
    // ════════════════════════════════════════════════════════════════
    //  uniform 一括設定 (Material はステートフルなため毎回全 uniform を明示設定)
    // ════════════════════════════════════════════════════════════════
    public partial class LayerCompositor
    {
        /// <summary>1 回の Blit に必要な全 uniform 値。未設定スロットは安全な既定値になる。</summary>
        struct DrawParams
        {
            public Texture LayerTex;     // null → 黒テクスチャ (未使用スロットの掃除)
            public Vector4 LayerRect;    // (L, T, W, H) PSD 座標
            public Vector2 LayerTile;    // タイル反復数 (LayerWrap=true のとき有効)
            public bool    LayerWrap;    // true → レイヤー矩形内で frac タイリング
            public float   Opacity;      // 0..1
            public int     BlendMode;    // BlendMode enum の int 値 (シェーダー分岐番号)
            public bool    IsAdjustment;
            public Texture MaskTex;      // null → _HasMask = 0
            public Vector4 MaskRect;
            public float   MaskDefault;  // 0..1
            public Texture ClipMaskTex;  // null → _HasClipMask = 0
            public float   Brightness, Contrast, Hue, Saturation, Lightness; // 正規化済み -1..1
            public bool    Colorize;     // true → 絶対値の色相・彩度を強制 (白黒着色)
            public bool    Invert;       // true → 階調反転
            public bool    Threshold;    // true → しきい値有効
            public float   ThresholdLevel;   // 0..1 (実値/255)
            public bool    Posterize;    // true → ポスタリゼーション有効
            public float   PosterizeLevels;  // 2..255
            public float   LevelsInBlack, LevelsInWhite, LevelsGamma, LevelsOutBlack, LevelsOutWhite; // 0..1 (Gamma は実値)
            public Vector3 LevelsChInBlack, LevelsChInWhite, LevelsChGamma, LevelsChOutBlack, LevelsChOutWhite; // R/G/B 別 (既定は恒等)
            public Texture CurveLutTex;  // null → トーンカーブ無効
            public Texture GradFillTex;    // null → グラデーション塗りつぶし無効
            public Vector4 GradFillParams; // (cosθ, sinθ, 円形フラグ, スケール)
            public Texture GradientMapTex;     // null → グラデーションマップ無効
            public float   GradientMapOpacity; // 0..1
            public bool    GradientMapNormalize; // true → 輝度を [LumMin,LumMax] → [0,1] に正規化してから LUT を引く
            public float   GradientMapLumMin, GradientMapLumMax; // 正規化レンジ (0..1)
            public bool    ColorBalance;         // true → カラーバランス有効
            public Vector3 CBShadows, CBMidtones, CBHighlights; // (CR,MG,YB) -1..1
            public bool    CBPreserveLum;        // true → 輝度保持
        }

        static DrawParams NewParams()
        {
            return new DrawParams
            {
                LayerTex    = null,
                LayerRect   = new Vector4(0, 0, 1, 1),
                LayerTile   = Vector2.one,
                LayerWrap   = false,
                Opacity     = 1f,
                BlendMode   = 0,
                IsAdjustment = false,
                MaskTex     = null,
                MaskRect    = new Vector4(0, 0, 1, 1),
                MaskDefault = 1f,
                ClipMaskTex = null,
                Brightness = 0f, Contrast = 0f, Hue = 0f, Saturation = 0f, Lightness = 0f,
                Colorize = false,
                Invert = false,
                Threshold = false, ThresholdLevel = 0.5f,
                Posterize = false, PosterizeLevels = 4f,
                LevelsInBlack = 0f, LevelsInWhite = 1f, LevelsGamma = 1f,
                LevelsOutBlack = 0f, LevelsOutWhite = 1f,
                LevelsChInBlack = Vector3.zero, LevelsChInWhite = Vector3.one,
                LevelsChGamma = Vector3.one,
                LevelsChOutBlack = Vector3.zero, LevelsChOutWhite = Vector3.one,
                CurveLutTex = null,
                GradFillTex = null, GradFillParams = new Vector4(1, 0, 0, 1),
                GradientMapTex = null, GradientMapOpacity = 1f,
                GradientMapNormalize = false, GradientMapLumMin = 0f, GradientMapLumMax = 1f,
                ColorBalance = false,
                CBShadows = Vector3.zero, CBMidtones = Vector3.zero, CBHighlights = Vector3.zero,
                CBPreserveLum = true
            };
        }

        /// <summary>
        /// 全関連 uniform を毎回明示的に設定する (前レイヤーの値が残る事故の防止)。
        /// </summary>
        void ApplyParams(in DrawParams p)
            => ApplyParams(p, new Vector4(_canvasW, _canvasH, 0, 0));

        /// <summary>キャンバスサイズを明示指定する版 (レイヤー単独の 1:1 書き出しレンダリング用)。</summary>
        void ApplyParams(in DrawParams p, Vector4 canvasSize)
        {
            _mat.SetVector("_CanvasSize", canvasSize);

            // レイヤー
            _mat.SetTexture("_LayerTex", p.LayerTex != null ? p.LayerTex : Texture2D.blackTexture);
            _mat.SetVector ("_LayerRect", p.LayerRect);
            _mat.SetVector ("_LayerTile", new Vector4(p.LayerTile.x, p.LayerTile.y, 0, 0));
            _mat.SetInt    ("_LayerWrap", p.LayerWrap ? 1 : 0);
            _mat.SetFloat  ("_Opacity",   Mathf.Clamp01(p.Opacity));
            _mat.SetInt    ("_BlendMode", p.BlendMode);
            _mat.SetInt    ("_IsAdjustment", p.IsAdjustment ? 1 : 0);

            // レイヤーマスク (無効マスクは CPU 側で MaskTex = null とし _HasMask = 0 にする)
            bool hasMask = p.MaskTex != null;
            _mat.SetInt    ("_HasMask",     hasMask ? 1 : 0);
            _mat.SetTexture("_MaskTex",     hasMask ? p.MaskTex : (Texture)Texture2D.whiteTexture);
            _mat.SetVector ("_MaskRect",    hasMask ? p.MaskRect : new Vector4(0, 0, 1, 1));
            _mat.SetFloat  ("_MaskDefault", hasMask ? Mathf.Clamp01(p.MaskDefault) : 1f);

            // クリッピングマスク
            bool hasClip = p.ClipMaskTex != null;
            _mat.SetInt    ("_HasClipMask", hasClip ? 1 : 0);
            _mat.SetTexture("_ClipMaskTex", hasClip ? p.ClipMaskTex : (Texture)Texture2D.whiteTexture);

            // 色調補正 (通常パスでもレイヤー色へ適用される。REWRITE_SPEC.md §3)
            _mat.SetFloat("_Brightness", Mathf.Clamp(p.Brightness, -1f, 1f));
            _mat.SetFloat("_Contrast",   Mathf.Clamp(p.Contrast,   -1f, 1f));
            _mat.SetFloat("_Hue",        Mathf.Clamp(p.Hue,        -1f, 1f));
            _mat.SetFloat("_Saturation", Mathf.Clamp(p.Saturation, -1f, 1f));
            _mat.SetFloat("_Lightness",  Mathf.Clamp(p.Lightness,  -1f, 1f));
            _mat.SetInt  ("_Colorize",   p.Colorize ? 1 : 0);
            _mat.SetInt  ("_HasInvert",    p.Invert ? 1 : 0);
            _mat.SetInt  ("_HasThreshold", p.Threshold ? 1 : 0);
            _mat.SetFloat("_ThresholdLevel", Mathf.Clamp01(p.ThresholdLevel));
            _mat.SetInt  ("_HasPosterize", p.Posterize ? 1 : 0);
            _mat.SetFloat("_PosterizeLevels", Mathf.Max(2f, p.PosterizeLevels));
            _mat.SetFloat("_LevelsInBlack",  Mathf.Clamp01(p.LevelsInBlack));
            _mat.SetFloat("_LevelsInWhite",  Mathf.Clamp01(p.LevelsInWhite));
            _mat.SetFloat("_LevelsGamma",    Mathf.Max(0.01f, p.LevelsGamma));
            _mat.SetFloat("_LevelsOutBlack", Mathf.Clamp01(p.LevelsOutBlack));
            _mat.SetFloat("_LevelsOutWhite", Mathf.Clamp01(p.LevelsOutWhite));
            _mat.SetVector("_LevelsChInBlack",  p.LevelsChInBlack);
            _mat.SetVector("_LevelsChInWhite",  p.LevelsChInWhite);
            _mat.SetVector("_LevelsChGamma",    p.LevelsChGamma);
            _mat.SetVector("_LevelsChOutBlack", p.LevelsChOutBlack);
            _mat.SetVector("_LevelsChOutWhite", p.LevelsChOutWhite);

            // トーンカーブ (LUT 未設定時は無効)
            bool hasCurve = p.CurveLutTex != null;
            _mat.SetInt    ("_HasCurveLut", hasCurve ? 1 : 0);
            _mat.SetTexture("_CurveLutTex", hasCurve ? p.CurveLutTex : (Texture)Texture2D.whiteTexture);

            // グラデーションマップ (LUT 未設定時は無効)
            bool hasGrad = p.GradientMapTex != null;
            _mat.SetInt    ("_HasGradientMap",     hasGrad ? 1 : 0);
            _mat.SetTexture("_GradientMapTex",     hasGrad ? p.GradientMapTex : (Texture)Texture2D.whiteTexture);
            _mat.SetFloat  ("_GradientMapOpacity", Mathf.Clamp01(p.GradientMapOpacity));
            _mat.SetInt    ("_GradientMapNormalize", p.GradientMapNormalize ? 1 : 0);
            _mat.SetFloat  ("_GradientMapLumMin", Mathf.Clamp01(p.GradientMapLumMin));
            _mat.SetFloat  ("_GradientMapLumMax", Mathf.Clamp01(p.GradientMapLumMax));

            // カラーバランス
            _mat.SetInt    ("_HasColorBalance", p.ColorBalance ? 1 : 0);
            _mat.SetVector ("_CBShadows",    p.CBShadows);
            _mat.SetVector ("_CBMidtones",   p.CBMidtones);
            _mat.SetVector ("_CBHighlights", p.CBHighlights);
            _mat.SetInt    ("_CBPreserveLum", p.CBPreserveLum ? 1 : 0);

            // グラデーション塗りつぶし (GdFl。LUT 未設定時は無効)
            bool hasGradFill = p.GradFillTex != null;
            _mat.SetInt    ("_HasGradFill",    hasGradFill ? 1 : 0);
            _mat.SetTexture("_GradFillTex",    hasGradFill ? p.GradFillTex : (Texture)Texture2D.whiteTexture);
            _mat.SetVector ("_GradFillParams", p.GradFillParams);
        }

        // レイヤーのマスク情報を DrawParams へ反映 (無効・テクスチャなしは「マスクなし」扱い)
        static void SetMaskFrom(ref DrawParams p, PSDLayer layer)
        {
            if (!layer.HasMask || layer.MaskTexture == null || layer.MaskIsDisabled) return;
            p.MaskTex     = layer.MaskTexture;
            p.MaskRect    = new Vector4(
                layer.MaskLeft, layer.MaskTop,
                layer.MaskRight - layer.MaskLeft, layer.MaskBottom - layer.MaskTop);
            p.MaskDefault = layer.MaskDefaultColor / 255f;
        }

        // UI 調整値を契約どおりの除数で正規化して反映 (REWRITE_SPEC.md §3)
        static void SetAdjustmentsFrom(ref DrawParams p, PSDLayer layer)
        {
            p.Brightness = layer.UIBrightness / 150f;
            p.Contrast   = layer.UIContrast   / 100f;
            p.Hue        = layer.UIHue        / 180f;
            p.Saturation = layer.UISaturation / 100f;
            p.Lightness  = layer.UILightness  / 100f;
            p.Colorize   = layer.UIColorize;

            p.Invert          = layer.UIInvert;
            p.Threshold       = layer.UIThresholdEnabled;
            p.ThresholdLevel  = layer.UIThresholdLevel / 255f;
            p.Posterize       = layer.UIPosterizeEnabled;
            p.PosterizeLevels = layer.UIPosterizeLevels;

            if (layer.UILevelsEnabled)
            {
                p.LevelsInBlack  = layer.UILevelsInputBlack  / 255f;
                p.LevelsInWhite  = layer.UILevelsInputWhite  / 255f;
                p.LevelsGamma    = Mathf.Max(0.01f, layer.UILevelsGamma);
                p.LevelsOutBlack = layer.UILevelsOutputBlack / 255f;
                p.LevelsOutWhite = layer.UILevelsOutputWhite / 255f;

                // R/G/B チャンネル別レコード (PSD 由来。UI 編集対象外、有効トグルには連動)
                var a = layer.Adjustment;
                if (a != null && a.HasChannelLevels && a.LevelsChannelRanges != null)
                {
                    p.LevelsChInBlack = new Vector3(
                        a.LevelsChannelRanges[0].x, a.LevelsChannelRanges[1].x, a.LevelsChannelRanges[2].x) / 255f;
                    p.LevelsChInWhite = new Vector3(
                        a.LevelsChannelRanges[0].y, a.LevelsChannelRanges[1].y, a.LevelsChannelRanges[2].y) / 255f;
                    p.LevelsChOutBlack = new Vector3(
                        a.LevelsChannelRanges[0].z, a.LevelsChannelRanges[1].z, a.LevelsChannelRanges[2].z) / 255f;
                    p.LevelsChOutWhite = new Vector3(
                        a.LevelsChannelRanges[0].w, a.LevelsChannelRanges[1].w, a.LevelsChannelRanges[2].w) / 255f;
                    p.LevelsChGamma = new Vector3(
                        Mathf.Max(0.01f, a.LevelsChannelGamma[0]),
                        Mathf.Max(0.01f, a.LevelsChannelGamma[1]),
                        Mathf.Max(0.01f, a.LevelsChannelGamma[2]));
                }
            }
            else
            {
                p.LevelsInBlack  = 0f;
                p.LevelsInWhite  = 1f;
                p.LevelsGamma    = 1f;
                p.LevelsOutBlack = 0f;
                p.LevelsOutWhite = 1f;
            }

            if (layer.UICurveEnabled && layer._curveLut != null)
                p.CurveLutTex = layer._curveLut;

            if (layer.UIGradientMapEnabled && layer._gradientLut != null)
            {
                p.GradientMapTex       = layer._gradientLut;
                p.GradientMapOpacity   = layer.UIGradientMapOpacity;
                p.GradientMapNormalize = layer.UIGradientMapNormalize;
                p.GradientMapLumMin    = layer._gradientLumMin;
                p.GradientMapLumMax    = layer._gradientLumMax;
            }

            if (layer.UIColorBalanceEnabled)
            {
                p.ColorBalance   = true;
                p.CBShadows      = layer.UICBShadows    / 100f;
                p.CBMidtones     = layer.UICBMidtones   / 100f;
                p.CBHighlights   = layer.UICBHighlights / 100f;
                p.CBPreserveLum  = layer.UICBPreserveLuminosity;
            }
        }

        // グラデーション塗りつぶし (GdFl) のパラメータを DrawParams へ反映 (LUT 未ベイクは無効扱い)
        static void SetGradientFillFrom(ref DrawParams p, PSDLayer layer)
        {
            var a = layer.Adjustment;
            if (a == null || !a.HasGradientFill || layer._gradientFillLut == null) return;
            float rad = a.GradientFillAngle * Mathf.Deg2Rad;
            p.GradFillTex    = layer._gradientFillLut;
            p.GradFillParams = new Vector4(
                Mathf.Cos(rad), Mathf.Sin(rad),
                a.GradientFillRadial ? 1f : 0f,
                Mathf.Max(0.01f, a.GradientFillScale));
        }

        // PassThrough / Unknown はシェーダー上 Normal として扱う
        static int ToShaderBlendMode(BlendMode mode)
        {
            if (mode == BlendMode.PassThrough || mode == BlendMode.Unknown)
                return (int)BlendMode.Normal;
            return (int)mode;
        }

        // グループ平坦化結果などキャンバス全面のテクスチャを 1 枚のレイヤーとして合成する
        void BlitAsFullCanvasLayer(Texture tex, PSDLayer layer, int blendMode, RenderTexture clipMask,
                                   ref RenderTexture cur, ref RenderTexture next)
        {
            var p = NewParams();
            p.LayerTex  = tex;
            p.LayerRect = FullCanvasRect;
            p.Opacity   = layer.UIOpacity;
            p.BlendMode = blendMode;
            SetMaskFrom(ref p, layer);
            p.ClipMaskTex = clipMask;
            SetAdjustmentsFrom(ref p, layer);
            ApplyParams(p);
            Graphics.Blit(cur, next, _mat);
            Swap(ref cur, ref next);
        }

        Vector4 FullCanvasRect => new Vector4(0, 0, _canvasW, _canvasH);

        static Vector4 LayerRectOf(PSDLayer layer)
            => new Vector4(layer.Left, layer.Top, layer.Width, layer.Height);
    }
}
