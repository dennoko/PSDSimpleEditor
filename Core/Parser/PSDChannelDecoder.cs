using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEngine;

namespace PSDSimpleEditor
{
    // ════════════════════════════════════════════════════════════════
    //  チャンネル画像データ (Raw / RLE / ZIP 展開)
    // ════════════════════════════════════════════════════════════════
    internal static class PSDChannelDecoder
    {
        internal static void ReadLayerChannels(BigEndianBinaryReader r, PSDFile psd, PSDLayer layer)
        {
            int bytesPerSample = psd.BitDepth / 8;
            var planes = new Dictionary<int, byte[]>(); // チャンネル id → 8bit プレーン
            byte[] maskPlane = null;

            foreach (var ch in layer.Channels)
            {
                long channelStart = r.Position;
                long channelEnd   = channelStart + (uint)ch.DataLength;
                try
                {
                    if (ch.ChannelId == -3)
                        continue; // ベクターマスク: 読まずにスキップ (finally で channelEnd へ)

                    // 使用する矩形: id==-2 はマスク矩形 / それ以外はレイヤー矩形
                    int w, h;
                    if (ch.ChannelId == -2)
                    {
                        if (!layer.HasMask) continue;
                        w = layer.MaskRight - layer.MaskLeft;
                        h = layer.MaskBottom - layer.MaskTop;
                    }
                    else
                    {
                        w = layer.Width;
                        h = layer.Height;
                    }
                    if (w <= 0 || h <= 0 || ch.DataLength < 2)
                        continue;

                    ushort compression = r.ReadUInt16();
                    byte[] plane = DecodeChannel(r, compression, w, h, bytesPerSample, ch.DataLength - 2);
                    if (plane == null) continue;

                    // 16bit → 8bit: 0..32768 レンジを 0..255 へスケール
                    if (psd.BitDepth == 16)
                        plane = Downsample16To8(plane, w * h);

                    if (ch.ChannelId == -2) maskPlane = plane;
                    else                    planes[ch.ChannelId] = plane;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[PSDParser] レイヤー '{layer.Name}' チャンネル {ch.ChannelId} の読み取りに失敗 (スキップ): {e.Message}");
                }
                finally { r.Seek(channelEnd); } // チャンネル末尾で必ず境界 seek
            }

            AssembleLayerPixels(psd, layer, planes, maskPlane);
        }

        /// <summary>1 チャンネル分のピクセルデータを解凍し、(w × h × bytesPerSample) のプレーンを返す。</summary>
        internal static byte[] DecodeChannel(BigEndianBinaryReader r, ushort compression, int w, int h, int bytesPerSample, long payloadLen)
        {
            int rowBytes = w * bytesPerSample;
            int total    = rowBytes * h;

            switch (compression)
            {
                case 0: // Raw
                    return r.ReadBytesExact(total);

                case 1: // RLE (行ごとの PackBits)
                {
                    var rowLens = new int[h];
                    for (int y = 0; y < h; y++)
                        rowLens[y] = r.ReadUInt16();
                    var dst = new byte[total];
                    for (int y = 0; y < h; y++)
                    {
                        byte[] src = r.ReadBytesExact(rowLens[y]);
                        DecodePackBitsRow(src, dst, y * rowBytes, rowBytes);
                    }
                    return dst;
                }

                case 2: // ZIP (zlib)
                case 3: // ZIP + prediction
                {
                    if (payloadLen < 2) return null;
                    byte[] compressed = r.ReadBytesExact((int)payloadLen);
                    byte[] raw = InflateZlib(compressed, total);
                    if (compression == 3)
                        UndoPrediction(raw, w, h, bytesPerSample);
                    return raw;
                }

                default:
                    Debug.LogWarning($"[PSDParser] 未知の圧縮形式: {compression}");
                    return null;
            }
        }

        /// <summary>PackBits 1 行分を解凍する。境界を超える破損データはクランプして続行。</summary>
        internal static void DecodePackBitsRow(byte[] src, byte[] dst, int dstOffset, int rowBytes)
        {
            int si = 0, di = dstOffset, dEnd = dstOffset + rowBytes;
            while (si < src.Length && di < dEnd)
            {
                sbyte n = (sbyte)src[si++];
                if (n >= 0)
                {
                    // リテラル: 続く (n+1) バイトをコピー
                    int count = n + 1;
                    if (si + count > src.Length) count = src.Length - si;
                    if (di + count > dEnd)       count = dEnd - di;
                    if (count <= 0) break;
                    Buffer.BlockCopy(src, si, dst, di, count);
                    si += count;
                    di += count;
                }
                else if (n != -128) // -128 は no-op
                {
                    // ラン: 次の 1 バイトを (1-n) 回繰り返す
                    int count = 1 - n;
                    if (si >= src.Length) break;
                    byte v = src[si++];
                    if (di + count > dEnd) count = dEnd - di;
                    for (int k = 0; k < count; k++) dst[di++] = v;
                }
            }
        }

        /// <summary>zlib ストリームを解凍する (先頭 2 バイトの zlib ヘッダを skip して DeflateStream)。</summary>
        static byte[] InflateZlib(byte[] compressed, int expectedLength)
        {
            var dst = new byte[expectedLength];
            using (var ms = new MemoryStream(compressed, 2, compressed.Length - 2, false))
            using (var ds = new DeflateStream(ms, CompressionMode.Decompress))
            {
                int offset = 0;
                while (offset < expectedLength)
                {
                    int n = ds.Read(dst, offset, expectedLength - offset);
                    if (n <= 0) break;
                    offset += n;
                }
                if (offset < expectedLength)
                    Debug.LogWarning($"[PSDParser] ZIP データが想定より短いです ({offset}/{expectedLength} バイト)");
            }
            return dst;
        }

        /// <summary>ZIP+prediction の行ごと横方向デルタを復元する (16bit は BE uint16 単位)。</summary>
        static void UndoPrediction(byte[] data, int w, int h, int bytesPerSample)
        {
            if (bytesPerSample == 1)
            {
                for (int y = 0; y < h; y++)
                {
                    int o = y * w;
                    for (int x = 1; x < w; x++)
                        data[o + x] = (byte)(data[o + x] + data[o + x - 1]);
                }
            }
            else // 16bit
            {
                int rowBytes = w * 2;
                for (int y = 0; y < h; y++)
                {
                    int o = y * rowBytes;
                    for (int x = 1; x < w; x++)
                    {
                        int p = o + (x - 1) * 2;
                        int c = o + x * 2;
                        int prev = (data[p] << 8) | data[p + 1];
                        int cur  = (data[c] << 8) | data[c + 1];
                        int v = (prev + cur) & 0xFFFF;
                        data[c]     = (byte)(v >> 8);
                        data[c + 1] = (byte)v;
                    }
                }
            }
        }

        /// <summary>
        /// 16bit プレーン → 8bit プレーン。
        /// Photoshop の 16bit チャンネルは 0..32768 (0x8000 = 白) のレンジで格納されるため、
        /// BE ペアを 16bit 値へ復元してから 0..255 へスケールする (単純な上位バイト採用だと白が 128 になり半分の明るさになる)。
        /// </summary>
        internal static byte[] Downsample16To8(byte[] plane16, int pixelCount)
        {
            var plane8 = new byte[pixelCount];
            int limit = Math.Min(pixelCount, plane16.Length / 2);
            for (int i = 0; i < limit; i++)
            {
                int v = (plane16[i * 2] << 8) | plane16[i * 2 + 1];
                plane8[i] = v >= 32768 ? (byte)255 : (byte)((v * 255 + 16384) / 32768);
            }
            return plane8;
        }

        /// <summary>チャンネルプレーンから RGBA32 (トップダウン) の生バッファを組み立てる。</summary>
        static void AssembleLayerPixels(PSDFile psd, PSDLayer layer, Dictionary<int, byte[]> planes, byte[] maskPlane)
        {
            int w = layer.Width, h = layer.Height;
            if (w > 0 && h > 0 && planes.Count > 0)
            {
                int n = w * h;
                var rgba = new byte[n * 4];
                planes.TryGetValue(0,  out byte[] pr);
                planes.TryGetValue(1,  out byte[] pg);
                planes.TryGetValue(2,  out byte[] pb);
                planes.TryGetValue(-1, out byte[] pa);
                bool grayscale = psd.ColorMode == 1;

                for (int i = 0, o = 0; i < n; i++, o += 4)
                {
                    byte cr = pr != null ? pr[i] : (byte)0;
                    rgba[o]     = cr;
                    rgba[o + 1] = grayscale ? cr : (pg != null ? pg[i] : (byte)0); // Grayscale は R を複製
                    rgba[o + 2] = grayscale ? cr : (pb != null ? pb[i] : (byte)0);
                    rgba[o + 3] = pa != null ? pa[i] : (byte)255; // α 無しは 255
                }
                layer._rawPixels = rgba;
            }

            int mw = layer.MaskRight - layer.MaskLeft;
            int mh = layer.MaskBottom - layer.MaskTop;
            if (maskPlane != null && mw > 0 && mh > 0 && maskPlane.Length >= mw * mh)
                layer._rawMaskPixels = maskPlane;
        }
    }
}
