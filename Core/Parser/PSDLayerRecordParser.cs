using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace PSDSimpleEditor
{
    // ════════════════════════════════════════════════════════════════
    //  Section 4: レイヤーとマスク情報 / レイヤーレコード
    // ════════════════════════════════════════════════════════════════
    internal static class PSDLayerRecordParser
    {
        /// <summary>
        /// Section 4 全体を読む。戻り値はファイル格納順 (最下層→最上層) のフラットなレイヤーリスト。
        /// どんな失敗があっても最後に必ず sectionEnd へ seek する。
        /// fileData はパース対象ファイル全体のバッファ (null 可)。非 null のとき
        /// チャンネル画像データの解凍をレイヤー単位で並列実行する。
        /// </summary>
        internal static List<PSDLayer> ParseLayerAndMaskSection(BigEndianBinaryReader r, PSDFile psd, byte[] fileData, StringBuilder vlog)
        {
            var flat = new List<PSDLayer>();

            uint sectionLen = r.ReadUInt32();
            long sectionEnd = r.Position + sectionLen;
            try
            {
                if (sectionLen == 0) return flat;                     // レイヤーなし
                if (!PSDHeaderReader.IsLayerSupportedColorMode(psd.ColorMode)) return flat; // CMYK/LAB 等はマージ画像のみ

                // ── レイヤー情報ブロック ──
                uint layerInfoLen = r.ReadUInt32();
                long layerInfoEnd = r.Position + layerInfoLen;
                try
                {
                    if (layerInfoLen > 0)
                        flat = ParseLayerInfo(r, psd, layerInfoEnd, fileData, vlog);
                }
                finally { r.Seek(layerInfoEnd); }

                // ── グローバルレイヤーマスク情報 (長さを読んでスキップ) ──
                if (r.Position + 4 <= sectionEnd)
                {
                    uint glmLen = r.ReadUInt32();
                    long glmEnd = r.Position + glmLen;
                    if (glmEnd > sectionEnd) glmEnd = sectionEnd;
                    r.Seek(glmEnd);
                }

                // ── セクションレベルの追加情報 (16bit PSD のレイヤーは Lr16 内に格納される) ──
                while (r.Position + 12 <= sectionEnd)
                {
                    string sig = r.ReadAscii(4);
                    if (sig != "8BIM" && sig != "8B64") break;
                    string key = r.ReadAscii(4);
                    uint   len = r.ReadUInt32();
                    long blockStart = r.Position;
                    long dataEnd    = blockStart + len;
                    if (dataEnd > sectionEnd) dataEnd = sectionEnd;
                    try
                    {
                        if ((key == "Lr16" || key == "Lr32") && flat.Count == 0)
                        {
                            vlog?.AppendLine($"  セクション追加情報 '{key}' からレイヤーを読み取ります");
                            flat = ParseLayerInfo(r, psd, dataEnd, fileData, vlog);
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[PSDParser] セクション追加情報 '{key}' の解析に失敗: {e.Message}");
                    }
                    finally { r.Seek(dataEnd); }

                    // 次ブロックのパディング量はライターにより 0〜3 バイトとブレがあるため、
                    // シグネチャが現れる位置を 0..3 バイトの範囲で探して同期する。
                    if (!TryAlignToNextSignature(r, dataEnd, sectionEnd)) break;
                }
            }
            finally { r.Seek(sectionEnd); }

            return flat;
        }

        /// <summary>
        /// basePos から 0..3 バイトのパディングを許容して次の 8BIM/8B64 シグネチャ位置へ同期する。
        /// 見つかればその位置に seek して true、それ以外は false。
        /// </summary>
        static bool TryAlignToNextSignature(BigEndianBinaryReader r, long basePos, long limit)
        {
            for (int pad = 0; pad < 4; pad++)
            {
                long cand = basePos + pad;
                if (cand + 12 > limit) return false;
                r.Seek(cand);
                string sig = r.ReadAscii(4);
                if (sig == "8BIM" || sig == "8B64")
                {
                    r.Seek(cand); // シグネチャ先頭へ戻す (読み直しはループ側で行う)
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// レイヤー情報 (count + レイヤーレコード群 + チャンネル画像データ) を読む。
        /// レコード解析の途中でストリーム同期を失った場合はレイヤーを破棄して空リストを返す
        /// (境界 seek は呼び出し側が行うため、マージ画像の表示は維持される)。
        /// </summary>
        static List<PSDLayer> ParseLayerInfo(BigEndianBinaryReader r, PSDFile psd, long infoEnd, byte[] fileData, StringBuilder vlog)
        {
            var flat = new List<PSDLayer>();
            if (r.Position + 2 > infoEnd) return flat;

            try
            {
                short rawCount = r.ReadInt16();
                int   count    = Math.Abs((int)rawCount); // 負値 = 最下層が透明 α を持つ印 → abs
                if (count == 0) return flat;
                if (count > 10000)
                    throw new IOException($"レイヤー数が不正です: {count}");

                vlog?.AppendLine($"  レイヤー数: {count} (raw={rawCount})");

                // ── レイヤーレコード (ファイル格納順 = 最下層 → 最上層) ──
                for (int i = 0; i < count; i++)
                    flat.Add(ParseLayerRecord(r, i, vlog));

                // ── チャンネル画像データ (レイヤー順 × チャンネル順に連続格納) ──
                if (fileData != null)
                    ReadAllLayerChannelsParallel(r, psd, flat, fileData);
                else
                    foreach (var layer in flat)
                        PSDChannelDecoder.ReadLayerChannels(r, psd, layer);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PSDParser] レイヤー情報の解析に失敗したためレイヤー表示を無効化します: {e.Message}");
                flat.Clear();
            }
            return flat;
        }

        /// <summary>
        /// 全レイヤーのチャンネル画像データを並列に解凍する。
        /// 各レイヤーのチャンネルブロックはファイル内に連続格納されており長さが既知のため、
        /// 開始オフセットを積算してレイヤーごとに独立したリーダー (同一バッファ上の
        /// MemoryStream) で読む。解凍 (RLE/ZIP) と RGBA 組み立てが CPU 時間の大半を占めるので
        /// レイヤー数分の並列化がそのまま効く。書き込み先は各ワーカー自身のレイヤーのみ。
        /// </summary>
        static void ReadAllLayerChannelsParallel(BigEndianBinaryReader r, PSDFile psd, List<PSDLayer> flat, byte[] fileData)
        {
            var offsets = new long[flat.Count];
            long pos = r.Position;
            for (int i = 0; i < flat.Count; i++)
            {
                offsets[i] = pos;
                foreach (var ch in flat[i].Channels)
                    pos += (uint)ch.DataLength;
            }
            r.Seek(pos); // メインリーダーはチャンネルデータ末尾へ (以降のセクション読みを維持)

            Parallel.For(0, flat.Count, i =>
            {
                try
                {
                    using (var ms = new MemoryStream(fileData, false))
                    using (var lr = new BigEndianBinaryReader(ms))
                    {
                        lr.Seek(offsets[i]);
                        PSDChannelDecoder.ReadLayerChannels(lr, psd, flat[i]);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[PSDParser] レイヤー '{flat[i].Name}' のチャンネルデータ読み取りに失敗 (スキップ): {e.Message}");
                }
            });
        }

        // ────────────────────────────────────────────────────────────────
        //  レイヤーレコード
        // ────────────────────────────────────────────────────────────────

        static PSDLayer ParseLayerRecord(BigEndianBinaryReader r, int index, StringBuilder vlog)
        {
            var layer = new PSDLayer();

            // 矩形 (top, left, bottom, right)
            layer.Top    = r.ReadInt32();
            layer.Left   = r.ReadInt32();
            layer.Bottom = r.ReadInt32();
            layer.Right  = r.ReadInt32();

            // チャンネルリスト
            int channelCount = r.ReadUInt16();
            if (channelCount > 64)
                throw new IOException($"チャンネル数が不正です ({channelCount})。ストリーム破損の可能性があります。");
            for (int c = 0; c < channelCount; c++)
            {
                var ch = new ChannelInfo
                {
                    ChannelId  = r.ReadInt16(),
                    DataLength = (int)r.ReadUInt32(),
                };
                layer.Channels.Add(ch);
            }

            // ブレンドモードシグネチャ + キー
            string sig = r.ReadAscii(4);
            if (sig != "8BIM")
                throw new IOException($"レイヤーレコードのシグネチャが不正です ('{sig}')。ストリーム破損の可能性があります。");
            layer.BlendKeyRaw = r.ReadAscii(4);
            layer.BlendMode   = PSDBlendModeConverter.BlendModeFromKey(layer.BlendKeyRaw);

            // opacity / clipping / flags / filler
            layer.Opacity    = r.ReadByte();
            layer.IsClipping = r.ReadByte() != 0;
            byte flags       = r.ReadByte();
            layer.IsVisible  = (flags & 0x02) == 0; // bit1 が立っていたら非表示
            r.ReadByte(); // filler

            // ── エクストラデータ (マスク / ブレンディングレンジ / 名前 / 追加情報) ──
            uint extraLen = r.ReadUInt32();
            long extraEnd = r.Position + extraLen;
            try
            {
                ParseLayerExtra(r, layer, extraEnd);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PSDParser] レイヤー '{layer.Name}' の付加情報の解析に失敗 (スキップして続行): {e.Message}");
            }
            finally { r.Seek(extraEnd); } // レコード末尾で必ず境界 seek

            PSDParser.DumpLayer(vlog, index, layer);
            return layer;
        }

        static void ParseLayerExtra(BigEndianBinaryReader r, PSDLayer layer, long extraEnd)
        {
            // ── マスクデータ (len 0 / 20 / 36 ...) ──
            uint maskLen = r.ReadUInt32();
            long maskEnd = r.Position + maskLen;
            if (maskLen >= 20)
            {
                layer.MaskTop    = r.ReadInt32();
                layer.MaskLeft   = r.ReadInt32();
                layer.MaskBottom = r.ReadInt32();
                layer.MaskRight  = r.ReadInt32();
                layer.MaskDefaultColor = r.ReadByte();
                byte mflags = r.ReadByte();
                if ((mflags & 0x01) != 0)
                {
                    // bit0 = マスク位置がレイヤー相対 → レイヤー座標を加算して絶対座標へ変換
                    layer.MaskTop    += layer.Top;
                    layer.MaskBottom += layer.Top;
                    layer.MaskLeft   += layer.Left;
                    layer.MaskRight  += layer.Left;
                }
                layer.MaskIsDisabled = (mflags & 0x02) != 0;
                layer.HasMask = true;
            }
            r.Seek(maskEnd); // 36 バイト形式 (real rect 等) の残りはスキップ

            // ── ブレンディングレンジ ──
            uint rangeLen = r.ReadUInt32();
            r.Skip(rangeLen);

            // ── Pascal 名 ((1+len) を 4 の倍数にパディング)。luni があれば後で上書き ──
            layer.Name = r.ReadPascalString(4);

            // ── 追加情報 (Additional Layer Information) ループ ──
            while (r.Position + 12 <= extraEnd)
            {
                string sig = r.ReadAscii(4);
                if (sig != "8BIM" && sig != "8B64")
                    break; // 不明データ → ループを抜ける (finally で extraEnd へ seek される)

                string key = r.ReadAscii(4);
                uint   len = r.ReadUInt32();
                long dataEnd = r.Position + len + (len & 1); // 奇数長は +1 パディング
                if (dataEnd > extraEnd) dataEnd = extraEnd;
                try
                {
                    PSDAdditionalInfoParser.HandleAdditionalInfo(r, layer, key, len);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[PSDParser] レイヤー '{layer.Name}' の追加情報 '{key}' の解析に失敗 (この機能は無効化): {e.Message}");
                }
                finally { r.Seek(dataEnd); } // ブロック末尾で必ず境界 seek
            }
        }
    }
}
