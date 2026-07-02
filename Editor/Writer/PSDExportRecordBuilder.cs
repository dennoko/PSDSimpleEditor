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

                    // 非破壊調整を dPSE マーカー付きクリップ調整レイヤーとして真上に積む
                    // (焼き込み対象レイヤーは何も積まれない)
                    AppendAdjustmentClipRecords(layer, outRecs);

                    // Color Overlay 効果はベタ塗り (SoCo) のクリッピングレイヤーへ変換する。
                    // クリップメンバー自身の効果は適用対象が変わってしまうため変換しない (消失)。
                    if (layer.Effects != null && layer.Effects.HasColorOverlay &&
                        !layer.IsClipping && layer.Texture != null)
                        outRecs.Add(BuildColorOverlayClipRecord(layer));

                    // 画像クリップ合成は独立したクリッピングレイヤーとして最上段に挿入する。
                    // (ファイル格納順 = 下→上なので、直後 append = クリップ群の最上段)
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
                // 調整レイヤーで表現できない補正だけ画素へ焼き込み、それ以外は素のテクスチャを読み戻す
                // (表現できる補正は AppendAdjustmentClipRecords がクリップ調整レイヤーとして書き出す)
                Color32[] px = WillBakeAdjustments(layer) && comp != null
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

        // ════════════════════════════════════════════════════════════════
        //  非破壊調整のクリップ調整レイヤー化
        // ════════════════════════════════════════════════════════════════

        /// <summary>塗りつぶし系 (SoCo/GdFl) でもグループでもない、画素を持つ通常レイヤー。</summary>
        static bool IsPlainPixelLayer(PSDLayer layer)
        {
            if (layer.Children != null || layer.Texture == null || layer.IsAdjustmentLayer) return false;
            var a = layer.Adjustment;
            return a == null || (!a.HasSolidColor && !a.HasGradientFill);
        }

        static bool HasAnyActiveAdjustment(PSDLayer layer)
        {
            return !Mathf.Approximately(layer.UIBrightness, 0f)
                || !Mathf.Approximately(layer.UIContrast,   0f)
                || HasActiveHsl(layer)
                || layer.UIInvert
                || layer.UIThresholdEnabled
                || layer.UIPosterizeEnabled
                || layer.UILevelsEnabled
                || (layer.UICurveEnabled && layer.UICurve != null)
                || (layer.UIGradientMapEnabled && layer.UIGradient != null)
                || layer.UIColorBalanceEnabled;
        }

        static bool HasActiveHsl(PSDLayer layer)
        {
            return !Mathf.Approximately(layer.UIHue,        0f)
                || !Mathf.Approximately(layer.UISaturation, 0f)
                || !Mathf.Approximately(layer.UILightness,  0f)
                || layer.UIColorize;
        }

        /// <summary>
        /// このレイヤーの非破壊調整を従来どおり画素へ焼き込むか (= クリップ調整レイヤーで
        /// 表現できないか)。書き出しダイアログの警告表示にも使う。
        /// </summary>
        internal static bool WillBakeAdjustments(PSDLayer layer)
        {
            if (!IsPlainPixelLayer(layer) || !HasAnyActiveAdjustment(layer)) return false;
            // クリップメンバー自身の補正: クリップ調整レイヤーにすると適用対象が
            // 「そのメンバー単体」から「クリップ群の合成結果」へ変わってしまう
            if (layer.IsClipping) return true;
            // clbl=false のベース: メンバーが背景バッファへ直接ブレンドされるため
            // 「ブレンド前のベース画素への補正」を調整レイヤーで再現できない
            if (!layer.BlendClippedAsGroup) return true;
            // 輝度正規化グラデーションマップは PSD に対応する表現が無い
            if (layer.UIGradientMapEnabled && layer.UIGradientMapNormalize) return true;
            return false;
        }

        /// <summary>
        /// レイヤーの有効な非破壊調整を、dPSE マーカー付きのクリップ調整レイヤー
        /// (ゼロ面積 + Clipping=1 + 調整キー) としてベースの直上へ積む。
        /// 積み順はシェーダー (LayerBlend.shader ApplyAdjustments) の適用順に合わせる:
        /// 反転 → レベル → カーブ → ポスタリ → しきい値 → 明るさ/コントラスト
        /// → カラーバランス → 色相・彩度 → グラデーションマップ。
        /// </summary>
        static void AppendAdjustmentClipRecords(PSDLayer layer, List<ExportRecord> outRecs)
        {
            if (!IsPlainPixelLayer(layer) || WillBakeAdjustments(layer)) return;

            if (layer.UIInvert)
                outRecs.Add(BuildAdjustmentClipRecord("階調の反転",
                    new ExportExtraBlock { Key = "nvrt", Data = new byte[0] }));

            if (layer.UILevelsEnabled)
                outRecs.Add(BuildAdjustmentClipRecord("レベル補正",
                    PSDAdjustmentInfoWriter.EncodeLevl(layer)));

            if (layer.UICurveEnabled && layer.UICurve != null)
            {
                var curv = PSDAdjustmentInfoWriter.EncodeCurv(layer);
                if (curv != null)
                    outRecs.Add(BuildAdjustmentClipRecord("トーンカーブ", curv));
            }

            if (layer.UIPosterizeEnabled)
                outRecs.Add(BuildAdjustmentClipRecord("ポスタリゼーション",
                    PSDAdjustmentInfoWriter.EncodePost(layer.UIPosterizeLevels)));

            if (layer.UIThresholdEnabled)
                outRecs.Add(BuildAdjustmentClipRecord("2階調化",
                    PSDAdjustmentInfoWriter.EncodeThrs(layer.UIThresholdLevel)));

            if (!Mathf.Approximately(layer.UIBrightness, 0f) || !Mathf.Approximately(layer.UIContrast, 0f))
                outRecs.Add(BuildAdjustmentClipRecord("明るさ・コントラスト",
                    PSDAdjustmentInfoWriter.EncodeBrit(layer.UIBrightness, layer.UIContrast),
                    PSDAdjustmentInfoWriter.EncodeCgEd(layer.UIBrightness, layer.UIContrast)));

            if (layer.UIColorBalanceEnabled)
                outRecs.Add(BuildAdjustmentClipRecord("カラーバランス",
                    PSDAdjustmentInfoWriter.EncodeBlnc(layer)));

            if (HasActiveHsl(layer))
                outRecs.Add(BuildAdjustmentClipRecord("色相・彩度",
                    PSDAdjustmentInfoWriter.EncodeHue2(
                        layer.UIHue, layer.UISaturation, layer.UILightness, layer.UIColorize)));

            if (layer.UIGradientMapEnabled && layer.UIGradient != null)
            {
                var rec = BuildAdjustmentClipRecord("グラデーションマップ",
                    PSDAdjustmentInfoWriter.EncodeGrdm(layer.UIGradient));
                rec.Opacity = ToByteOpacity(layer.UIGradientMapOpacity); // 適用率 → レイヤー不透明度
                outRecs.Add(rec);
            }
        }

        /// <summary>ゼロ面積 + Clipping=1 + マーカー付きのクリップ調整レイヤーレコード。</summary>
        static ExportRecord BuildAdjustmentClipRecord(string name, params ExportExtraBlock[] blocks)
        {
            var extra = new List<ExportExtraBlock>(blocks);
            extra.Add(PSDAdjustmentInfoWriter.BuildClipMarkerBlock());
            return new ExportRecord
            {
                Name        = name,
                BlendKey    = "norm",
                Opacity     = 255,
                Clipping    = 1,
                Flags       = 0x00, // 表示
                Channels    = EmptyChannels(),
                ExtraBlocks = extra,
            };
        }

        /// <summary>
        /// Color Overlay 効果をベタ塗り (SoCo) のクリッピングレイヤーへ変換する。
        /// マーカーは付けない (再読み込み後はベタ塗りクリップレイヤーとして扱われ、
        /// クリップ群合成によって効果とほぼ同じ見た目で再現される)。
        /// </summary>
        static ExportRecord BuildColorOverlayClipRecord(PSDLayer layer)
        {
            var fx = layer.Effects;
            return new ExportRecord
            {
                Name        = "カラーオーバーレイ",
                BlendKey    = PSDWriter.KeyOf(fx.OverlayBlendMode),
                Opacity     = ToByteOpacity(fx.OverlayOpacity),
                Clipping    = 1,
                Flags       = 0x00,
                Channels    = EmptyChannels(),
                ExtraBlocks = new List<ExportExtraBlock>
                {
                    PSDAdjustmentInfoWriter.EncodeSoCo(fx.OverlayColor),
                },
            };
        }

        static byte ToByteOpacity(float opacity01)
            => (byte)Mathf.Clamp(Mathf.RoundToInt(opacity01 * 255f), 0, 255);
    }
}
