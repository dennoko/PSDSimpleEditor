using System.Collections.Generic;
using UnityEngine;

namespace PSDSimpleEditor
{
    // ════════════════════════════════════════════════════════════════
    //  ツリー → フラットレコード (BuildLayerTree の逆)
    // ════════════════════════════════════════════════════════════════
    internal static class PSDExportRecordBuilder
    {
        internal static void BuildRecords(List<PSDLayer> layers, List<ExportRecord> outRecs, LayerCompositor comp)
        {
            if (layers == null) return;
            // index 0 = 最下層 = ファイル格納順の先頭
            foreach (var layer in layers)
            {
                if (layer.Children != null)
                {
                    // グループ: 終端マーカー → 子 → フォルダ
                    outRecs.Add(BuildGroupEndMarker());
                    BuildRecords(layer.Children, outRecs, comp);
                    outRecs.Add(BuildFolderRecord(layer));
                }
                else
                {
                    outRecs.Add(BuildPixelRecord(layer, comp));

                    // 画像クリップ合成は独立したクリッピングレイヤーとして真上に挿入する。
                    // (ファイル格納順 = 下→上なので、直後 append = ベースの真上 = クリップ先が直下)
                    // 既知の制限: ベースが既に他のクリッピングメンバーを真上に持つ場合、その間に入る。
                    if (layer.UIImageClipEnabled && layer.UIImageClipTex != null)
                        outRecs.Add(BuildImageClipRecord(layer, comp));
                }
            }
        }

        static ExportRecord BuildGroupEndMarker()
        {
            return new ExportRecord
            {
                Name     = "</Layer group>",
                BlendKey = "norm",
                Opacity  = 255,
                Flags    = 0x02, // 非表示 (Photoshop 慣習)
                LsctType = 3,
                Channels = EmptyChannels(),
            };
        }

        static ExportRecord BuildFolderRecord(PSDLayer layer)
        {
            string gkey = PSDWriter.KeyOf(layer.GroupBlendMode);
            return new ExportRecord
            {
                Name          = layer.Name,
                BlendKey      = gkey,
                Opacity       = ToByteOpacity(layer.UIOpacity),
                Flags         = (byte)(layer.UIVisible ? 0x00 : 0x02),
                LsctType      = layer.IsExpanded ? 1 : 2,
                GroupBlendKey = gkey,
                Channels      = EmptyChannels(),
            };
        }

        /// <summary>ゼロ面積レイヤー (フォルダ/終端マーカー) 用の空チャンネル R,G,B,A。</summary>
        static List<ExportChannel> EmptyChannels()
        {
            var list = new List<ExportChannel>();
            foreach (short id in new short[] { 0, 1, 2, -1 })
                list.Add(new ExportChannel { Id = id, Data = new byte[] { 0, 0 } }); // compression=0 (raw), payload なし
            return list;
        }

        static ExportRecord BuildPixelRecord(PSDLayer layer, LayerCompositor comp)
        {
            var rec = new ExportRecord
            {
                Top      = layer.Top,
                Left     = layer.Left,
                Bottom   = layer.Bottom,
                Right    = layer.Right,
                Name     = layer.Name,
                BlendKey = PSDWriter.KeyOf(layer.BlendMode),
                Opacity  = ToByteOpacity(layer.UIOpacity),
                Clipping = (byte)(layer.IsClipping ? 1 : 0),
                Flags    = (byte)(layer.UIVisible ? 0x00 : 0x02),
            };

            int lw = layer.Width, lh = layer.Height;
            if (layer.Texture != null && lw > 0 && lh > 0)
            {
                // 補正/グラデーションがあれば焼き込み、無ければ素のテクスチャを読み戻す
                Color32[] px = NeedsBake(layer) && comp != null
                    ? comp.RenderLayerForExport(layer)
                    : null;
                if (px == null) px = PSDPixelEncoder.ReadTextureTopDown(layer.Texture);

                var rp = new byte[lw * lh];
                var gp = new byte[lw * lh];
                var bp = new byte[lw * lh];
                var ap = new byte[lw * lh];
                for (int i = 0; i < lw * lh; i++)
                {
                    rp[i] = px[i].r; gp[i] = px[i].g; bp[i] = px[i].b; ap[i] = px[i].a;
                }
                rec.Channels.Add(new ExportChannel { Id = 0,  Data = PSDPixelEncoder.CompressPlaneRLE(rp, lw, lh) });
                rec.Channels.Add(new ExportChannel { Id = 1,  Data = PSDPixelEncoder.CompressPlaneRLE(gp, lw, lh) });
                rec.Channels.Add(new ExportChannel { Id = 2,  Data = PSDPixelEncoder.CompressPlaneRLE(bp, lw, lh) });
                rec.Channels.Add(new ExportChannel { Id = -1, Data = PSDPixelEncoder.CompressPlaneRLE(ap, lw, lh) });
            }

            // レイヤーマスク (チャンネル -2 + マスクデータブロック)
            int mw = layer.MaskRight - layer.MaskLeft;
            int mh = layer.MaskBottom - layer.MaskTop;
            if (layer.HasMask && layer.MaskTexture != null && mw > 0 && mh > 0)
            {
                byte[] mask = PSDPixelEncoder.ReadMaskTopDown(layer.MaskTexture, mw, mh);
                rec.Channels.Add(new ExportChannel { Id = -2, Data = PSDPixelEncoder.CompressPlaneRLE(mask, mw, mh) });
                rec.HasMask          = true;
                rec.MaskTop          = layer.MaskTop;
                rec.MaskLeft         = layer.MaskLeft;
                rec.MaskBottom       = layer.MaskBottom;
                rec.MaskRight        = layer.MaskRight;
                rec.MaskDefaultColor = layer.MaskDefaultColor;
                rec.MaskDisabled     = layer.MaskIsDisabled;
            }

            // R/G/B/A チャンネルが 1 つも無いレコード (調整レイヤー等) は他アプリで
            // 読み込みエラーの原因になるため、空のカラーチャンネルを先頭へ補う
            bool hasColorChannel = rec.Channels.Exists(c => c.Id >= -1);
            if (!hasColorChannel)
                rec.Channels.InsertRange(0, EmptyChannels());

            // 調整レイヤー / SoCo / GdFl の内容を追加情報ブロックとして書き戻す
            rec.ExtraBlocks = PSDAdjustmentInfoWriter.BuildBlocks(layer);

            // clbl (クリップ群をグループとしてブレンド) が既定値 (true) 以外なら保持する
            if (!layer.BlendClippedAsGroup)
            {
                if (rec.ExtraBlocks == null) rec.ExtraBlocks = new List<ExportExtraBlock>();
                rec.ExtraBlocks.Add(new ExportExtraBlock { Key = "clbl", Data = new byte[] { 0, 0, 0, 0 } });
            }

            return rec;
        }

        /// <summary>
        /// 画像クリップ合成を独立したクリッピングピクセルレイヤーとして書き出すレコードを組み立てる。
        /// 矩形・座標はベースレイヤーと同一。タイル展開済み画像をピクセルとして格納し、
        /// ブレンドモード/不透明度/クリッピングは PSD プロパティとして保持する。
        /// </summary>
        static ExportRecord BuildImageClipRecord(PSDLayer baseLayer, LayerCompositor comp)
        {
            var rec = new ExportRecord
            {
                Top      = baseLayer.Top,
                Left     = baseLayer.Left,
                Bottom   = baseLayer.Bottom,
                Right    = baseLayer.Right,
                Name     = baseLayer.Name + " 画像合成",
                BlendKey = PSDWriter.KeyOf(baseLayer.UIImageClipBlend),
                Opacity  = ToByteOpacity(baseLayer.UIImageClipOpacity),
                Clipping = 1,
                Flags    = 0x00, // 表示
            };

            int lw = baseLayer.Width, lh = baseLayer.Height;
            Color32[] px = comp != null ? comp.RenderImageClipForExport(baseLayer) : null;
            if (px != null && lw > 0 && lh > 0)
            {
                var rp = new byte[lw * lh];
                var gp = new byte[lw * lh];
                var bp = new byte[lw * lh];
                var ap = new byte[lw * lh];
                for (int i = 0; i < lw * lh; i++)
                {
                    rp[i] = px[i].r; gp[i] = px[i].g; bp[i] = px[i].b; ap[i] = px[i].a;
                }
                rec.Channels.Add(new ExportChannel { Id = 0,  Data = PSDPixelEncoder.CompressPlaneRLE(rp, lw, lh) });
                rec.Channels.Add(new ExportChannel { Id = 1,  Data = PSDPixelEncoder.CompressPlaneRLE(gp, lw, lh) });
                rec.Channels.Add(new ExportChannel { Id = 2,  Data = PSDPixelEncoder.CompressPlaneRLE(bp, lw, lh) });
                rec.Channels.Add(new ExportChannel { Id = -1, Data = PSDPixelEncoder.CompressPlaneRLE(ap, lw, lh) });
            }
            else
            {
                // 展開失敗時は空チャンネルでクラッシュを避ける
                rec.Channels = EmptyChannels();
            }

            return rec;
        }

        static bool NeedsBake(PSDLayer layer)
        {
            return !Mathf.Approximately(layer.UIBrightness, 0f)
                || !Mathf.Approximately(layer.UIContrast,   0f)
                || !Mathf.Approximately(layer.UIHue,        0f)
                || !Mathf.Approximately(layer.UISaturation, 0f)
                || !Mathf.Approximately(layer.UILightness,  0f)
                || layer.UIColorize
                || layer.UIInvert
                || layer.UIThresholdEnabled
                || layer.UIPosterizeEnabled
                || (layer.UILevelsEnabled && (
                       !Mathf.Approximately(layer.UILevelsInputBlack,  0f)
                    || !Mathf.Approximately(layer.UILevelsInputWhite,  255f)
                    || !Mathf.Approximately(layer.UILevelsGamma,       1f)
                    || !Mathf.Approximately(layer.UILevelsOutputBlack, 0f)
                    || !Mathf.Approximately(layer.UILevelsOutputWhite, 255f)
                   ))
                || (layer.UICurveEnabled && layer._curveLut != null)
                || (layer.UIGradientMapEnabled && layer._gradientLut != null)
                || layer.UIColorBalanceEnabled;
        }

        static byte ToByteOpacity(float opacity01)
            => (byte)Mathf.Clamp(Mathf.RoundToInt(opacity01 * 255f), 0, 255);
    }
}
