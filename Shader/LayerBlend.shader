// ─────────────────────────────────────────────────────────────────────────────
// Dennoko PSD Editor — LayerBlend.shader
//
// 1 パスで「レイヤー画像 + レイヤーマスク + クリッピングマスク + 色調補正」を
// 背景 (_MainTex) に合成するシェーダー。
//
// 契約 (REWRITE_SPEC.md §2, §3):
//   - uniform の名前・型・意味は凍結 (C# コンポジターが依存)
//   - _BlendMode の分岐番号 = BlendMode enum の int 値 (0..27, 未知は Normal)
//   - 演算はすべてサンプリング値そのまま (sRGB 値)。gamma/linear 変換禁止
//   - 合成式はストレートアルファの Photoshop / W3C compositing 準拠:
//       αo = αs + αb·(1-αs)
//       Co = ( αs·(1-αb)·Cs + αs·αb·B(Cb,Cs) + (1-αs)·αb·Cb ) / αo   (αo=0 → 0)
// ─────────────────────────────────────────────────────────────────────────────

Shader "PSDSimpleEditor/LayerBlend"
{
    Properties
    {
        _MainTex      ("背景 (Blit ソース)",            2D)     = "black" {}
        _LayerTex     ("レイヤー画像",                  2D)     = "black" {}
        _MaskTex      ("レイヤーマスク (.r)",           2D)     = "white" {}
        _ClipMaskTex  ("クリッピングマスク (.a)",       2D)     = "white" {}
        _CanvasSize   ("キャンバスサイズ (W,H,0,0) px", Vector) = (1,1,0,0)
        _LayerRect    ("レイヤー矩形 (L,T,W,H) px",     Vector) = (0,0,1,1)
        _LayerTile    ("(X,Y) タイル反復",              Vector) = (1,1,0,0)
        _LayerWrap    ("ラップ (タイリング) 0/1",       Int)    = 0
        _MaskRect     ("マスク矩形 (L,T,W,H) px",       Vector) = (0,0,1,1)
        _MaskDefault  ("マスク矩形外の値 0..1",         Float)  = 1
        _Opacity      ("不透明度 0..1",                 Float)  = 1
        _BlendMode    ("ブレンドモード番号 (§2)",       Int)    = 0
        _IsAdjustment ("調整パス 0/1",                  Int)    = 0
        _HasMask      ("マスク有効 0/1",                Int)    = 0
        _HasClipMask  ("クリップマスク有効 0/1",        Int)    = 0
        _Brightness   ("明るさ -1..1 (実値/150)",       Float)  = 0
        _Contrast     ("コントラスト -1..1 (実値/100)", Float)  = 0
        _Hue          ("色相 -1..1 (実値/180)",         Float)  = 0
        _Saturation   ("彩度 -1..1 (実値/100)",         Float)  = 0
        _Lightness    ("明度 -1..1 (実値/100)",         Float)  = 0
        _Colorize     ("着色 0/1",                      Int)    = 0
        _HasInvert    ("階調反転有効 0/1",              Int)    = 0
        _HasThreshold ("しきい値有効 0/1",              Int)    = 0
        _ThresholdLevel ("しきい値 0..1 (実値/255)",    Float)  = 0.5
        _HasPosterize ("ポスタリゼーション有効 0/1",    Int)    = 0
        _PosterizeLevels ("階調数 (2..255)",            Float)  = 4
        _LevelsInBlack  ("レベル 入力シャドウ 0..1",    Float)  = 0
        _LevelsInWhite  ("レベル 入力ハイライト 0..1",  Float)  = 1
        _LevelsGamma    ("レベル 中間調ガンマ",         Float)  = 1
        _LevelsOutBlack ("レベル 出力シャドウ 0..1",    Float)  = 0
        _LevelsOutWhite ("レベル 出力ハイライト 0..1",  Float)  = 1
        _HasCurveLut  ("トーンカーブ LUT 有効 0/1",     Int)    = 0
        _CurveLutTex  ("トーンカーブ LUT (256x1)",      2D)     = "white" {}
        _HasGradientMap     ("グラデーションマップ有効 0/1", Int)   = 0
        _GradientMapTex     ("グラデーション LUT (256x1)",   2D)    = "white" {}
        _GradientMapOpacity ("グラデーションマップ適用率 0..1", Float) = 1
        _GradientMapNormalize ("グラデーションマップ輝度正規化 0/1", Int) = 0
        _GradientMapLumMin  ("グラデーションマップ正規化 輝度下限 0..1", Float) = 0
        _GradientMapLumMax  ("グラデーションマップ正規化 輝度上限 0..1", Float) = 1
        _HasColorBalance ("カラーバランス有効 0/1",      Int)    = 0
        _CBShadows    ("CB シャドウ (CR,MG,YB) -1..1",   Vector) = (0,0,0,0)
        _CBMidtones   ("CB 中間調 (CR,MG,YB) -1..1",     Vector) = (0,0,0,0)
        _CBHighlights ("CB ハイライト (CR,MG,YB) -1..1", Vector) = (0,0,0,0)
        _CBPreserveLum ("CB 輝度保持 0/1",               Int)    = 1
    }

    SubShader
    {
        // エディタ内 Blit 専用。深度・カリングは一切使わない
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma target   3.0
            #include "UnityCG.cginc"

            // ════════════════════════════════════════════════════════════════
            //  uniform (REWRITE_SPEC.md §3【凍結】)
            // ════════════════════════════════════════════════════════════════
            sampler2D _MainTex;       // 背景 (Graphics.Blit のソース)
            sampler2D _LayerTex;      // レイヤー画像
            sampler2D _MaskTex;       // レイヤーマスク (.r 参照)
            sampler2D _ClipMaskTex;   // クリッピングマスク (キャンバス全面 RT, .a 参照)
            float4    _CanvasSize;    // (キャンバス幅, 高さ, 0, 0) px
            float4    _LayerRect;     // (L, T, W, H) — PSD 座標 (左上原点) px
            float4    _LayerTile;     // (X, Y, 0, 0) タイル反復数 (_LayerWrap=1 のとき有効)
            int       _LayerWrap;     // 1 = レイヤー矩形内で frac タイリング
            float4    _MaskRect;      // (L, T, W, H) — 同上
            float     _MaskDefault;   // マスク矩形外の値 0..1
            float     _Opacity;       // 0..1
            int       _BlendMode;     // §2 の番号
            int       _IsAdjustment;  // 1 = 調整パス (背景へ色調補正)
            int       _HasMask;       // 0/1
            int       _HasClipMask;   // 0/1
            float     _Brightness;    // -1..1 (実値 / 150)
            float     _Contrast;      // -1..1 (実値 / 100)
            float     _Hue;           // -1..1 (実値 / 180)
            float     _Saturation;    // -1..1 (実値 / 100)
            float     _Lightness;     // -1..1 (実値 / 100)
            int       _Colorize;      // 1 = 絶対値の色相・彩度を強制 (白黒着色)
            int       _HasInvert;        // 0/1
            int       _HasThreshold;     // 0/1
            float     _ThresholdLevel;   // 0..1 (実値 / 255)
            int       _HasPosterize;     // 0/1
            float     _PosterizeLevels;  // 2..255
            float     _LevelsInBlack;    // 0..1
            float     _LevelsInWhite;    // 0..1
            float     _LevelsGamma;      // 実値 (既定 1 = 恒等)
            float     _LevelsOutBlack;   // 0..1
            float     _LevelsOutWhite;   // 0..1
            int       _HasCurveLut;      // 0/1
            sampler2D _CurveLutTex;      // 256x1 LUT (入力輝度 → 出力値、R=G=B)
            int       _HasGradientMap;     // 0/1
            sampler2D _GradientMapTex;     // 256x1 LUT (輝度 → 色)
            float     _GradientMapOpacity; // 0..1
            int       _GradientMapNormalize; // 0/1: 輝度を [Min,Max] → [0,1] にストレッチしてから LUT を引く
            float     _GradientMapLumMin;    // 正規化下限 (対象レイヤーの不透明画素の最小輝度)
            float     _GradientMapLumMax;    // 正規化上限 (同、最大輝度)
            int       _HasColorBalance;      // 0/1
            float3    _CBShadows;            // (CR,MG,YB) -1..1
            float3    _CBMidtones;
            float3    _CBHighlights;
            int       _CBPreserveLum;        // 0/1: 変換前後で輝度を保持

            // ゼロ除算ガード用の微小値
            #define EPS 1e-5

            // ════════════════════════════════════════════════════════════════
            //  頂点シェーダー (フルスクリーン Blit クアッドをそのまま通す)
            // ════════════════════════════════════════════════════════════════
            struct appdata { float4 vertex : POSITION;    float2 uv : TEXCOORD0; };
            struct v2f     { float4 pos    : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv  = v.uv;
                return o;
            }

            // ════════════════════════════════════════════════════════════════
            //  非分離型ブレンド用ヘルパー (W3C compositing-1 仕様の式)
            // ════════════════════════════════════════════════════════════════

            // 輝度 (W3C: 0.3R + 0.59G + 0.11B)
            float Lum(float3 c)
            {
                return dot(c, float3(0.3, 0.59, 0.11));
            }

            // 輝度を保ったまま成分を [0,1] へ収める
            float3 ClipColor(float3 c)
            {
                float l = Lum(c);
                float n = min(c.r, min(c.g, c.b));
                float x = max(c.r, max(c.g, c.b));
                if (n < 0.0) c = l + (c - l) * (l       / max(l - n, EPS));
                if (x > 1.0) c = l + (c - l) * ((1.0-l) / max(x - l, EPS));
                return c;
            }

            // 輝度を lum に置き換える
            float3 SetLum(float3 c, float lum)
            {
                return ClipColor(c + (lum - Lum(c)));
            }

            // 彩度 (最大成分 − 最小成分)
            float Sat(float3 c)
            {
                return max(c.r, max(c.g, c.b)) - min(c.r, min(c.g, c.b));
            }

            // 彩度を s に置き換える
            // (W3C SetSat: min→0, max→s, mid→比例。線形正規化と等価)
            float3 SetSat(float3 c, float s)
            {
                float mn = min(c.r, min(c.g, c.b));
                float mx = max(c.r, max(c.g, c.b));
                if (mx - mn < EPS) return float3(0, 0, 0); // 無彩色
                return (c - mn) / (mx - mn) * s;
            }

            // ════════════════════════════════════════════════════════════════
            //  分離型ブレンド関数 B(Cb, Cs)  — Cb=背景色, Cs=レイヤー色
            //  Photoshop 準拠の式。ゼロ除算は EPS でガード
            // ════════════════════════════════════════════════════════════════

            // Multiply: 乗算
            float3 B_Multiply(float3 cb, float3 cs) { return cb * cs; }

            // Screen: スクリーン
            float3 B_Screen(float3 cb, float3 cs) { return cb + cs - cb * cs; }

            // Overlay: オーバーレイ (背景が暗→乗算, 明→スクリーン)
            float3 B_Overlay(float3 cb, float3 cs)
            {
                return lerp(2.0 * cb * cs,
                            1.0 - 2.0 * (1.0 - cb) * (1.0 - cs),
                            step(0.5, cb));
            }

            // Darken: 比較(暗)
            float3 B_Darken(float3 cb, float3 cs) { return min(cb, cs); }

            // Lighten: 比較(明)
            float3 B_Lighten(float3 cb, float3 cs) { return max(cb, cs); }

            // ColorBurn: 焼き込みカラー (cs=0 のとき cb=1 なら 1, それ以外 0)
            float3 B_ColorBurn(float3 cb, float3 cs)
            {
                return 1.0 - min(1.0, (1.0 - cb) / max(cs, EPS));
            }

            // ColorDodge: 覆い焼きカラー (cs=1 のとき cb=0 なら 0, それ以外 1)
            float3 B_ColorDodge(float3 cb, float3 cs)
            {
                return min(1.0, cb / max(1.0 - cs, EPS));
            }

            // LinearBurn: 焼き込み(リニア)
            float3 B_LinearBurn(float3 cb, float3 cs) { return max(cb + cs - 1.0, 0.0); }

            // LinearDodge: 覆い焼き(リニア) = 加算
            float3 B_LinearDodge(float3 cb, float3 cs) { return min(cb + cs, 1.0); }

            // SoftLight: ソフトライト (W3C/PDF 式)
            float SoftLightD(float x)
            {
                return (x <= 0.25) ? ((16.0 * x - 12.0) * x + 4.0) * x : sqrt(x);
            }
            float SoftLight1(float cb, float cs)
            {
                if (cs <= 0.5)
                    return cb - (1.0 - 2.0 * cs) * cb * (1.0 - cb);
                else
                    return cb + (2.0 * cs - 1.0) * (SoftLightD(cb) - cb);
            }
            float3 B_SoftLight(float3 cb, float3 cs)
            {
                return float3(SoftLight1(cb.r, cs.r),
                              SoftLight1(cb.g, cs.g),
                              SoftLight1(cb.b, cs.b));
            }

            // HardLight: ハードライト (Overlay の引数入れ替え)
            float3 B_HardLight(float3 cb, float3 cs) { return B_Overlay(cs, cb); }

            // VividLight: ビビッドライト (cs<0.5 → ColorBurn, cs>=0.5 → ColorDodge)
            float3 B_VividLight(float3 cb, float3 cs)
            {
                float3 burn  = 1.0 - min(1.0, (1.0 - cb) / max(2.0 * cs, EPS));
                float3 dodge = min(1.0, cb / max(2.0 * (1.0 - cs), EPS));
                return lerp(burn, dodge, step(0.5, cs));
            }

            // LinearLight: リニアライト
            float3 B_LinearLight(float3 cb, float3 cs)
            {
                return saturate(cb + 2.0 * cs - 1.0);
            }

            // PinLight: ピンライト
            float3 B_PinLight(float3 cb, float3 cs)
            {
                return lerp(min(cb, 2.0 * cs),
                            max(cb, 2.0 * cs - 1.0),
                            step(0.5, cs));
            }

            // HardMix: ハードミックス (cb+cs >= 1 なら 1, それ未満は 0)
            float3 B_HardMix(float3 cb, float3 cs)
            {
                return step(1.0, cb + cs);
            }

            // Difference: 差の絶対値
            float3 B_Difference(float3 cb, float3 cs) { return abs(cb - cs); }

            // Exclusion: 除外
            float3 B_Exclusion(float3 cb, float3 cs) { return cb + cs - 2.0 * cb * cs; }

            // Subtract: 減算
            float3 B_Subtract(float3 cb, float3 cs) { return max(cb - cs, 0.0); }

            // Divide: 除算
            float3 B_Divide(float3 cb, float3 cs)
            {
                return min(1.0, cb / max(cs, EPS));
            }

            // ════════════════════════════════════════════════════════════════
            //  非分離型・色比較ブレンド関数
            // ════════════════════════════════════════════════════════════════

            // DarkerColor: カラー比較(暗) — 輝度が低い方を色ごと採用
            float3 B_DarkerColor(float3 cb, float3 cs)
            {
                return (Lum(cs) < Lum(cb)) ? cs : cb;
            }

            // LighterColor: カラー比較(明) — 輝度が高い方を色ごと採用
            float3 B_LighterColor(float3 cb, float3 cs)
            {
                return (Lum(cs) > Lum(cb)) ? cs : cb;
            }

            // Hue: 色相 (レイヤーの色相 + 背景の彩度・輝度)
            float3 B_Hue(float3 cb, float3 cs)
            {
                return SetLum(SetSat(cs, Sat(cb)), Lum(cb));
            }

            // Saturation: 彩度 (レイヤーの彩度 + 背景の色相・輝度)
            float3 B_Saturation(float3 cb, float3 cs)
            {
                return SetLum(SetSat(cb, Sat(cs)), Lum(cb));
            }

            // Color: カラー (レイヤーの色相・彩度 + 背景の輝度)
            float3 B_Color(float3 cb, float3 cs)
            {
                return SetLum(cs, Lum(cb));
            }

            // Luminosity: 輝度 (レイヤーの輝度 + 背景の色相・彩度)
            float3 B_Luminosity(float3 cb, float3 cs)
            {
                return SetLum(cb, Lum(cs));
            }

            // ════════════════════════════════════════════════════════════════
            //  ブレンドディスパッチ
            //  if / else if の分岐番号 = BlendMode enum の int 値 (§2【凍結】)
            //  4 (Dissolve) と 27 (PassThrough) は呼び出し側で Normal 相当に処理
            // ════════════════════════════════════════════════════════════════
            float3 ApplyBlend(int mode, float3 cb, float3 cs)
            {
                if      (mode == 0)  return cs;                       // Normal
                else if (mode == 1)  return B_Multiply    (cb, cs);   // Multiply
                else if (mode == 2)  return B_Screen      (cb, cs);   // Screen
                else if (mode == 3)  return B_Overlay     (cb, cs);   // Overlay
                else if (mode == 4)  return cs;                       // Dissolve (α 側で処理)
                else if (mode == 5)  return B_Darken      (cb, cs);   // Darken
                else if (mode == 6)  return B_ColorBurn   (cb, cs);   // ColorBurn
                else if (mode == 7)  return B_LinearBurn  (cb, cs);   // LinearBurn
                else if (mode == 8)  return B_DarkerColor (cb, cs);   // DarkerColor
                else if (mode == 9)  return B_Lighten     (cb, cs);   // Lighten
                else if (mode == 10) return B_ColorDodge  (cb, cs);   // ColorDodge
                else if (mode == 11) return B_LinearDodge (cb, cs);   // LinearDodge (Add)
                else if (mode == 12) return B_LighterColor(cb, cs);   // LighterColor
                else if (mode == 13) return B_SoftLight   (cb, cs);   // SoftLight
                else if (mode == 14) return B_HardLight   (cb, cs);   // HardLight
                else if (mode == 15) return B_VividLight  (cb, cs);   // VividLight
                else if (mode == 16) return B_LinearLight (cb, cs);   // LinearLight
                else if (mode == 17) return B_PinLight    (cb, cs);   // PinLight
                else if (mode == 18) return B_HardMix     (cb, cs);   // HardMix
                else if (mode == 19) return B_Difference  (cb, cs);   // Difference
                else if (mode == 20) return B_Exclusion   (cb, cs);   // Exclusion
                else if (mode == 21) return B_Subtract    (cb, cs);   // Subtract
                else if (mode == 22) return B_Divide      (cb, cs);   // Divide
                else if (mode == 23) return B_Hue         (cb, cs);   // Hue
                else if (mode == 24) return B_Saturation  (cb, cs);   // Saturation
                else if (mode == 25) return B_Color       (cb, cs);   // Color
                else if (mode == 26) return B_Luminosity  (cb, cs);   // Luminosity
                else if (mode == 27) return cs;                       // PassThrough (Normal 扱い)
                else                 return cs;                       // 99 / 未知 → Normal
            }

            // ════════════════════════════════════════════════════════════════
            //  RGB ↔ HSL 変換 (色調補正の Hue/Saturation/Lightness 用)
            // ════════════════════════════════════════════════════════════════

            // RGB → HSL (h, s, l いずれも 0..1)
            float3 RGBtoHSL(float3 c)
            {
                float mx    = max(c.r, max(c.g, c.b));
                float mn    = min(c.r, min(c.g, c.b));
                float delta = mx - mn;
                float l     = (mx + mn) * 0.5;
                float h     = 0.0;
                float s     = 0.0;
                if (delta > EPS)
                {
                    s = delta / max(1.0 - abs(2.0 * l - 1.0), EPS);
                    if      (mx == c.r) h = (c.g - c.b) / delta;        // 赤が最大
                    else if (mx == c.g) h = (c.b - c.r) / delta + 2.0;  // 緑が最大
                    else                h = (c.r - c.g) / delta + 4.0;  // 青が最大
                    h = h / 6.0;
                    if (h < 0.0) h += 1.0;
                }
                return float3(h, s, l);
            }

            // HSL → RGB
            float3 HSLtoRGB(float3 hsl)
            {
                float h = hsl.x;
                float s = hsl.y;
                float l = hsl.z;
                float c = (1.0 - abs(2.0 * l - 1.0)) * s;
                float x = c * (1.0 - abs(fmod(h * 6.0, 2.0) - 1.0));
                float m = l - c * 0.5;
                float3 rgb;
                if      (h < 1.0 / 6.0) rgb = float3(c, x, 0);
                else if (h < 2.0 / 6.0) rgb = float3(x, c, 0);
                else if (h < 3.0 / 6.0) rgb = float3(0, c, x);
                else if (h < 4.0 / 6.0) rgb = float3(0, x, c);
                else if (h < 5.0 / 6.0) rgb = float3(x, 0, c);
                else                    rgb = float3(c, 0, x);
                return saturate(rgb + m);
            }

            // ════════════════════════════════════════════════════════════════
            //  ハッシュノイズ (スクリーン座標 → 0..1)。Dissolve とグラデーションマップの
            //  正規化ディザで共用するため ApplyAdjustments より前に定義する
            // ════════════════════════════════════════════════════════════════
            float Hash21(float2 p)
            {
                return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453);
            }

            // ════════════════════════════════════════════════════════════════
            //  カラーバランス: 画素輝度で シャドウ/中間調/ハイライト の重みを求め、
            //  各トーンの色シフト (CR,MG,YB = RGB 加算方向) を合成して適用する。
            //  _CBPreserveLum=1 のとき変換前後の輝度を加算で復元する (Photoshop の輝度保持相当)。
            // ════════════════════════════════════════════════════════════════
            float3 ColorBalance(float3 col)
            {
                float lBefore = Lum(col);
                float sW = saturate(1.0 - 2.0 * lBefore);   // 輝度0で最大、0.5 以上で0
                float hW = saturate(2.0 * lBefore - 1.0);   // 輝度1で最大、0.5 以下で0
                float mW = 1.0 - sW - hW;                    // 0.5 を頂点とするテント
                float3 shift = (_CBShadows * sW + _CBMidtones * mW + _CBHighlights * hW) * 0.5;
                float3 outc = saturate(col + shift);
                if (_CBPreserveLum == 1)
                    outc = saturate(outc + (lBefore - Lum(outc)));
                return outc;
            }

            // ════════════════════════════════════════════════════════════════
            //  色調補正 (_Brightness / _Contrast / _Hue / _Saturation / _Lightness)
            //  すべて -1..1 に正規化済み (§3)
            // ════════════════════════════════════════════════════════════════
            // ditherSeed: グラデーションマップの輝度正規化ディザ用シード (スクリーン座標)
            float3 ApplyAdjustments(float3 col, float2 ditherSeed)
            {
                // ── 階調反転 ──
                if (_HasInvert == 1)
                    col = saturate(1.0 - col);

                // ── レベル補正: 入力レンジ → ガンマ → 出力レンジ (既定値は恒等変換) ──
                {
                    float3 t = saturate((col - _LevelsInBlack) / max(_LevelsInWhite - _LevelsInBlack, EPS));
                    t = pow(t, 1.0 / max(_LevelsGamma, 0.01));
                    col = saturate(_LevelsOutBlack + t * (_LevelsOutWhite - _LevelsOutBlack));
                }

                // ── トーンカーブ: 256×1 LUT を各チャンネルへ同一適用 (複合/コンポジットカーブ) ──
                if (_HasCurveLut == 1)
                {
                    col.r = tex2D(_CurveLutTex, float2(saturate(col.r), 0.5)).r;
                    col.g = tex2D(_CurveLutTex, float2(saturate(col.g), 0.5)).g;
                    col.b = tex2D(_CurveLutTex, float2(saturate(col.b), 0.5)).b;
                }

                // ── ポスタリゼーション: 各チャンネルを指定階調数へ量子化 ──
                if (_HasPosterize == 1)
                {
                    float levels = max(_PosterizeLevels, 2.0);
                    col = round(col * (levels - 1.0)) / (levels - 1.0);
                }

                // ── しきい値: 輝度がしきい値以上なら白、未満なら黒 ──
                if (_HasThreshold == 1)
                {
                    float lum = Lum(col);
                    col = (lum >= _ThresholdLevel) ? float3(1, 1, 1) : float3(0, 0, 0);
                }

                // ── 明るさ: 加算系 ──
                col = saturate(col + _Brightness);

                // ── コントラスト: 中間値 0.5 基準のスケーリング ──
                col = saturate((col - 0.5) * (1.0 + _Contrast) + 0.5);

                // ── カラーバランス ──
                if (_HasColorBalance == 1)
                    col = ColorBalance(col);

                // ── 色相・彩度・明度: RGB → HSL → RGB (Photoshop hue2 相当の見た目を目標) ──
                if (abs(_Hue) > EPS || abs(_Saturation) > EPS || abs(_Lightness) > EPS || _Colorize == 1)
                {
                    float3 hsl = RGBtoHSL(col);

                    if (_Colorize == 1)
                    {
                        // 着色: 原色の彩度に依存せず絶対値を強制 → 白黒 (彩度0) にも色が乗る
                        hsl.x = frac(_Hue * 0.5 + 1.0);            // Hue スライダ → 絶対色相 [0,1]
                        hsl.y = saturate(_Saturation * 0.5 + 0.5); // Saturation スライダ中央=0.5
                    }
                    else
                    {
                        // 色相: _Hue=±1 が ±180° = hue 値 ±0.5 (1 周 = 1.0)
                        hsl.x = frac(hsl.x + _Hue * 0.5 + 1.0);

                        // 彩度: +側は 1/(1-x) で増幅 (+1 → 完全飽和), -側は線形減衰 (-1 → 無彩色)
                        if (_Saturation >= 0.0)
                            hsl.y = saturate(hsl.y / max(1.0 - _Saturation, EPS));
                        else
                            hsl.y = hsl.y * (1.0 + _Saturation);
                    }

                    // 明度: +側は白へ, -側は黒へ lerp (Photoshop の Lightness 挙動)
                    if (_Lightness >= 0.0)
                        hsl.z = hsl.z + (1.0 - hsl.z) * _Lightness;
                    else
                        hsl.z = hsl.z * (1.0 + _Lightness);

                    col = HSLtoRGB(hsl);
                }

                // ── グラデーションマップ: 輝度を LUT 色に置き換え、適用率で lerp ──
                if (_HasGradientMap == 1)
                {
                    float lum = Lum(col);
                    if (_GradientMapNormalize == 1)
                    {
                        lum = saturate((lum - _GradientMapLumMin) / max(_GradientMapLumMax - _GradientMapLumMin, EPS));
                        // ── ディザ: 正規化で拡大された量子化ステップをノイズでほぐし階調とびを目立たなくする (±0.5 LUT texel) ──
                        lum = saturate(lum + (Hash21(ditherSeed) - 0.5) / 256.0);
                    }
                    float3 g  = tex2D(_GradientMapTex, float2(saturate(lum), 0.5)).rgb;
                    col = lerp(col, g, saturate(_GradientMapOpacity));
                }
                return col;
            }

            // ════════════════════════════════════════════════════════════════
            //  フラグメントシェーダー
            // ════════════════════════════════════════════════════════════════
            float4 frag(v2f i) : SV_Target
            {
                // 背景 (ストレートアルファ)
                float4 bg = tex2D(_MainTex, i.uv);

                // ── キャンバス UV → PSD ピクセル座標 (左上原点)【凍結】──
                float psdX = i.uv.x * _CanvasSize.x;
                float psdY = (1.0 - i.uv.y) * _CanvasSize.y;

                // ── レイヤーマスク値 (矩形外は _MaskDefault) ──
                float maskVal = 1.0;
                if (_HasMask == 1)
                {
                    float mu = (psdX - _MaskRect.x) / max(_MaskRect.z, EPS);
                    float mv = 1.0 - (psdY - _MaskRect.y) / max(_MaskRect.w, EPS);
                    if (mu < 0.0 || mu > 1.0 || mv < 0.0 || mv > 1.0)
                        maskVal = _MaskDefault;
                    else
                        maskVal = tex2D(_MaskTex, float2(mu, mv)).r;
                }

                // ── クリッピングマスク値 (キャンバス全面 RT の α, キャンバス UV) ──
                float clipVal = 1.0;
                if (_HasClipMask == 1)
                    clipVal = tex2D(_ClipMaskTex, i.uv).a;

                // ════════════════════════════════════════════════════════════
                //  調整パス: _LayerTex を使わず背景へ色調補正を適用し、
                //  適用率 = _Opacity × マスク × クリップマスク で元の色と lerp
                // ════════════════════════════════════════════════════════════
                if (_IsAdjustment == 1)
                {
                    float  amount   = _Opacity * maskVal * clipVal;
                    float3 adjusted = ApplyAdjustments(bg.rgb, i.pos.xy);
                    return float4(lerp(bg.rgb, adjusted, amount), bg.a);
                }

                // ════════════════════════════════════════════════════════════
                //  通常パス: レイヤーをサンプルして背景へブレンド
                // ════════════════════════════════════════════════════════════

                // ── レイヤー UV (範囲外は α=0)【凍結】──
                float lu = (psdX - _LayerRect.x) / max(_LayerRect.z, EPS);
                float lv = 1.0 - (psdY - _LayerRect.y) / max(_LayerRect.w, EPS);

                float4 layer = float4(0, 0, 0, 0);
                if (_LayerWrap == 1)
                {
                    // タイリング: レイヤー矩形を基準に frac でラップ (範囲外クランプ無し)。
                    // レイヤー矩形外はクリップ形状 (clipVal) で α=0 になるため漏れない。
                    layer = tex2D(_LayerTex, frac(float2(lu, lv) * _LayerTile.xy));
                }
                else if (lu >= 0.0 && lu <= 1.0 && lv >= 0.0 && lv <= 1.0)
                {
                    layer = tex2D(_LayerTex, float2(lu, lv));
                }

                // 通常パスでも色調補正はレイヤー色に適用 (グループの 1 枚畳み込み用)
                float3 cs = ApplyAdjustments(layer.rgb, i.pos.xy);

                // ── ソースα: レイヤーα × _Opacity × マスク × クリップマスク【凍結】──
                float alphaS = layer.a * _Opacity * maskVal * clipVal;

                // ── Dissolve (4): スクリーン座標ハッシュノイズで α を 0/1 化する近似 ──
                if (_BlendMode == 4)
                    alphaS = (Hash21(floor(i.pos.xy)) < alphaS) ? 1.0 : 0.0;

                float alphaB = bg.a;
                float3 cb    = bg.rgb;

                // ── ブレンド関数 B(Cb, Cs) ──
                float3 blended = ApplyBlend(_BlendMode, cb, cs);

                // ── 合成式 (ストレートアルファ, Photoshop / W3C 準拠)【凍結】──
                //   αo = αs + αb·(1-αs)
                //   Co = ( αs·(1-αb)·Cs + αs·αb·B + (1-αs)·αb·Cb ) / αo
                float alphaO = alphaS + alphaB * (1.0 - alphaS);
                float3 co;
                if (alphaO <= 0.0)
                {
                    co     = float3(0, 0, 0);
                    alphaO = 0.0;
                }
                else
                {
                    co = ( alphaS * (1.0 - alphaB) * cs
                         + alphaS * alphaB         * blended
                         + (1.0 - alphaS) * alphaB * cb ) / alphaO;
                }

                return float4(co, alphaO);
            }
            ENDCG
        }
    }
    // フォールバックなし (エディタ専用)
    Fallback Off
}
