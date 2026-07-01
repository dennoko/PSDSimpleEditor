using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace PSDSimpleEditor
{
    // ════════════════════════════════════════════════════════════════
    //  テクスチャ構築 / グループツリー構築 / UI 初期値
    // ════════════════════════════════════════════════════════════════
    internal static class PSDLayerAssembler
    {
        internal static void BuildLayerTextures(List<PSDLayer> flat)
        {
            foreach (var layer in flat)
            {
                if (layer._rawPixels != null)
                {
                    layer.Texture    = CreateTexture(layer._rawPixels, layer.Width, layer.Height,
                                                     TextureFormat.RGBA32, layer.Name);
                    layer._rawPixels = null; // メモリ解放
                }
                if (layer._rawMaskPixels != null)
                {
                    int mw = layer.MaskRight - layer.MaskLeft;
                    int mh = layer.MaskBottom - layer.MaskTop;
                    layer.MaskTexture    = CreateTexture(layer._rawMaskPixels, mw, mh,
                                                         TextureFormat.R8, layer.Name + " (mask)");
                    layer._rawMaskPixels = null;
                }
            }
        }

        /// <summary>
        /// トップダウンの生バッファから Texture2D を作る。
        /// 上下反転 (Unity 標準向き、UV(0,0)=左下) する。
        /// linear パラメータで sRGB/Linear 挙動を設定（デフォルトは true で sRGB 変換バイパス）。
        /// </summary>
        internal static Texture2D CreateTexture(byte[] topDownPixels, int w, int h, TextureFormat format, string name, bool linear = true)
        {
            int bpp      = format == TextureFormat.RGBA32 ? 4 : 1;
            int rowBytes = w * bpp;
            var flipped  = new byte[rowBytes * h];
            for (int y = 0; y < h; y++)
                Buffer.BlockCopy(topDownPixels, y * rowBytes, flipped, (h - 1 - y) * rowBytes, rowBytes);

            var tex = new Texture2D(w, h, format, false, linear)
            {
                name      = name,
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode  = TextureWrapMode.Clamp,
            };
            tex.LoadRawTextureData(flipped);
            tex.Apply(false);
            return tex;
        }

        /// <summary>
        /// ファイル格納順 (最下層→最上層) のフラットリストからツリーを構築する。
        /// lsct type3 (終端マーカー) でグループスコープを push、type1/2 (フォルダ) で pop して確定。
        /// マーカーはツリーに含めない。結果は index 0 = 最下層。
        /// </summary>
        internal static List<PSDLayer> BuildLayerTree(List<PSDLayer> flat, StringBuilder vlog)
        {
            var root  = new List<PSDLayer>();
            var stack = new Stack<List<PSDLayer>>();
            stack.Push(root);

            foreach (var layer in flat)
            {
                switch (layer.SectionType)
                {
                    case LayerSectionType.GroupEnd:
                        // 終端マーカー = 新しいグループのスコープ開始 (マーカー自体は捨てる)
                        stack.Push(new List<PSDLayer>());
                        break;

                    case LayerSectionType.GroupBegin:
                        // フォルダレイヤー = スコープ確定
                        if (stack.Count > 1)
                        {
                            layer.Children = stack.Pop(); // 追加順のまま index 0 = 最下層
                        }
                        else
                        {
                            Debug.LogWarning($"[PSDParser] グループ構造が不整合です (フォルダ '{layer.Name}' に対応する終端マーカーがありません)");
                            layer.Children = new List<PSDLayer>();
                        }
                        stack.Peek().Add(layer);
                        break;

                    default:
                        stack.Peek().Add(layer);
                        break;
                }
            }

            // 終端マーカー過多 (不整合): 取り残されたスコープを親へ吐き出す
            while (stack.Count > 1)
            {
                var orphan = stack.Pop();
                stack.Peek().AddRange(orphan);
                Debug.LogWarning("[PSDParser] グループ構造が不整合です (余分な終端マーカーを無視しました)");
            }

            vlog?.AppendLine($"  ツリー構築完了: ルート直下 {root.Count} 項目");
            return root;
        }

        internal static void InitUIState(List<PSDLayer> layers)
        {
            if (layers == null) return;
            foreach (var l in layers)
            {
                l.UIVisible    = l.IsVisible;
                l.UIOpacity    = l.Opacity / 255f;
                l.UIBrightness = l.Adjustment.HasBrightnessContrast ? l.Adjustment.Brightness : 0f;
                l.UIContrast   = l.Adjustment.HasBrightnessContrast ? l.Adjustment.Contrast   : 0f;
                l.UIHue        = l.Adjustment.HasHueSaturation ? l.Adjustment.Hue        : 0f;
                l.UISaturation = l.Adjustment.HasHueSaturation ? l.Adjustment.Saturation : 0f;
                l.UILightness  = l.Adjustment.HasHueSaturation ? l.Adjustment.Lightness  : 0f;
                l.UIInvert            = l.Adjustment.HasInvert;
                l.UIThresholdEnabled  = l.Adjustment.HasThreshold;
                l.UIThresholdLevel    = l.Adjustment.HasThreshold ? l.Adjustment.ThresholdLevel : 128f;
                l.UIPosterizeEnabled  = l.Adjustment.HasPosterize;
                l.UIPosterizeLevels   = l.Adjustment.HasPosterize ? l.Adjustment.PosterizeLevels : 4f;
                l.UILevelsInputBlack  = l.Adjustment.HasLevels ? l.Adjustment.LevelsInputBlack  : 0f;
                l.UILevelsInputWhite  = l.Adjustment.HasLevels ? l.Adjustment.LevelsInputWhite  : 255f;
                l.UILevelsGamma       = l.Adjustment.HasLevels ? l.Adjustment.LevelsGamma       : 1f;
                l.UILevelsOutputBlack = l.Adjustment.HasLevels ? l.Adjustment.LevelsOutputBlack : 0f;
                l.UILevelsOutputWhite = l.Adjustment.HasLevels ? l.Adjustment.LevelsOutputWhite : 255f;
                l.UICurveEnabled = l.Adjustment.HasCurves;
                if (l.Adjustment.HasCurves && l.Adjustment.CurvePoints != null && l.Adjustment.CurvePoints.Count >= 2)
                    l.UICurve = BuildAnimationCurveFromPoints(l.Adjustment.CurvePoints);
                InitUIState(l.Children);
            }
        }

        /// <summary>curv の制御点 (0..255 空間) から滑らかな 0..1 空間の AnimationCurve を組み立てる。</summary>
        static AnimationCurve BuildAnimationCurveFromPoints(List<Vector2> points)
        {
            var keys = new Keyframe[points.Count];
            for (int i = 0; i < points.Count; i++)
                keys[i] = new Keyframe(points[i].x / 255f, points[i].y / 255f);
            var curve = new AnimationCurve(keys);
            for (int i = 0; i < keys.Length; i++)
                curve.SmoothTangents(i, 0f);
            return curve;
        }
    }
}
