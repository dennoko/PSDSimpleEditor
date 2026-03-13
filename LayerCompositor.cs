using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace PSDSimpleEditor
{
    /// <summary>
    /// カスタムシェーダーを使い、全レイヤーを GPU 上で順次合成して
    /// 最終的な合成結果を Texture2D として返す。
    /// </summary>
    public class LayerCompositor
    {
        const string ShaderPath = "Assets/Editor/PSDSimpleEditor/LayerBlend.shader";

        readonly Material       _mat;
        readonly RenderTexture  _rtA;
        readonly RenderTexture  _rtB;
        readonly int            _canvasW;
        readonly int            _canvasH;

        public bool IsValid => _mat != null;

        public LayerCompositor(int canvasW, int canvasH)
        {
            _canvasW = canvasW;
            _canvasH = canvasH;

            var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            if (shader == null)
            {
                Debug.LogError($"[LayerCompositor] シェーダーが見つかりません: {ShaderPath}");
                return;
            }

            _mat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            _rtA = CreateRT(canvasW, canvasH);
            _rtB = CreateRT(canvasW, canvasH);
        }

        static RenderTexture CreateRT(int w, int h)
        {
            var rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            rt.Create();
            return rt;
        }

        /// <summary>
        /// layers を下から上へ合成し、最終結果を Texture2D で返す。
        /// layers は「index 0 = 最下層」の順であることを前提とする。
        /// </summary>
        public Texture2D Composite(List<PSDLayer> layers)
        {
            if (!IsValid) return null;

            // ── 作業 RT をクリア ──
            ClearRT(_rtA);

            _mat.SetVector("_CanvasSize", new Vector4(_canvasW, _canvasH, 0, 0));

            RenderTexture src = _rtA;
            RenderTexture dst = _rtB;

            foreach (var layer in layers)
            {
                if (!layer.UIVisible) continue;

                if (layer.IsAdjustmentLayer)
                {
                    // 調整レイヤー: ピクセル合成なし、色調補正のみ適用
                    bool hasAny = layer.Adjustment.HasBrightnessContrast
                               || layer.Adjustment.HasHueSaturation;
                    if (!hasAny) continue;

                    SetAdjustmentUniforms(layer);
                    _mat.SetInt("_IsAdjustment", 1);
                    Graphics.Blit(src, dst, _mat);
                }
                else
                {
                    if (layer.Texture == null) continue;

                    SetAdjustmentUniforms(layer);
                    _mat.SetInt    ("_IsAdjustment", 0);
                    _mat.SetTexture("_LayerTex", layer.Texture);
                    _mat.SetVector ("_LayerRect",
                        new Vector4(layer.Left, layer.Top, layer.Width, layer.Height));
                    _mat.SetFloat  ("_Opacity",   layer.UIOpacity);
                    _mat.SetInt    ("_BlendMode", (int)layer.BlendMode);
                    Graphics.Blit(src, dst, _mat);
                }

                // ピンポンバッファの入れ替え
                var tmp = src; src = dst; dst = tmp;
            }

            // ── src (最終結果) を Texture2D に転写 ──
            var result     = new Texture2D(_canvasW, _canvasH, TextureFormat.RGBA32, false);
            var prevActive = RenderTexture.active;
            RenderTexture.active = src;
            result.ReadPixels(new Rect(0, 0, _canvasW, _canvasH), 0, 0);
            result.Apply();
            RenderTexture.active = prevActive;

            return result;
        }

        void SetAdjustmentUniforms(PSDLayer layer)
        {
            // 各パラメータをシェーダーが期待する -1..1 の範囲に正規化
            _mat.SetFloat("_Brightness", layer.UIBrightness / 150f);   // -150..150 → -1..1
            _mat.SetFloat("_Contrast",   layer.UIContrast   / 100f);   // -100..100 → -1..1
            _mat.SetFloat("_Hue",        layer.UIHue        / 180f);   // -180..180 → -1..1
            _mat.SetFloat("_Saturation", layer.UISaturation / 100f);   // -100..100 → -1..1
            _mat.SetFloat("_Lightness",  layer.UILightness  / 100f);   // -100..100 → -1..1
        }

        static void ClearRT(RenderTexture rt)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            GL.Clear(true, true, Color.clear);
            RenderTexture.active = prev;
        }

        public void Dispose()
        {
            if (_mat  != null) { Object.DestroyImmediate(_mat);           }
            if (_rtA  != null) { _rtA.Release(); Object.DestroyImmediate(_rtA); }
            if (_rtB  != null) { _rtB.Release(); Object.DestroyImmediate(_rtB); }
        }
    }
}
