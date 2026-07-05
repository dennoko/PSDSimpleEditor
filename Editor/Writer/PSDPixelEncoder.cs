using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace PSDSimpleEditor
{
    // ════════════════════════════════════════════════════════════════
    //  チャンネル圧縮 (RLE / PackBits) / テクスチャ読み戻し (トップダウン化)
    // ════════════════════════════════════════════════════════════════
    internal static class PSDPixelEncoder
    {
        /// <summary>1 プレーン (w×h, トップダウン) を [uint16 compr=1][行長テーブル][packbits 行] に圧縮する。</summary>
        internal static byte[] CompressPlaneRLE(byte[] plane, int w, int h)
        {
            // 各行の PackBits は独立しているため並列に圧縮する (行順は組み立て時に維持)
            var rows = new byte[h][];
            Parallel.For(0, h, y => rows[y] = PackBits(plane, y * w, w));

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
        internal static byte[] PackBits(byte[] src, int off, int len)
        {
            // 最悪ケース (ラン皆無) は 128 バイトごとに制御 1 バイト + 端数
            var buf = new byte[len + (len >> 7) + 2];
            int oi = 0;
            int i = 0;
            while (i < len)
            {
                // 同一バイトのラン長 (最大 128)
                int run = 1;
                while (i + run < len && run < 128 && src[off + i + run] == src[off + i]) run++;

                if (run >= 3)
                {
                    buf[oi++] = (byte)(1 - run); // [-127..-1]
                    buf[oi++] = src[off + i];
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
                    buf[oi++] = (byte)(lit - 1); // [0..127]
                    Buffer.BlockCopy(src, off + litStart, buf, oi, lit);
                    oi += lit;
                }
            }

            var result = new byte[oi];
            Buffer.BlockCopy(buf, 0, result, 0, oi);
            return result;
        }

        /// <summary>RGBA32 テクスチャをトップダウン (行 0 = 上端) の Color32[] で読み戻す。</summary>
        internal static Color32[] ReadTextureTopDown(Texture2D tex)
        {
            int w = tex.width, h = tex.height;
            var bottomUp = tex.GetPixels32(); // 行 0 = 下端
            var topDown  = new Color32[w * h];
            for (int y = 0; y < h; y++)
                Array.Copy(bottomUp, (h - 1 - y) * w, topDown, y * w, w);
            return topDown;
        }

        /// <summary>R8 マスクテクスチャをトップダウンの 8bit プレーンで読み戻す。</summary>
        internal static byte[] ReadMaskTopDown(Texture2D mask, int w, int h)
        {
            var top = new byte[w * h];
            // R8 は GetPixels32 の .r に値が入る (ボトムアップ)
            var px = mask.GetPixels32();
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    top[y * w + x] = px[(h - 1 - y) * w + x].r;
            return top;
        }
    }
}
