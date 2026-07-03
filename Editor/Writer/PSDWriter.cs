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
    ///
    /// 実装はレコード組み立て (PSDExportRecordBuilder) / レコード書き込み (PSDLayerRecordWriter) /
    /// ピクセル圧縮 (PSDPixelEncoder) へ分割されている。このクラスは公開 API とヘッダ・
    /// マージ画像セクションの書き込み、レコード間で共有するユーティリティを担う。
    /// </summary>
    public static class PSDWriter
    {
        // BlendMode → 4 文字キー。表の実体は Core の PSDBlendModeConverter が
        // 読み込み用 KeyToBlendMode から自動生成する (read/write で表を二重管理しない)。
        internal static string KeyOf(BlendMode mode) => PSDBlendModeConverter.KeyOf(mode);

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
            PSDExportRecordBuilder.BuildRecords(tree, records, compositor);

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
            foreach (var rec in records) PSDLayerRecordWriter.WriteLayerRecord(w, rec);
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

        // ════════════════════════════════════════════════════════════════
        //  マージ済み画像 (Section 5)
        // ════════════════════════════════════════════════════════════════

        static void WriteMergedImage(BigEndianBinaryWriter w, Texture2D merged, int W, int H)
        {
            Color32[] px = (merged != null && merged.width == W && merged.height == H)
                ? PSDPixelEncoder.ReadTextureTopDown(merged)
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
                    rows[c * H + y] = PSDPixelEncoder.PackBits(planes[c], y * W, W);

            for (int i = 0; i < rows.Length; i++) w.WriteUInt16((ushort)rows[i].Length);
            for (int i = 0; i < rows.Length; i++) w.WriteBytesExact(rows[i]);
        }

        // ════════════════════════════════════════════════════════════════
        //  ユーティリティ
        // ════════════════════════════════════════════════════════════════

        internal static void Backpatch(BigEndianBinaryWriter w, long lenPos, uint value)
        {
            long cur = w.Position;
            w.Seek(lenPos);
            w.WriteUInt32(value);
            w.Seek(cur);
        }
    }
}
