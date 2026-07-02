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
                l.UIColorize   = l.Adjustment.HasHueSaturation && l.Adjustment.HueColorize;
                l.UIInvert            = l.Adjustment.HasInvert;
                l.UIThresholdEnabled  = l.Adjustment.HasThreshold;
                l.UIThresholdLevel    = l.Adjustment.HasThreshold ? l.Adjustment.ThresholdLevel : 128f;
                l.UIPosterizeEnabled  = l.Adjustment.HasPosterize;
                l.UIPosterizeLevels   = l.Adjustment.HasPosterize ? l.Adjustment.PosterizeLevels : 4f;
                l.UILevelsEnabled     = l.Adjustment.HasLevels;
                l.UILevelsInputBlack  = l.Adjustment.HasLevels ? l.Adjustment.LevelsInputBlack  : 0f;
                l.UILevelsInputWhite  = l.Adjustment.HasLevels ? l.Adjustment.LevelsInputWhite  : 255f;
                l.UILevelsGamma       = l.Adjustment.HasLevels ? l.Adjustment.LevelsGamma       : 1f;
                l.UILevelsOutputBlack = l.Adjustment.HasLevels ? l.Adjustment.LevelsOutputBlack : 0f;
                l.UILevelsOutputWhite = l.Adjustment.HasLevels ? l.Adjustment.LevelsOutputWhite : 255f;
                l.UICurveEnabled = l.Adjustment.HasCurves;
                if (l.Adjustment.HasCurves && l.Adjustment.CurvePoints != null && l.Adjustment.CurvePoints.Count >= 2)
                    l.UICurve = BuildAnimationCurveFromPoints(l.Adjustment.CurvePoints);

                // R/G/B チャンネル別カーブ (合成へ反映のみ。LUT ベイク時に複合カーブと畳み込む)
                if (l.Adjustment.HasChannelCurves && l.Adjustment.CurveChannelPoints != null)
                {
                    l.UICurveChannels = new AnimationCurve[3];
                    for (int c = 0; c < 3; c++)
                    {
                        var pts = l.Adjustment.CurveChannelPoints[c];
                        if (pts != null && pts.Count >= 2)
                            l.UICurveChannels[c] = BuildAnimationCurveFromPoints(pts);
                    }
                }

                // グラデーションマップ (LUT は Editor 側でロード後に焼く。ここでは有効化とグラデーションのみ)
                if (l.Adjustment.HasGradientMap && l.Adjustment.GradientMapGradient != null)
                {
                    l.UIGradientMapEnabled = true;
                    l.UIGradient           = l.Adjustment.GradientMapGradient;
                }

                // カラーバランス
                l.UIColorBalanceEnabled = l.Adjustment.HasColorBalance;
                if (l.Adjustment.HasColorBalance)
                {
                    l.UICBShadows            = l.Adjustment.CBShadows;
                    l.UICBMidtones           = l.Adjustment.CBMidtones;
                    l.UICBHighlights         = l.Adjustment.CBHighlights;
                    l.UICBPreserveLuminosity = l.Adjustment.CBPreserveLuminosity;
                }

                InitUIState(l.Children);
            }
        }

        // ════════════════════════════════════════════════════════════════
        //  本ツール製クリップ調整レイヤーの畳み戻し
        //  (PSDExportRecordBuilder.AppendAdjustmentClipRecords の逆変換)
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// dPSE マーカー付きのクリップ調整レイヤーを直下のピクセルレイヤーの UI* へ吸収し、
        /// ツリーから除去する (InitUIState 実行後に呼ぶこと)。
        /// Photoshop 側でマスク付与・非表示化・不透明度変更・ブレンド変更されたものは
        /// 情報を壊さないよう畳み戻さず通常の調整レイヤーとして残す。
        /// </summary>
        internal static void FoldBackToolAdjustmentClips(List<PSDLayer> layers)
        {
            if (layers == null) return;
            for (int i = 0; i < layers.Count; i++)
            {
                var baseLayer = layers[i];
                FoldBackToolAdjustmentClips(baseLayer.Children);

                // 畳み戻し先はピクセルレイヤーのみ (マーカー自身・グループ・調整レイヤーは対象外)
                if (baseLayer.Children != null || baseLayer.Texture == null ||
                    baseLayer.IsAdjustmentLayer || baseLayer.IsToolAdjustmentClip) continue;

                // 直上に連続するマーカー付きレイヤーを順に吸収する
                // (非対象レイヤーが現れた時点で停止 = 適用順を壊さない)
                while (i + 1 < layers.Count && CanFoldBack(layers[i + 1]))
                {
                    AbsorbAdjustmentClip(baseLayer, layers[i + 1]);
                    layers.RemoveAt(i + 1);
                }
            }
        }

        static bool CanFoldBack(PSDLayer m)
        {
            if (!m.IsToolAdjustmentClip || !m.IsClipping) return false;
            if (m.Children != null || !m.IsAdjustmentLayer) return false;   // ピクセルを持つ = 対象外
            if (m.HasMask && m.MaskTexture != null) return false;           // マスクが付与された
            if (!m.IsVisible) return false;                                 // 非表示化された
            if (m.BlendMode != BlendMode.Normal) return false;              // ブレンドが変更された
            // グラデーションマップは不透明度に適用率を載せて書き出すため不透明度変更を許容する
            bool isGradientMap = m.Adjustment != null && m.Adjustment.HasGradientMap;
            if (!isGradientMap && m.Opacity != 255) return false;           // 不透明度で効果が弱められた
            return true;
        }

        static void AbsorbAdjustmentClip(PSDLayer b, PSDLayer m)
        {
            var a = m.Adjustment;
            if (a == null) return;

            if (a.HasInvert) b.UIInvert = true;

            if (a.HasThreshold)
            {
                b.UIThresholdEnabled = true;
                b.UIThresholdLevel   = m.UIThresholdLevel;
            }

            if (a.HasPosterize)
            {
                b.UIPosterizeEnabled = true;
                b.UIPosterizeLevels  = m.UIPosterizeLevels;
            }

            if (a.HasBrightnessContrast)
            {
                b.UIBrightness = m.UIBrightness;
                b.UIContrast   = m.UIContrast;
            }

            if (a.HasHueSaturation)
            {
                b.UIHue        = m.UIHue;
                b.UISaturation = m.UISaturation;
                b.UILightness  = m.UILightness;
                b.UIColorize   = m.UIColorize;
            }

            if (a.HasLevels)
            {
                b.UILevelsEnabled     = true;
                b.UILevelsInputBlack  = m.UILevelsInputBlack;
                b.UILevelsInputWhite  = m.UILevelsInputWhite;
                b.UILevelsGamma       = m.UILevelsGamma;
                b.UILevelsOutputBlack = m.UILevelsOutputBlack;
                b.UILevelsOutputWhite = m.UILevelsOutputWhite;
                // R/G/B チャンネル別レコードは合成・書き出しとも Adjustment 側を参照する
                b.Adjustment.HasChannelLevels    = a.HasChannelLevels;
                b.Adjustment.LevelsChannelRanges = a.LevelsChannelRanges;
                b.Adjustment.LevelsChannelGamma  = a.LevelsChannelGamma;
            }

            if (a.HasCurves && m.UICurve != null)
            {
                b.UICurveEnabled  = true;
                b.UICurve         = m.UICurve;
                b.UICurveChannels = m.UICurveChannels;
                // チャンネル別カーブの書き戻し (EncodeCurv) は Adjustment 側を参照する
                b.Adjustment.HasChannelCurves   = a.HasChannelCurves;
                b.Adjustment.CurveChannelPoints = a.CurveChannelPoints;
            }

            if (a.HasColorBalance)
            {
                b.UIColorBalanceEnabled  = true;
                b.UICBShadows            = m.UICBShadows;
                b.UICBMidtones           = m.UICBMidtones;
                b.UICBHighlights         = m.UICBHighlights;
                b.UICBPreserveLuminosity = m.UICBPreserveLuminosity;
            }

            if (a.HasGradientMap && m.UIGradient != null)
            {
                b.UIGradientMapEnabled = true;
                b.UIGradient           = m.UIGradient;
                b.UIGradientMapOpacity = m.UIOpacity; // 書き出し時に適用率をレイヤー不透明度へ載せている
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
