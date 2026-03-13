using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;

namespace PSDSimpleEditor
{
    /// <summary>
    /// Adobe PSD (version 1) ファイルを独自実装のバイナリパーサーで解析する。
    /// 外部ライブラリ不使用。Big-endian 読み取りは BigEndianBinaryReader が担当。
    /// </summary>
    public static class PSDParser
    {
        // ────────────────────────────────────────────────────────────────
        //  Public API
        // ────────────────────────────────────────────────────────────────

        public static PSDFile Parse(string filePath)
        {
            var psd = new PSDFile();

            using (var fs     = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            using (var reader = new BigEndianBinaryReader(fs))
            {
                ParseFileHeader(reader, psd);

                // ── セクション 2: カラーモードデータ（スキップ） ──
                int colorModeLen = reader.ReadInt32();
                reader.BaseStream.Seek(colorModeLen, SeekOrigin.Current);

                // ── セクション 3: イメージリソース（スキップ） ──
                int imageResourceLen = reader.ReadInt32();
                reader.BaseStream.Seek(imageResourceLen, SeekOrigin.Current);

                // ── セクション 4: レイヤーとマスク情報 ──
                ParseLayerAndMaskInfo(reader, psd);
            }

            return psd;
        }

        // ────────────────────────────────────────────────────────────────
        //  セクション 1: ファイルヘッダ
        // ────────────────────────────────────────────────────────────────

        static void ParseFileHeader(BigEndianBinaryReader r, PSDFile psd)
        {
            string sig = r.ReadString(4);
            if (sig != "8BPS")
                throw new Exception("PSD ファイルのシグネチャが不正です (8BPS ではありません)");

            psd.Version = r.ReadUInt16();
            if (psd.Version != 1)
                throw new Exception($"未対応の PSD バージョン: {psd.Version}。バージョン 1 (PSD) のみ対応しています。");

            r.ReadBytes(6); // 予約済み

            psd.Channels  = r.ReadUInt16();
            psd.Height    = r.ReadInt32();
            psd.Width     = r.ReadInt32();
            psd.BitDepth  = r.ReadUInt16();
            psd.ColorMode = r.ReadUInt16();

            if (psd.BitDepth != 8)
                Debug.LogWarning($"[PSDParser] ビット深度 {psd.BitDepth} bit は未検証です。8 bit のみ正式サポートです。");
        }

        // ────────────────────────────────────────────────────────────────
        //  セクション 4: レイヤーとマスク情報
        // ────────────────────────────────────────────────────────────────

        static void ParseLayerAndMaskInfo(BigEndianBinaryReader r, PSDFile psd)
        {
            int sectionLen = r.ReadInt32();
            if (sectionLen == 0) return;

            long sectionEnd = r.BaseStream.Position + sectionLen;

            // ── レイヤー情報サブセクション ──
            int layerInfoLen = r.ReadInt32();
            if (layerInfoLen == 0)
            {
                r.BaseStream.Seek(sectionEnd, SeekOrigin.Begin);
                return;
            }

            long layerInfoEnd = r.BaseStream.Position + layerInfoLen;

            // レイヤー数 (負値 = 先頭アルファが合成済み透明度データ)
            short rawCount = r.ReadInt16();
            int   count    = Math.Abs(rawCount);

            // ── フェーズ 2: 全レイヤーのメタデータ解析 ──
            var layers = new List<PSDLayer>(count);
            for (int i = 0; i < count; i++)
                layers.Add(ParseLayerRecord(r));

            // ── フェーズ 3: 全レイヤーのピクセルデータ読み取り ──
            foreach (var layer in layers)
                ReadLayerChannelData(r, layer);

            // ── Texture2D の構築 ──
            foreach (var layer in layers)
                BuildTexture(layer);

            // PSD ではレイヤー順序が「一番上→一番下」なので反転して
            // 合成時に下から順に処理できるようにする
            layers.Reverse();
            psd.Layers = layers;

            r.BaseStream.Seek(sectionEnd, SeekOrigin.Begin);
        }

        // ────────────────────────────────────────────────────────────────
        //  レイヤーレコード 1 件のメタデータ解析
        // ────────────────────────────────────────────────────────────────

        static PSDLayer ParseLayerRecord(BigEndianBinaryReader r)
        {
            var layer = new PSDLayer();

            // 矩形座標 (top, left, bottom, right)
            layer.Top    = r.ReadInt32();
            layer.Left   = r.ReadInt32();
            layer.Bottom = r.ReadInt32();
            layer.Right  = r.ReadInt32();

            // チャンネル情報
            short channelCount = r.ReadInt16();
            for (int i = 0; i < channelCount; i++)
            {
                layer.Channels.Add(new ChannelInfo
                {
                    ChannelId  = r.ReadInt16(),
                    DataLength = r.ReadInt32()
                });
            }

            // ブレンドモード
            string blendSig = r.ReadString(4); // "8BIM"
            string blendKey = r.ReadString(4);
            layer.BlendMode = ParseBlendMode(blendKey);

            // 不透明度・クリッピング・フラグ
            layer.Opacity = r.ReadByte();
            r.ReadByte(); // clipping
            byte flags = r.ReadByte();
            layer.IsVisible = (flags & 2) == 0; // bit1 が 1 なら非表示
            r.ReadByte();                        // filler

            // UI 状態を PSD 値で初期化
            layer.UIVisible = layer.IsVisible;
            layer.UIOpacity = layer.Opacity / 255f;

            // ── 追加データ (Extra data) ──
            int  extraLen = r.ReadInt32();
            long extraEnd = r.BaseStream.Position + extraLen;

            // レイヤーマスクデータ
            int maskLen = r.ReadInt32();
            r.BaseStream.Seek(maskLen, SeekOrigin.Current);

            // レイヤーブレンド範囲データ
            int blendRangeLen = r.ReadInt32();
            r.BaseStream.Seek(blendRangeLen, SeekOrigin.Current);

            // レイヤー名 (Pascal string)
            layer.Name = r.ReadPascalString();

            // 追加レイヤー情報 (調整レイヤーのパラメータ等)
            ParseAdditionalLayerInfo(r, layer, extraEnd);

            r.BaseStream.Seek(extraEnd, SeekOrigin.Begin);
            return layer;
        }

        // ────────────────────────────────────────────────────────────────
        //  追加レイヤー情報 (brit / hue2 / SoCo など)
        // ────────────────────────────────────────────────────────────────

        static void ParseAdditionalLayerInfo(BigEndianBinaryReader r, PSDLayer layer, long extraEnd)
        {
            // ヘッダ最小サイズ: sig(4) + key(4) + len(4) = 12 bytes
            while (r.BaseStream.Position <= extraEnd - 12)
            {
                long blockStart = r.BaseStream.Position;

                string sig;
                try { sig = r.ReadString(4); }
                catch { break; }

                if (sig != "8BIM" && sig != "8B64")
                {
                    // 不正な位置にある場合は 1 バイト戻って再試行
                    r.BaseStream.Seek(blockStart + 1, SeekOrigin.Begin);
                    continue;
                }

                string key;
                try { key = r.ReadString(4); }
                catch { break; }

                int  dataLen = r.ReadInt32();
                long dataEnd = r.BaseStream.Position + dataLen;

                try
                {
                    switch (key)
                    {
                        // ── 明るさ / コントラスト ──
                        case "brit":
                            if (dataLen >= 4)
                            {
                                layer.Adjustment.HasBrightnessContrast = true;
                                layer.Adjustment.Brightness = r.ReadInt16();
                                layer.Adjustment.Contrast   = r.ReadInt16();
                                layer.UIBrightness = layer.Adjustment.Brightness;
                                layer.UIContrast   = layer.Adjustment.Contrast;
                            }
                            break;

                        // ── 色相 / 彩度 / 明度 ──
                        case "hue2":
                            if (dataLen >= 12)
                            {
                                layer.Adjustment.HasHueSaturation = true;
                                r.ReadUInt16(); // version
                                r.ReadUInt16(); // enable
                                r.ReadUInt16(); // colorization
                                layer.Adjustment.Hue        = r.ReadInt16();
                                layer.Adjustment.Saturation = r.ReadInt16();
                                layer.Adjustment.Lightness  = r.ReadInt16();
                                layer.UIHue        = layer.Adjustment.Hue;
                                layer.UISaturation = layer.Adjustment.Saturation;
                                layer.UILightness  = layer.Adjustment.Lightness;
                            }
                            break;

                        // ── ベタ塗り (SoCo) ──
                        case "SoCo":
                            layer.Adjustment.HasSolidColor = true;
                            // OSType descriptor の完全パースは省略
                            break;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[PSDParser] 追加レイヤー情報 '{key}' の解析に失敗: {e.Message}");
                }

                r.BaseStream.Seek(dataEnd, SeekOrigin.Begin);
            }
        }

        // ────────────────────────────────────────────────────────────────
        //  チャンネル画像データの読み取り
        // ────────────────────────────────────────────────────────────────

        static void ReadLayerChannelData(BigEndianBinaryReader r, PSDLayer layer)
        {
            int pixelCount = layer.Width * layer.Height;

            byte[] chR = new byte[Math.Max(pixelCount, 1)];
            byte[] chG = new byte[Math.Max(pixelCount, 1)];
            byte[] chB = new byte[Math.Max(pixelCount, 1)];
            byte[] chA = new byte[Math.Max(pixelCount, 1)];
            // アルファをデフォルト完全不透明に設定
            for (int i = 0; i < chA.Length; i++) chA[i] = 255;

            foreach (var ch in layer.Channels)
            {
                long channelStart = r.BaseStream.Position;
                long channelEnd   = channelStart + ch.DataLength;

                ushort compression = r.ReadUInt16();

                if (pixelCount > 0)
                {
                    byte[] channelData = null;
                    try
                    {
                        switch (compression)
                        {
                            case 0: // Raw (非圧縮)
                                channelData = r.ReadBytes(layer.Width * layer.Height);
                                break;
                            case 1: // RLE PackBits
                                channelData = DecompressPackBits(r, layer.Width, layer.Height);
                                break;
                            default:
                                Debug.LogWarning(
                                    $"[PSDParser] レイヤー '{layer.Name}' チャンネル {ch.ChannelId}: " +
                                    $"圧縮形式 {compression} は未対応です (ZIP等)。");
                                break;
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning(
                            $"[PSDParser] レイヤー '{layer.Name}' チャンネル {ch.ChannelId} の読み取り失敗: {e.Message}");
                    }

                    if (channelData != null)
                    {
                        int copyLen = Math.Min(channelData.Length, pixelCount);
                        switch (ch.ChannelId)
                        {
                            case  0: Array.Copy(channelData, chR, copyLen); break;
                            case  1: Array.Copy(channelData, chG, copyLen); break;
                            case  2: Array.Copy(channelData, chB, copyLen); break;
                            case -1: Array.Copy(channelData, chA, copyLen); break;
                        }
                    }
                }

                // チャンネルデータの末尾に正確に移動（エラー安全）
                r.BaseStream.Seek(channelEnd, SeekOrigin.Begin);
            }

            if (pixelCount <= 0) return;

            // RGBA パック
            layer._rawPixels = new byte[pixelCount * 4];
            for (int i = 0; i < pixelCount; i++)
            {
                layer._rawPixels[i * 4 + 0] = chR[i];
                layer._rawPixels[i * 4 + 1] = chG[i];
                layer._rawPixels[i * 4 + 2] = chB[i];
                layer._rawPixels[i * 4 + 3] = chA[i];
            }
        }

        // ────────────────────────────────────────────────────────────────
        //  PackBits (RLE) 解凍
        // ────────────────────────────────────────────────────────────────

        static byte[] DecompressPackBits(BigEndianBinaryReader r, int width, int height)
        {
            // 各スキャンラインのバイト数テーブル
            ushort[] rowByteCounts = new ushort[height];
            for (int i = 0; i < height; i++)
                rowByteCounts[i] = r.ReadUInt16();

            byte[] result = new byte[width * height];
            int    dest   = 0;

            for (int row = 0; row < height; row++)
            {
                byte[] rowData = r.ReadBytes(rowByteCounts[row]);
                int    src     = 0;

                while (src < rowData.Length && dest < result.Length)
                {
                    sbyte header = (sbyte)rowData[src++];

                    if (header == -128)
                    {
                        // No-op
                    }
                    else if (header >= 0)
                    {
                        // リテラルラン: (header+1) バイトをそのままコピー
                        int copyCount = header + 1;
                        for (int k = 0; k < copyCount && src < rowData.Length && dest < result.Length; k++)
                            result[dest++] = rowData[src++];
                    }
                    else
                    {
                        // 繰り返しラン: 次の 1 バイトを (-header+1) 回コピー
                        int  repeatCount = -header + 1;
                        byte val         = rowData[src++];
                        for (int k = 0; k < repeatCount && dest < result.Length; k++)
                            result[dest++] = val;
                    }
                }
            }

            return result;
        }

        // ────────────────────────────────────────────────────────────────
        //  Texture2D の構築
        // ────────────────────────────────────────────────────────────────

        static void BuildTexture(PSDLayer layer)
        {
            if (layer._rawPixels == null || layer.Width <= 0 || layer.Height <= 0)
                return;

            var tex = new Texture2D(layer.Width, layer.Height, TextureFormat.RGBA32, false)
            {
                name = layer.Name
            };

            // PSD は左上原点、Unity Texture2D は左下原点 → 垂直反転
            int    stride  = layer.Width * 4;
            byte[] flipped = new byte[layer._rawPixels.Length];
            for (int y = 0; y < layer.Height; y++)
            {
                int srcRow = layer.Height - 1 - y;
                Buffer.BlockCopy(layer._rawPixels, srcRow * stride, flipped, y * stride, stride);
            }

            tex.LoadRawTextureData(flipped);
            tex.Apply();

            layer.Texture    = tex;
            layer._rawPixels = null; // メモリ解放
        }

        // ────────────────────────────────────────────────────────────────
        //  ブレンドモードキーのマッピング
        // ────────────────────────────────────────────────────────────────

        static BlendMode ParseBlendMode(string key)
        {
            switch (key)
            {
                case "norm": return BlendMode.Normal;
                case "mul ": return BlendMode.Multiply; // 末尾スペース込み4文字
                case "scrn": return BlendMode.Screen;
                case "over": return BlendMode.Overlay;
                default:
                    Debug.LogWarning($"[PSDParser] 未対応のブレンドモード: '{key}'");
                    return BlendMode.Unknown;
            }
        }
    }
}
