using System;
using System.Text;
using UnityEngine;

namespace PSDSimpleEditor
{
    // ════════════════════════════════════════════════════════════════
    //  Section 5: マージ済み画像
    // ════════════════════════════════════════════════════════════════
    internal static class PSDMergedImageParser
    {
        internal static void ParseMergedImage(BigEndianBinaryReader r, PSDFile psd, StringBuilder vlog)
        {
            try
            {
                if (r.Length - r.Position < 2) return; // マージ画像なし

                ushort compression = r.ReadUInt16();
                int w = psd.Width, h = psd.Height;
                int channels = psd.Channels;
                int bytesPerSample = psd.BitDepth / 8;
                int rowBytes = w * bytesPerSample;

                var planes = new byte[channels][];
                if (compression == 0) // Raw (planar)
                {
                    for (int c = 0; c < channels; c++)
                        planes[c] = r.ReadBytesExact(rowBytes * h);
                }
                else if (compression == 1) // RLE
                {
                    // 全チャンネル分の行バイト数 (channels × height) が先頭にまとまっている
                    var rowLens = new int[channels * h];
                    for (int i = 0; i < rowLens.Length; i++)
                        rowLens[i] = r.ReadUInt16();

                    for (int c = 0; c < channels; c++)
                    {
                        // チャンネル全行を一括で読み、オフセット参照で行ごとに解凍する
                        long srcTotal = 0;
                        for (int y = 0; y < h; y++) srcTotal += rowLens[c * h + y];
                        byte[] src = r.ReadBytesExact((int)srcTotal);

                        var plane = new byte[rowBytes * h];
                        int so = 0;
                        for (int y = 0; y < h; y++)
                        {
                            int len = rowLens[c * h + y];
                            PSDChannelDecoder.DecodePackBitsRow(src, so, len, plane, y * rowBytes, rowBytes);
                            so += len;
                        }
                        planes[c] = plane;
                    }
                }
                else
                {
                    Debug.LogWarning($"[PSDParser] マージ画像の圧縮形式 {compression} は非対応です");
                    return;
                }

                if (psd.BitDepth == 16)
                    for (int c = 0; c < channels; c++)
                        planes[c] = PSDChannelDecoder.Downsample16To8(planes[c], w * h);

                byte[] rgba = AssembleMergedPixels(psd, planes, w, h);
                if (rgba == null) return;

                // マージ参照用テクスチャはエディタGUIでのプレビュー表示専用のため、linear: false (sRGB) で作成する
                psd.MergedComposite = PSDLayerAssembler.CreateTexture(rgba, w, h, TextureFormat.RGBA32, "Merged Composite", false);
                vlog?.AppendLine($"  マージ画像: {w}x{h} compression={compression}");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PSDParser] マージ済み画像の読み取りに失敗: {e.Message}");
                psd.MergedComposite = null;
            }
        }

        static byte[] AssembleMergedPixels(PSDFile psd, byte[][] planes, int w, int h)
        {
            int n = w * h;
            var rgba = new byte[n * 4];
            int channels = planes.Length;

            switch (psd.ColorMode)
            {
                case 3: // RGB (channels>=4 なら 4ch 目を α に)
                    if (channels < 3) return null;
                    for (int i = 0, o = 0; i < n; i++, o += 4)
                    {
                        rgba[o]     = planes[0][i];
                        rgba[o + 1] = planes[1][i];
                        rgba[o + 2] = planes[2][i];
                        rgba[o + 3] = channels >= 4 ? planes[3][i] : (byte)255;
                    }
                    return rgba;

                case 1: // Grayscale (R 複製)
                    if (channels < 1) return null;
                    for (int i = 0, o = 0; i < n; i++, o += 4)
                    {
                        byte v = planes[0][i];
                        rgba[o] = v; rgba[o + 1] = v; rgba[o + 2] = v;
                        rgba[o + 3] = channels >= 2 ? planes[1][i] : (byte)255;
                    }
                    return rgba;

                case 4: // CMYK (反転格納 → 乗算近似で RGB へ)
                    if (channels < 4) return null;
                    for (int i = 0, o = 0; i < n; i++, o += 4)
                    {
                        int k = planes[3][i];
                        rgba[o]     = (byte)(planes[0][i] * k / 255);
                        rgba[o + 1] = (byte)(planes[1][i] * k / 255);
                        rgba[o + 2] = (byte)(planes[2][i] * k / 255);
                        rgba[o + 3] = 255;
                    }
                    return rgba;

                default: // Lab 等 → L (1ch 目) をグレースケール近似で表示
                    if (channels < 1) return null;
                    Debug.LogWarning($"[PSDParser] カラーモード {PSDHeaderReader.ColorModeName(psd.ColorMode)} は輝度のみの近似表示です");
                    for (int i = 0, o = 0; i < n; i++, o += 4)
                    {
                        byte v = planes[0][i];
                        rgba[o] = v; rgba[o + 1] = v; rgba[o + 2] = v; rgba[o + 3] = 255;
                    }
                    return rgba;
            }
        }
    }
}
