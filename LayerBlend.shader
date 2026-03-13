// PSD Simple Editor - Layer Blend Shader
// ブレンドモード (通常/乗算/スクリーン/オーバーレイ) と
// 色調補正 (明るさ/コントラスト/色相/彩度/明度) を GPU 上で処理する。

Shader "PSDSimpleEditor/LayerBlend"
{
    Properties
    {
        _MainTex      ("Background",                  2D) = "white"  {}
        _LayerTex     ("Layer Texture",               2D) = "clear"  {}
        _CanvasSize   ("Canvas Size (W, H, -, -)",    Vector) = (1, 1, 0, 0)
        _LayerRect    ("Layer Rect (L, T, W, H)",     Vector) = (0, 0, 1, 1)
        _Opacity      ("Opacity",                     Float)  = 1.0
        _BlendMode    ("Blend Mode (0=Norm 1=Mul 2=Scr 3=Ovr)", Int) = 0
        _IsAdjustment ("Is Adjustment Pass",          Int)    = 0

        // 色調補正パラメータ (正規化済み: Brightness -1..1 / Contrast -1..1 /
        //                    Hue -1..1 / Saturation -1..1 / Lightness -1..1)
        _Brightness   ("Brightness",  Float) = 0
        _Contrast     ("Contrast",    Float) = 0
        _Hue          ("Hue",         Float) = 0
        _Saturation   ("Saturation",  Float) = 0
        _Lightness    ("Lightness",   Float) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Cull Off  ZWrite Off  ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            // ── Uniforms ──────────────────────────────────────────────
            sampler2D _MainTex;
            sampler2D _LayerTex;
            float4    _CanvasSize;
            float4    _LayerRect;
            float     _Opacity;
            int       _BlendMode;
            int       _IsAdjustment;
            float     _Brightness;
            float     _Contrast;
            float     _Hue;
            float     _Saturation;
            float     _Lightness;

            // ── Vertex ────────────────────────────────────────────────
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f     { float4 pos    : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv  = v.uv;
                return o;
            }

            // ── ブレンドモード関数 ────────────────────────────────────
            float3 BlendNormal  (float3 bg, float3 fg) { return fg; }
            float3 BlendMultiply(float3 bg, float3 fg) { return bg * fg; }
            float3 BlendScreen  (float3 bg, float3 fg) { return 1.0 - (1.0 - bg) * (1.0 - fg); }
            float3 BlendOverlay (float3 bg, float3 fg)
            {
                return lerp(
                    2.0 * bg * fg,
                    1.0 - 2.0 * (1.0 - bg) * (1.0 - fg),
                    step(0.5, bg));
            }

            // ── RGB ↔ HSL 変換 ────────────────────────────────────────
            float3 RGBtoHSL(float3 c)
            {
                float mx    = max(c.r, max(c.g, c.b));
                float mn    = min(c.r, min(c.g, c.b));
                float delta = mx - mn;
                float l     = (mx + mn) * 0.5;
                float s     = (delta < 1e-4) ? 0.0
                              : delta / (1.0 - abs(2.0 * l - 1.0));
                float h     = 0.0;
                if (delta > 1e-4)
                {
                    if      (mx == c.r) h = fmod((c.g - c.b) / delta, 6.0);
                    else if (mx == c.g) h = (c.b - c.r) / delta + 2.0;
                    else                h = (c.r - c.g) / delta + 4.0;
                    h /= 6.0;
                    if (h < 0.0) h += 1.0;
                }
                return float3(h, s, l);
            }

            float3 HSLtoRGB(float3 hsl)
            {
                float h = hsl.x;
                float s = hsl.y;
                float l = hsl.z;
                float c = (1.0 - abs(2.0 * l - 1.0)) * s;
                float x = c * (1.0 - abs(fmod(h * 6.0, 2.0) - 1.0));
                float m = l - c * 0.5;
                float3 rgb;
                if      (h < 1.0/6.0) rgb = float3(c, x, 0);
                else if (h < 2.0/6.0) rgb = float3(x, c, 0);
                else if (h < 3.0/6.0) rgb = float3(0, c, x);
                else if (h < 4.0/6.0) rgb = float3(0, x, c);
                else if (h < 5.0/6.0) rgb = float3(x, 0, c);
                else                  rgb = float3(c, 0, x);
                return saturate(rgb + m);
            }

            // ── 色調補正の適用 ────────────────────────────────────────
            float3 ApplyAdjustments(float3 col)
            {
                // 明るさ: _Brightness は -1..1 に正規化済み
                col = saturate(col + _Brightness);

                // コントラスト: _Contrast は -1..1 に正規化済み
                // factor = 1 + _Contrast → 0(フラット) .. 2(強調)
                float factor = 1.0 + _Contrast;
                col = saturate((col - 0.5) * factor + 0.5);

                // 色相・彩度・明度
                if (abs(_Hue) > 1e-4 || abs(_Saturation) > 1e-4 || abs(_Lightness) > 1e-4)
                {
                    float3 hsl = RGBtoHSL(col);
                    hsl.x = frac(hsl.x + _Hue);               // 色相を円環回転
                    hsl.y = saturate(hsl.y + _Saturation);     // 彩度
                    hsl.z = saturate(hsl.z + _Lightness * 0.5); // 明度 (±0.5 程度に抑制)
                    col   = HSLtoRGB(hsl);
                }

                return col;
            }

            // ── Fragment ──────────────────────────────────────────────
            fixed4 frag(v2f i) : SV_Target
            {
                float4 bg = tex2D(_MainTex, i.uv);

                // 調整レイヤーパス: 合成せずバックグラウンドに補正だけ適用
                if (_IsAdjustment == 1)
                {
                    bg.rgb = ApplyAdjustments(bg.rgb);
                    return bg;
                }

                // ── キャンバス UV → PSD ピクセル座標変換 ──
                // Unity UV: (u, v) = (右, 上), PSD 座標: (x, y) = (右, 下)
                float psdX = i.uv.x * _CanvasSize.x;
                float psdY = (1.0 - i.uv.y) * _CanvasSize.y;

                // レイヤーローカル UV
                float layerU = (psdX - _LayerRect.x) / max(_LayerRect.z, 1.0);
                float layerV = 1.0 - (psdY - _LayerRect.y) / max(_LayerRect.w, 1.0);

                // レイヤー矩形の外側は描画しない
                float inBounds = step(0.0, layerU) * step(layerU, 1.0)
                               * step(0.0, layerV) * step(layerV, 1.0);

                // UV をクランプしてからサンプリング (範囲外サンプリングを防ぐ)
                float4 layerCol = tex2D(_LayerTex, saturate(float2(layerU, layerV)));

                // 色調補正
                layerCol.rgb = ApplyAdjustments(layerCol.rgb);

                // ── ブレンドモード ──
                float3 blended;
                if      (_BlendMode == 1) blended = BlendMultiply(bg.rgb, layerCol.rgb);
                else if (_BlendMode == 2) blended = BlendScreen  (bg.rgb, layerCol.rgb);
                else if (_BlendMode == 3) blended = BlendOverlay (bg.rgb, layerCol.rgb);
                else                      blended = BlendNormal  (bg.rgb, layerCol.rgb);

                // Porter-Duff "over" 合成
                float alpha       = layerCol.a * _Opacity * inBounds;
                float3 outRGB     = blended * alpha + bg.rgb * (1.0 - alpha);
                float  outAlpha   = alpha + bg.a * (1.0 - alpha);

                return float4(outRGB, outAlpha);
            }

            ENDCG
        }
    }
}
