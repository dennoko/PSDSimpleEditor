using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PSDSimpleEditor
{
    /// <summary>
    /// レイヤー構造を保持した PSD バイナリを書き出す (PSDParser の逆)。
    ///
    /// 方針:
    ///   - 出力は常に 8bit RGB (colorMode 3, channels 4)。16bit/CMYK/LAB ソースも
    ///     RGBA32 読み戻し経由で 8bit RGB へ統一する。
    ///   - 色調補正・グラデーションマップは各レイヤーのピクセルへ焼き込む
    ///     (LayerCompositor.RenderLayerForExport)。ブレンドモード/不透明度/マスク/
    ///     グループ構造は PSD のプロパティとして保持する。
    ///   - グループは BuildLayerTree の逆変換でフラット化する:
    ///     ファイル格納順 (最下層→最上層) で「lsct type3 終端マーカー → 子 → フォルダ (type1/2)」。
    ///   - チャンネルは RLE (PackBits) で圧縮する。
    /// </summary>
    public static class PSDWriter
    {
        // ── BlendMode → 4 文字キー (PSDParser.KeyToBlendMode の逆) ──
        static readonly Dictionary<BlendMode, string> BlendModeToKey = new Dictionary<BlendMode, string>
        {
            { BlendMode.Normal,       "norm" }, { BlendMode.Multiply,     "mul " },
            { BlendMode.Screen,       "scrn" }, { BlendMode.Overlay,      "over" },
            { BlendMode.Dissolve,     "diss" }, { BlendMode.Darken,       "dark" },
            { BlendMode.ColorBurn,    "idiv" }, { BlendMode.LinearBurn,   "lbrn" },
            { BlendMode.DarkerColor,  "dkCl" }, { BlendMode.Lighten,      "lite" },
            { BlendMode.ColorDodge,   "div " }, { BlendMode.LinearDodge,  "lddg" },
            { BlendMode.LighterColor, "lgCl" }, { BlendMode.SoftLight,    "sLit" },
            { BlendMode.HardLight,    "hLit" }, { BlendMode.VividLight,   "vLit" },
            { BlendMode.LinearLight,  "lLit" }, { BlendMode.PinLight,     "pLit" },
            { BlendMode.HardMix,      "hMix" }, { BlendMode.Difference,   "diff" },
            { BlendMode.Exclusion,    "smud" }, { BlendMode.Subtract,     "fsub" },
            { BlendMode.Divide,       "fdiv" }, { BlendMode.Hue,          "hue " },
            { BlendMode.Saturation,   "sat " }, { BlendMode.Color,        "colr" },
            { BlendMode.Luminosity,   "lum " }, { BlendMode.PassThrough,  "pass" },
        };

        static string KeyOf(BlendMode mode)
            => BlendModeToKey.TryGetValue(mode, out var k) ? k : "norm";

        // ── 書き出し用に準備した 1 チャンネル分 (圧縮タグ込み) ──
        class ExportChannel
        {
            public short  Id;    // 0=R 1=G 2=B -1=A -2=Mask
            public byte[] Data;  // [uint16 compression][rowLens..][packbits rows..]
        }

        // ── 書き出し用に準備した 1 レコード分 ──
        class ExportRecord
        {
            public int Top, Left, Bottom, Right;
            public List<ExportChannel> Channels = new List<ExportChannel>();
            public string BlendKey = "norm";
            public byte   Opacity  = 255;
            public byte   Clipping;
            public byte   Flags;          // bit1 = 非表示
            public string Name = "";

            public int    LsctType;       // 0=通常 1=開フォルダ 2=閉フォルダ 3=終端マーカー
            public string GroupBlendKey;  // フォルダの lsct ブレンドキー (type1/2 のみ)

            public bool HasMask;
            public int  MaskTop, MaskLeft, MaskBottom, MaskRight;
            public byte MaskDefaultColor;
            public bool MaskDisabled;
        }

        // ════════════════════════════════════════════════════════════════
        //  Public
        // ════════════════════════════════════════════════════════════════

        public static void Save(PSDFile psd, List<PSDLayer> tree, LayerCompositor compositor,
                                Texture2D merged, string path)
        {
            if (psd == null) throw new ArgumentNullException(nameof(psd));
            int W = psd.Width, H = psd.Height;

            // レコードを先に組み立てる (チャンネルは圧縮済み = 長さ確定済み)
            var records = new List<ExportRecord>();
            BuildRecords(tree, records, compositor);

            using (var ms = new MemoryStream())
            using (var w = new BigEndianBinaryWriter(ms))
            {
                WriteHeader(w, W, H);
                w.WriteUInt32(0); // カラーモードデータ (なし)
                w.WriteUInt32(0); // 画像リソース (なし)
                WriteLayerAndMaskSection(w, records);
                WriteMergedImage(w, merged, W, H);

                w.Flush();
                File.WriteAllBytes(path, ms.ToArray());
            }
        }

        // ════════════════════════════════════════════════════════════════
        //  ヘッダ
        // ════════════════════════════════════════════════════════════════

        static void WriteHeader(BigEndianBinaryWriter w, int width, int height)
        {
            w.WriteAscii("8BPS");
            w.WriteUInt16(1);                 // version 1 (PSD)
            w.WriteBytesExact(new byte[6]);   // 予約領域
            w.WriteUInt16(4);                 // チャンネル数 (RGBA)
            w.WriteUInt32((uint)height);
            w.WriteUInt32((uint)width);
            w.WriteUInt16(8);                 // ビット深度
            w.WriteUInt16(3);                 // カラーモード = RGB
        }

        // ════════════════════════════════════════════════════════════════
        //  レイヤー & マスク情報セクション
        // ════════════════════════════════════════════════════════════════

        static void WriteLayerAndMaskSection(BigEndianBinaryWriter w, List<ExportRecord> records)
        {
            long sectionLenPos = w.Position;
            w.WriteUInt32(0); // セクション長プレースホルダ
            long sectionStart = w.Position;

            long layerInfoLenPos = w.Position;
            w.WriteUInt32(0); // レイヤー情報長プレースホルダ
            long layerInfoStart = w.Position;

            w.WriteInt16((short)records.Count);
            foreach (var rec in records) WriteLayerRecord(w, rec);
            foreach (var rec in records)
                foreach (var ch in rec.Channels)
                    w.WriteBytesExact(ch.Data);

            // レイヤー情報長は偶数パディング
            if (((w.Position - layerInfoStart) & 1) != 0) w.Write((byte)0);
            Backpatch(w, layerInfoLenPos, (uint)(w.Position - layerInfoStart));

            // グローバルレイヤーマスク情報 (なし)
            w.WriteUInt32(0);

            Backpatch(w, sectionLenPos, (uint)(w.Position - sectionStart));
        }

        static void WriteLayerRecord(BigEndianBinaryWriter w, ExportRecord rec)
        {
            w.WriteInt32(rec.Top);
            w.WriteInt32(rec.Left);
            w.WriteInt32(rec.Bottom);
            w.WriteInt32(rec.Right);

            w.WriteUInt16((ushort)rec.Channels.Count);
            foreach (var ch in rec.Channels)
            {
                w.WriteInt16(ch.Id);
                w.WriteUInt32((uint)ch.Data.Length);
            }

            w.WriteAscii("8BIM");
            w.WriteAscii(rec.BlendKey);
            w.Write(rec.Opacity);
            w.Write(rec.Clipping);
            w.Write(rec.Flags);
            w.Write((byte)0); // filler

            // ── エクストラデータ ──
            long extraLenPos = w.Position;
            w.WriteUInt32(0); // プレースホルダ
            long extraStart = w.Position;

            // マスクデータ
            if (rec.HasMask)
            {
                w.WriteUInt32(20);
                w.WriteInt32(rec.MaskTop);
                w.WriteInt32(rec.MaskLeft);
                w.WriteInt32(rec.MaskBottom);
                w.WriteInt32(rec.MaskRight);
                w.Write(rec.MaskDefaultColor);
                w.Write((byte)(rec.MaskDisabled ? 0x02 : 0x00)); // bit0=0 → 絶対座標
            }
            else
            {
                w.WriteUInt32(0);
            }

            // ブレンディングレンジ (なし)
            w.WriteUInt32(0);

            // Pascal 名 (4 バイト境界パディング)
            w.WritePascalString(rec.Name, 4);

            // 追加情報: luni (Unicode 名)
            WriteAdditionalLuni(w, rec.Name);

            // 追加情報: lsct (グループ/終端マーカー)
            if (rec.LsctType != 0)
                WriteAdditionalLsct(w, rec);

            Backpatch(w, extraLenPos, (uint)(w.Position - extraStart));
        }

        static void WriteAdditionalLuni(BigEndianBinaryWriter w, string name)
        {
            w.WriteAscii("8BIM");
            w.WriteAscii("luni");
            long lenPos = w.Position;
            w.WriteUInt32(0);
            long start = w.Position;
            w.WriteUnicodeString(name ?? "");
            Backpatch(w, lenPos, (uint)(w.Position - start));
        }

        static void WriteAdditionalLsct(BigEndianBinaryWriter w, ExportRecord rec)
        {
            w.WriteAscii("8BIM");
            w.WriteAscii("lsct");
            if (rec.LsctType == 3)
            {
                // 終端マーカー: type のみ
                w.WriteUInt32(4);
                w.WriteUInt32(3);
            }
            else
            {
                // フォルダ: type + 8BIM + ブレンドキー
                w.WriteUInt32(12);
                w.WriteUInt32((uint)rec.LsctType);
                w.WriteAscii("8BIM");
                w.WriteAscii(rec.GroupBlendKey ?? "pass");
            }
        }

        // ════════════════════════════════════════════════════════════════
        //  ツリー → フラットレコード (BuildLayerTree の逆)
        // ════════════════════════════════════════════════════════════════

        static void BuildRecords(List<PSDLayer> layers, List<ExportRecord> outRecs, LayerCompositor comp)
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
            string gkey = KeyOf(layer.GroupBlendMode);
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
                BlendKey = KeyOf(layer.BlendMode),
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
                if (px == null) px = ReadTextureTopDown(layer.Texture);

                var rp = new byte[lw * lh];
                var gp = new byte[lw * lh];
                var bp = new byte[lw * lh];
                var ap = new byte[lw * lh];
                for (int i = 0; i < lw * lh; i++)
                {
                    rp[i] = px[i].r; gp[i] = px[i].g; bp[i] = px[i].b; ap[i] = px[i].a;
                }
                rec.Channels.Add(new ExportChannel { Id = 0,  Data = CompressPlaneRLE(rp, lw, lh) });
                rec.Channels.Add(new ExportChannel { Id = 1,  Data = CompressPlaneRLE(gp, lw, lh) });
                rec.Channels.Add(new ExportChannel { Id = 2,  Data = CompressPlaneRLE(bp, lw, lh) });
                rec.Channels.Add(new ExportChannel { Id = -1, Data = CompressPlaneRLE(ap, lw, lh) });
            }

            // レイヤーマスク (チャンネル -2 + マスクデータブロック)
            int mw = layer.MaskRight - layer.MaskLeft;
            int mh = layer.MaskBottom - layer.MaskTop;
            if (layer.HasMask && layer.MaskTexture != null && mw > 0 && mh > 0)
            {
                byte[] mask = ReadMaskTopDown(layer.MaskTexture, mw, mh);
                rec.Channels.Add(new ExportChannel { Id = -2, Data = CompressPlaneRLE(mask, mw, mh) });
                rec.HasMask          = true;
                rec.MaskTop          = layer.MaskTop;
                rec.MaskLeft         = layer.MaskLeft;
                rec.MaskBottom       = layer.MaskBottom;
                rec.MaskRight        = layer.MaskRight;
                rec.MaskDefaultColor = layer.MaskDefaultColor;
                rec.MaskDisabled     = layer.MaskIsDisabled;
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
                BlendKey = KeyOf(baseLayer.UIImageClipBlend),
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
                rec.Channels.Add(new ExportChannel { Id = 0,  Data = CompressPlaneRLE(rp, lw, lh) });
                rec.Channels.Add(new ExportChannel { Id = 1,  Data = CompressPlaneRLE(gp, lw, lh) });
                rec.Channels.Add(new ExportChannel { Id = 2,  Data = CompressPlaneRLE(bp, lw, lh) });
                rec.Channels.Add(new ExportChannel { Id = -1, Data = CompressPlaneRLE(ap, lw, lh) });
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
                || (layer.UIGradientMapEnabled && layer._gradientLut != null);
        }

        static byte ToByteOpacity(float opacity01)
            => (byte)Mathf.Clamp(Mathf.RoundToInt(opacity01 * 255f), 0, 255);

        // ════════════════════════════════════════════════════════════════
        //  マージ済み画像 (Section 5)
        // ════════════════════════════════════════════════════════════════

        static void WriteMergedImage(BigEndianBinaryWriter w, Texture2D merged, int W, int H)
        {
            Color32[] px = (merged != null && merged.width == W && merged.height == H)
                ? ReadTextureTopDown(merged)
                : new Color32[W * H]; // 合成結果が無ければ透明

            int n = W * H;
            var planes = new byte[4][];
            for (int c = 0; c < 4; c++) planes[c] = new byte[n];
            for (int i = 0; i < n; i++)
            {
                planes[0][i] = px[i].r;
                planes[1][i] = px[i].g;
                planes[2][i] = px[i].b;
                planes[3][i] = px[i].a;
            }

            // RLE: 先頭に全チャンネル分の行長テーブル (4 × H)、続いて行データ
            w.WriteUInt16(1); // compression = RLE
            var rows = new byte[4 * H][];
            for (int c = 0; c < 4; c++)
                for (int y = 0; y < H; y++)
                    rows[c * H + y] = PackBits(planes[c], y * W, W);

            for (int i = 0; i < rows.Length; i++) w.WriteUInt16((ushort)rows[i].Length);
            for (int i = 0; i < rows.Length; i++) w.WriteBytesExact(rows[i]);
        }

        // ════════════════════════════════════════════════════════════════
        //  チャンネル圧縮 (RLE / PackBits)
        // ════════════════════════════════════════════════════════════════

        /// <summary>1 プレーン (w×h, トップダウン) を [uint16 compr=1][行長テーブル][packbits 行] に圧縮する。</summary>
        static byte[] CompressPlaneRLE(byte[] plane, int w, int h)
        {
            var rows = new byte[h][];
            for (int y = 0; y < h; y++)
                rows[y] = PackBits(plane, y * w, w);

            using (var ms = new MemoryStream())
            using (var bw = new BigEndianBinaryWriter(ms))
            {
                bw.WriteUInt16(1); // compression = RLE
                for (int y = 0; y < h; y++) bw.WriteUInt16((ushort)rows[y].Length);
                for (int y = 0; y < h; y++) bw.WriteBytesExact(rows[y]);
                bw.Flush();
                return ms.ToArray();
            }
        }

        /// <summary>PackBits で 1 走査線を圧縮する (DecodePackBitsRow の逆)。</summary>
        static byte[] PackBits(byte[] src, int off, int len)
        {
            var o = new List<byte>(len + (len >> 7) + 1);
            int i = 0;
            while (i < len)
            {
                // 同一バイトのラン長 (最大 128)
                int run = 1;
                while (i + run < len && run < 128 && src[off + i + run] == src[off + i]) run++;

                if (run >= 3)
                {
                    o.Add((byte)(1 - run)); // [-127..-1]
                    o.Add(src[off + i]);
                    i += run;
                }
                else
                {
                    // リテラル: 3 連続以上のランが現れるか 128 まで収集
                    int litStart = i;
                    int lit = 0;
                    while (i < len && lit < 128)
                    {
                        int r = 1;
                        while (i + r < len && r < 3 && src[off + i + r] == src[off + i]) r++;
                        if (r >= 3) break; // ラン開始 → リテラル終了
                        i++; lit++;
                    }
                    o.Add((byte)(lit - 1)); // [0..127]
                    for (int k = 0; k < lit; k++) o.Add(src[off + litStart + k]);
                }
            }
            return o.ToArray();
        }

        // ════════════════════════════════════════════════════════════════
        //  テクスチャ読み戻し (トップダウン化)
        // ════════════════════════════════════════════════════════════════

        /// <summary>RGBA32 テクスチャをトップダウン (行 0 = 上端) の Color32[] で読み戻す。</summary>
        static Color32[] ReadTextureTopDown(Texture2D tex)
        {
            int w = tex.width, h = tex.height;
            var bottomUp = tex.GetPixels32(); // 行 0 = 下端
            var topDown  = new Color32[w * h];
            for (int y = 0; y < h; y++)
                Array.Copy(bottomUp, (h - 1 - y) * w, topDown, y * w, w);
            return topDown;
        }

        /// <summary>R8 マスクテクスチャをトップダウンの 8bit プレーンで読み戻す。</summary>
        static byte[] ReadMaskTopDown(Texture2D mask, int w, int h)
        {
            var top = new byte[w * h];
            // R8 は GetPixels32 の .r に値が入る (ボトムアップ)
            var px = mask.GetPixels32();
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    top[y * w + x] = px[(h - 1 - y) * w + x].r;
            return top;
        }

        // ════════════════════════════════════════════════════════════════
        //  ユーティリティ
        // ════════════════════════════════════════════════════════════════

        static void Backpatch(BigEndianBinaryWriter w, long lenPos, uint value)
        {
            long cur = w.Position;
            w.Seek(lenPos);
            w.WriteUInt32(value);
            w.Seek(cur);
        }
    }
}
