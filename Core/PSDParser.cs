using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;

namespace PSDSimpleEditor
{
    /// <summary>
    /// PSD (version 1) バイナリ → PSDFile 変換パーサー。
    ///
    /// 設計原則 (REWRITE_SPEC.md §0 / §5):
    ///   - すべての可変長ブロックは「end = pos + len を先に計算 → 処理 → 必ず Seek(end)」で
    ///     境界管理する。ブロック内のパース失敗が後続データのストリーム位置を壊さない。
    ///   - 致命的エラー (シグネチャ不正 / version!=1 / depth==32) のみ例外。
    ///     レイヤー単位・ブロック単位の失敗は Debug.LogWarning + スキップで続行。
    ///
    /// 対応機能:
    ///   - 8bit / 16bit (16bit は上位バイト採用で 8bit 化。Lr16 ブロック対応)
    ///   - RGB / Grayscale (R 複製)。CMYK / LAB は警告 + マージ画像のみ
    ///   - 圧縮: Raw / RLE (PackBits) / ZIP / ZIP+prediction
    ///   - luni (UTF-16BE レイヤー名・日本語対応) / lsct・lsdk (グループ) / クリッピング
    ///   - レイヤーマスク (相対座標 → 絶対座標変換、id==-2 はマスク矩形で読む)
    ///   - 調整: brit / CgEd / hue2 / SoCo (最小ディスクリプタパーサー使用)
    ///   - lfx2 / lrFX の Color Overlay (best effort)
    /// </summary>
    public static class PSDParser
    {
        // ════════════════════════════════════════════════════════════════
        //  公開 API
        // ════════════════════════════════════════════════════════════════

        /// <summary>true にすると Parse(path) でも詳細ダンプログを出力する。</summary>
        public static bool VerboseLog = false;

        /// <summary>PSD ファイルをパースする (既存呼び出し互換)。</summary>
        public static PSDFile Parse(string filePath)
        {
            return Parse(filePath, VerboseLog);
        }

        /// <summary>PSD ファイルをパースする。verbose=true でレイヤー構造のダンプログを出力。</summary>
        public static PSDFile Parse(string filePath, bool verbose)
        {
            // 直接アクセスが失敗した場合はダブルクォーテーションを無視して再アクセス
            if (!string.IsNullOrEmpty(filePath) && !File.Exists(filePath))
            {
                if (filePath.StartsWith("\"") || filePath.EndsWith("\""))
                {
                    string trimmed = filePath.Trim('"');
                    if (File.Exists(trimmed))
                    {
                        filePath = trimmed;
                    }
                }
            }

            var psd  = new PSDFile();
            var vlog = verbose ? new StringBuilder() : null;
            vlog?.AppendLine($"[PSDParser] ダンプ: {filePath}");

            List<PSDLayer> flat;
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            using (var r  = new BigEndianBinaryReader(fs))
            {
                // Section 1: ヘッダ (致命的エラーはここで throw)
                ParseHeader(r, psd, vlog);

                // Section 2: カラーモードデータ / Section 3: イメージリソース → 長さを読んでスキップ
                SkipLengthPrefixedBlock(r);
                SkipLengthPrefixedBlock(r);

                // Section 4: レイヤーとマスク情報
                flat = ParseLayerAndMaskSection(r, psd, vlog);

                // Section 5: マージ済み画像 (失敗しても警告のみで続行)
                ParseMergedImage(r, psd, vlog);
            }

            // テクスチャ構築 (構築後 _rawPixels は null 化)
            BuildLayerTextures(flat);

            // グループツリー構築 (index 0 = 最下層)
            psd.Layers = BuildLayerTree(flat, vlog);

            // UI 初期値の設定 (パーサーの責務)
            InitUIState(psd.Layers);

            if (vlog != null) Debug.Log(vlog.ToString());
            return psd;
        }

        // ════════════════════════════════════════════════════════════════
        //  Section 1: ヘッダ
        // ════════════════════════════════════════════════════════════════

        static void ParseHeader(BigEndianBinaryReader r, PSDFile psd, StringBuilder vlog)
        {
            string sig = r.ReadAscii(4);
            if (sig != "8BPS")
                throw new IOException("PSD ファイルではありません (シグネチャが 8BPS ではありません)");

            psd.Version = r.ReadUInt16();
            if (psd.Version == 2)
                throw new IOException("PSB (Large Document, version 2) 形式は非対応です。PSD 形式で保存し直してください。");
            if (psd.Version != 1)
                throw new IOException($"未対応の PSD バージョンです: {psd.Version}");

            r.Skip(6); // 予約領域

            psd.Channels = r.ReadUInt16();
            psd.Height   = (int)r.ReadUInt32();
            psd.Width    = (int)r.ReadUInt32();
            psd.BitDepth = r.ReadUInt16();

            if (psd.BitDepth == 32)
                throw new IOException("32bit/チャンネルの PSD は非対応です。8bit または 16bit で保存し直してください。");
            if (psd.BitDepth != 8 && psd.BitDepth != 16)
                throw new IOException($"未対応のビット深度です: {psd.BitDepth}bit");

            psd.ColorMode = r.ReadUInt16();

            if (psd.Width <= 0 || psd.Height <= 0 || psd.Width > 30000 || psd.Height > 30000)
                throw new IOException($"画像サイズが不正です: {psd.Width}x{psd.Height}");

            if (!IsLayerSupportedColorMode(psd.ColorMode))
                Debug.LogWarning($"[PSDParser] カラーモード {ColorModeName(psd.ColorMode)} は限定対応です。マージ済み画像のみ表示します。");

            vlog?.AppendLine($"  ヘッダ: {psd.Width}x{psd.Height} {psd.BitDepth}bit ch={psd.Channels} mode={ColorModeName(psd.ColorMode)}");
        }

        static bool IsLayerSupportedColorMode(ushort mode) => mode == 3 /*RGB*/ || mode == 1 /*Grayscale*/;

        static string ColorModeName(ushort mode)
        {
            switch (mode)
            {
                case 0: return "Bitmap";
                case 1: return "Grayscale";
                case 2: return "Indexed";
                case 3: return "RGB";
                case 4: return "CMYK";
                case 7: return "Multichannel";
                case 8: return "Duotone";
                case 9: return "Lab";
                default: return $"Unknown({mode})";
            }
        }

        // ════════════════════════════════════════════════════════════════
        //  Section 2 / 3: 長さプレフィックス付きブロックのスキップ
        // ════════════════════════════════════════════════════════════════

        static void SkipLengthPrefixedBlock(BigEndianBinaryReader r)
        {
            uint len = r.ReadUInt32();
            r.Skip(len);
        }

        // ════════════════════════════════════════════════════════════════
        //  Section 4: レイヤーとマスク情報
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Section 4 全体を読む。戻り値はファイル格納順 (最下層→最上層) のフラットなレイヤーリスト。
        /// どんな失敗があっても最後に必ず sectionEnd へ seek する。
        /// </summary>
        static List<PSDLayer> ParseLayerAndMaskSection(BigEndianBinaryReader r, PSDFile psd, StringBuilder vlog)
        {
            var flat = new List<PSDLayer>();

            uint sectionLen = r.ReadUInt32();
            long sectionEnd = r.Position + sectionLen;
            try
            {
                if (sectionLen == 0) return flat;                     // レイヤーなし
                if (!IsLayerSupportedColorMode(psd.ColorMode)) return flat; // CMYK/LAB 等はマージ画像のみ

                // ── レイヤー情報ブロック ──
                uint layerInfoLen = r.ReadUInt32();
                long layerInfoEnd = r.Position + layerInfoLen;
                try
                {
                    if (layerInfoLen > 0)
                        flat = ParseLayerInfo(r, psd, layerInfoEnd, vlog);
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
                            flat = ParseLayerInfo(r, psd, dataEnd, vlog);
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
        static List<PSDLayer> ParseLayerInfo(BigEndianBinaryReader r, PSDFile psd, long infoEnd, StringBuilder vlog)
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
                foreach (var layer in flat)
                    ReadLayerChannels(r, psd, layer);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PSDParser] レイヤー情報の解析に失敗したためレイヤー表示を無効化します: {e.Message}");
                flat.Clear();
            }
            return flat;
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
            layer.BlendMode   = BlendModeFromKey(layer.BlendKeyRaw);

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

            DumpLayer(vlog, index, layer);
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
                    HandleAdditionalInfo(r, layer, key, len);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[PSDParser] レイヤー '{layer.Name}' の追加情報 '{key}' の解析に失敗 (この機能は無効化): {e.Message}");
                }
                finally { r.Seek(dataEnd); } // ブロック末尾で必ず境界 seek
            }
        }

        // ────────────────────────────────────────────────────────────────
        //  追加情報 (Additional Layer Information) ハンドラ
        // ────────────────────────────────────────────────────────────────

        static void HandleAdditionalInfo(BigEndianBinaryReader r, PSDLayer layer, string key, uint len)
        {
            switch (key)
            {
                case "luni": // Unicode レイヤー名 (UTF-16BE)。Pascal 名より優先 (日本語名対応)
                {
                    string name = r.ReadUnicodeString();
                    if (name.Length > 0) layer.Name = name;
                    break;
                }
                case "lsct": // セクションディバイダ (グループ)
                case "lsdk": // ネストセクションディバイダ (深いネストで使用される亜種)
                    HandleSectionDivider(r, layer, len);
                    break;

                case "brit": // 明るさ・コントラスト (旧形式)
                    layer.Adjustment.HasBrightnessContrast = true;
                    layer.Adjustment.Brightness = r.ReadInt16();
                    layer.Adjustment.Contrast   = r.ReadInt16();
                    break;

                case "CgEd": // 明るさ・コントラスト (新形式ディスクリプタ)。brit を上書き
                    HandleCgEd(r, layer);
                    break;

                case "hue2": // 色相・彩度
                    HandleHue2(r, layer, len);
                    break;

                case "SoCo": // べた塗りレイヤー
                    HandleSoCo(r, layer);
                    break;

                case "lfx2": // レイヤーエフェクト (ディスクリプタ形式) → Color Overlay のみ best effort
                    HandleLfx2(r, layer);
                    break;

                case "lrFX": // レイヤーエフェクト (旧形式) → Color Overlay のみ best effort
                    HandleLrFX(r, layer, len);
                    break;

                default:
                    // その他のキーは読まずにスキップ (境界 seek は呼び出し側)
                    break;
            }
        }

        static void HandleSectionDivider(BigEndianBinaryReader r, PSDLayer layer, uint len)
        {
            uint type = r.ReadUInt32();
            switch (type)
            {
                case 1: layer.SectionType = LayerSectionType.GroupBegin; layer.IsExpanded = true;  break; // 開いたフォルダ
                case 2: layer.SectionType = LayerSectionType.GroupBegin; layer.IsExpanded = false; break; // 閉じたフォルダ
                case 3: layer.SectionType = LayerSectionType.GroupEnd;   break; // 終端マーカー (ツリーに含めない)
                default: layer.SectionType = LayerSectionType.Normal;    break;
            }
            if (len >= 12)
            {
                string sig = r.ReadAscii(4);
                if (sig == "8BIM")
                {
                    var mode = BlendModeFromKey(r.ReadAscii(4));
                    layer.GroupBlendMode = mode;
                    if (layer.BlendMode == BlendMode.Unknown)
                        layer.BlendMode = mode; // レコード側キーが未知なら lsct 側を採用
                }
            }
        }

        static void HandleCgEd(BigEndianBinaryReader r, PSDLayer layer)
        {
            uint descVersion = r.ReadUInt32();
            if (descVersion != 16) return;
            var d = ParseDescriptor(r, 0);
            bool any = false;
            if (TryGetNumber(d, "Brghtnss", out double b)) { layer.Adjustment.Brightness = (float)b; any = true; }
            if (TryGetNumber(d, "Cntrst",   out double c)) { layer.Adjustment.Contrast   = (float)c; any = true; }
            if (any) layer.Adjustment.HasBrightnessContrast = true;
        }

        static void HandleHue2(BigEndianBinaryReader r, PSDLayer layer, uint len)
        {
            // 形式: version(2B) + colorization フラグ(1B) + pad(1B)
            //       + colorization 用 hue/sat/lightness (int16×3)
            //       + master 用 hue/sat/lightness (int16×3)
            r.ReadUInt16();                  // version
            byte colorization = r.ReadByte();
            r.ReadByte();                    // padding

            short h, s, l;
            if (len >= 16)
            {
                short ch = r.ReadInt16(), cs = r.ReadInt16(), cl = r.ReadInt16(); // colorization 値
                short mh = r.ReadInt16(), ms = r.ReadInt16(), ml = r.ReadInt16(); // master 値
                if (colorization != 0) { h = ch; s = cs; l = cl; }
                else                   { h = mh; s = ms; l = ml; }
            }
            else if (len >= 10)
            {
                h = r.ReadInt16(); s = r.ReadInt16(); l = r.ReadInt16();
            }
            else
            {
                return;
            }

            layer.Adjustment.HasHueSaturation = true;
            layer.Adjustment.Hue        = h;
            layer.Adjustment.Saturation = s;
            layer.Adjustment.Lightness  = l;
        }

        static void HandleSoCo(BigEndianBinaryReader r, PSDLayer layer)
        {
            uint descVersion = r.ReadUInt32();
            if (descVersion != 16) return;
            var d   = ParseDescriptor(r, 0);
            var clr = GetChildDescriptor(d, "Clr ");
            if (clr != null &&
                TryGetNumber(clr, "Rd  ", out double cr) &&
                TryGetNumber(clr, "Grn ", out double cg) &&
                TryGetNumber(clr, "Bl  ", out double cb))
            {
                layer.Adjustment.HasSolidColor = true;
                layer.Adjustment.SolidColor = new Color(
                    Mathf.Clamp01((float)(cr / 255.0)),
                    Mathf.Clamp01((float)(cg / 255.0)),
                    Mathf.Clamp01((float)(cb / 255.0)), 1f);
            }
            else
            {
                Debug.LogWarning($"[PSDParser] レイヤー '{layer.Name}' のべた塗りは RGB 以外のカラー形式のため未対応です。");
            }
        }

        static void HandleLfx2(BigEndianBinaryReader r, PSDLayer layer)
        {
            r.ReadUInt32();                  // オブジェクトバージョン (0)
            uint descVersion = r.ReadUInt32();
            if (descVersion != 16) return;
            var d = ParseDescriptor(r, 0);

            // エフェクト全体の有効スイッチ
            if (d.TryGetValue("masterFXSwitch", out object sw) && sw is bool master && !master) return;

            var sofi = GetChildDescriptor(d, "SoFi"); // Color Overlay (Solid Fill)
            if (sofi == null) return;
            if (sofi.TryGetValue("enab", out object en) && en is bool enabled && !enabled) return;

            var clr = GetChildDescriptor(sofi, "Clr ");
            if (clr == null) return;
            if (!TryGetNumber(clr, "Rd  ", out double cr) ||
                !TryGetNumber(clr, "Grn ", out double cg) ||
                !TryGetNumber(clr, "Bl  ", out double cb)) return;

            var fx = new LayerEffects
            {
                HasColorOverlay = true,
                OverlayColor = new Color(
                    Mathf.Clamp01((float)(cr / 255.0)),
                    Mathf.Clamp01((float)(cg / 255.0)),
                    Mathf.Clamp01((float)(cb / 255.0)), 1f),
            };
            if (TryGetNumber(sofi, "Opct", out double opct))
                fx.OverlayOpacity = Mathf.Clamp01((float)(opct / 100.0)); // UntF パーセント
            if (sofi.TryGetValue("Md  ", out object md) && md is string mode)
                fx.OverlayBlendMode = BlendModeFromEnumValue(mode);

            layer.Effects = fx;
        }

        static void HandleLrFX(BigEndianBinaryReader r, PSDLayer layer, uint len)
        {
            long blockEnd = r.Position + len;
            r.ReadUInt16();                  // version (0)
            ushort effectCount = r.ReadUInt16();

            for (int i = 0; i < effectCount; i++)
            {
                if (r.Position + 12 > blockEnd) break;
                string sig = r.ReadAscii(4);
                if (sig != "8BIM") break;
                string effectKey = r.ReadAscii(4);
                uint   effectLen = r.ReadUInt32();
                long effectEnd = r.Position + effectLen;
                if (effectEnd > blockEnd) effectEnd = blockEnd;

                if (effectKey == "sofi" && effectLen >= 26)
                {
                    // sofi: version(4) + ブレンドキー(4 または '8BIM'+4) + カラー(2+2×4) + opacity(1) + enabled(1) + ...
                    r.ReadUInt32(); // version
                    string b4   = r.ReadAscii(4);
                    string bkey = (b4 == "8BIM") ? r.ReadAscii(4) : b4;
                    ushort colorSpace = r.ReadUInt16();
                    ushort c0 = r.ReadUInt16(), c1 = r.ReadUInt16(), c2 = r.ReadUInt16();
                    r.ReadUInt16(); // 4 成分目 (RGB では未使用)
                    byte opacity = r.ReadByte();
                    byte enabled = r.ReadByte();
                    if (enabled != 0 && colorSpace == 0 /*RGB*/)
                    {
                        layer.Effects = new LayerEffects
                        {
                            HasColorOverlay  = true,
                            OverlayColor     = new Color((c0 >> 8) / 255f, (c1 >> 8) / 255f, (c2 >> 8) / 255f, 1f),
                            OverlayBlendMode = BlendModeFromKey(bkey),
                            OverlayOpacity   = opacity / 255f,
                        };
                    }
                }
                r.Seek(effectEnd);
            }
        }

        // ────────────────────────────────────────────────────────────────
        //  チャンネル画像データ
        // ────────────────────────────────────────────────────────────────

        static void ReadLayerChannels(BigEndianBinaryReader r, PSDFile psd, PSDLayer layer)
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

                    // 16bit → 8bit: BE 各ペアの先頭 (上位) バイトを採用
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
        static byte[] DecodeChannel(BigEndianBinaryReader r, ushort compression, int w, int h, int bytesPerSample, long payloadLen)
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
        static void DecodePackBitsRow(byte[] src, byte[] dst, int dstOffset, int rowBytes)
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

        /// <summary>16bit プレーン → 8bit プレーン (BE 各ペアの先頭 = 上位バイトを採用)。</summary>
        static byte[] Downsample16To8(byte[] plane16, int pixelCount)
        {
            var plane8 = new byte[pixelCount];
            int limit = Math.Min(pixelCount, plane16.Length / 2);
            for (int i = 0; i < limit; i++)
                plane8[i] = plane16[i * 2];
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

        // ────────────────────────────────────────────────────────────────
        //  テクスチャ構築
        // ────────────────────────────────────────────────────────────────

        static void BuildLayerTextures(List<PSDLayer> flat)
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
        static Texture2D CreateTexture(byte[] topDownPixels, int w, int h, TextureFormat format, string name, bool linear = true)
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

        // ────────────────────────────────────────────────────────────────
        //  グループツリー構築
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// ファイル格納順 (最下層→最上層) のフラットリストからツリーを構築する。
        /// lsct type3 (終端マーカー) でグループスコープを push、type1/2 (フォルダ) で pop して確定。
        /// マーカーはツリーに含めない。結果は index 0 = 最下層。
        /// </summary>
        static List<PSDLayer> BuildLayerTree(List<PSDLayer> flat, StringBuilder vlog)
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

        // ────────────────────────────────────────────────────────────────
        //  UI 初期値 (パーサーの責務)
        // ────────────────────────────────────────────────────────────────

        static void InitUIState(List<PSDLayer> layers)
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
                InitUIState(l.Children);
            }
        }

        // ════════════════════════════════════════════════════════════════
        //  Section 5: マージ済み画像
        // ════════════════════════════════════════════════════════════════

        static void ParseMergedImage(BigEndianBinaryReader r, PSDFile psd, StringBuilder vlog)
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
                        var plane = new byte[rowBytes * h];
                        for (int y = 0; y < h; y++)
                        {
                            byte[] src = r.ReadBytesExact(rowLens[c * h + y]);
                            DecodePackBitsRow(src, plane, y * rowBytes, rowBytes);
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
                        planes[c] = Downsample16To8(planes[c], w * h);

                byte[] rgba = AssembleMergedPixels(psd, planes, w, h);
                if (rgba == null) return;

                // マージ参照用テクスチャはエディタGUIでのプレビュー表示専用のため、linear: false (sRGB) で作成する
                psd.MergedComposite = CreateTexture(rgba, w, h, TextureFormat.RGBA32, "Merged Composite", false);
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
                    Debug.LogWarning($"[PSDParser] カラーモード {ColorModeName(psd.ColorMode)} は輝度のみの近似表示です");
                    for (int i = 0, o = 0; i < n; i++, o += 4)
                    {
                        byte v = planes[0][i];
                        rgba[o] = v; rgba[o + 1] = v; rgba[o + 2] = v; rgba[o + 3] = 255;
                    }
                    return rgba;
            }
        }

        // ════════════════════════════════════════════════════════════════
        //  ブレンドモード変換
        // ════════════════════════════════════════════════════════════════

        static readonly Dictionary<string, BlendMode> KeyToBlendMode = new Dictionary<string, BlendMode>
        {
            { "norm", BlendMode.Normal       }, { "mul ", BlendMode.Multiply     },
            { "scrn", BlendMode.Screen       }, { "over", BlendMode.Overlay      },
            { "diss", BlendMode.Dissolve     }, { "dark", BlendMode.Darken       },
            { "idiv", BlendMode.ColorBurn    }, { "lbrn", BlendMode.LinearBurn   },
            { "dkCl", BlendMode.DarkerColor  }, { "lite", BlendMode.Lighten      },
            { "div ", BlendMode.ColorDodge   }, { "lddg", BlendMode.LinearDodge  },
            { "lgCl", BlendMode.LighterColor }, { "sLit", BlendMode.SoftLight    },
            { "hLit", BlendMode.HardLight    }, { "vLit", BlendMode.VividLight   },
            { "lLit", BlendMode.LinearLight  }, { "pLit", BlendMode.PinLight     },
            { "hMix", BlendMode.HardMix      }, { "diff", BlendMode.Difference   },
            { "smud", BlendMode.Exclusion    }, { "fsub", BlendMode.Subtract     },
            { "fdiv", BlendMode.Divide       }, { "hue ", BlendMode.Hue          },
            { "sat ", BlendMode.Saturation   }, { "colr", BlendMode.Color        },
            { "lum ", BlendMode.Luminosity   }, { "pass", BlendMode.PassThrough  },
        };

        static BlendMode BlendModeFromKey(string key)
        {
            return KeyToBlendMode.TryGetValue(key, out var mode) ? mode : BlendMode.Unknown;
        }

        // ディスクリプタの enum 値 (BlnM) → BlendMode (lfx2 用)
        static readonly Dictionary<string, BlendMode> EnumToBlendMode = new Dictionary<string, BlendMode>
        {
            { "Nrml", BlendMode.Normal       }, { "Dslv", BlendMode.Dissolve     },
            { "Drkn", BlendMode.Darken       }, { "Mltp", BlendMode.Multiply     },
            { "CBrn", BlendMode.ColorBurn    }, { "linearBurn", BlendMode.LinearBurn },
            { "darkerColor", BlendMode.DarkerColor }, { "Lghn", BlendMode.Lighten },
            { "Scrn", BlendMode.Screen       }, { "CDdg", BlendMode.ColorDodge   },
            { "linearDodge", BlendMode.LinearDodge }, { "lighterColor", BlendMode.LighterColor },
            { "Ovrl", BlendMode.Overlay      }, { "SftL", BlendMode.SoftLight    },
            { "HrdL", BlendMode.HardLight    }, { "vividLight", BlendMode.VividLight },
            { "linearLight", BlendMode.LinearLight }, { "pinLight", BlendMode.PinLight },
            { "hardMix", BlendMode.HardMix   }, { "Dfrn", BlendMode.Difference   },
            { "Xclu", BlendMode.Exclusion    }, { "blendSubtraction", BlendMode.Subtract },
            { "blendDivide", BlendMode.Divide }, { "H   ", BlendMode.Hue         },
            { "Strt", BlendMode.Saturation   }, { "Clr ", BlendMode.Color        },
            { "Lmns", BlendMode.Luminosity   },
        };

        static BlendMode BlendModeFromEnumValue(string value)
        {
            return EnumToBlendMode.TryGetValue(value, out var mode) ? mode : BlendMode.Normal;
        }

        // ════════════════════════════════════════════════════════════════
        //  最小ディスクリプタパーサー (SoCo / CgEd / lfx2 用)
        // ════════════════════════════════════════════════════════════════
        //  目的キーの抽出のみが目標。未知の OSType に遭遇したら DescriptorException を
        //  投げ、呼び出し側 (HandleAdditionalInfo の try/finally) が警告 + dataEnd への
        //  seek で回収するため、ストリーム位置は壊れない。

        class DescriptorException : IOException
        {
            public DescriptorException(string message) : base(message) { }
        }

        const int MaxDescriptorDepth = 24;
        const int MaxDescriptorItems = 4096;

        /// <summary>ディスクリプタを Dictionary&lt;キー文字列, object&gt; として読む。
        /// 値: double / long / bool / string (TEXT・enum 値) / Dictionary (Objc) / List (VlLs) / null (スキップ型)。</summary>
        static Dictionary<string, object> ParseDescriptor(BigEndianBinaryReader r, int depth)
        {
            if (depth > MaxDescriptorDepth)
                throw new DescriptorException("ディスクリプタのネストが深すぎます");

            r.ReadUnicodeString();   // クラス名 (Unicode) — 不要
            ReadDescriptorID(r);     // クラス ID — 不要
            uint count = r.ReadUInt32();
            if (count > MaxDescriptorItems)
                throw new DescriptorException($"ディスクリプタ項目数が不正です: {count}");

            var dict = new Dictionary<string, object>();
            for (uint i = 0; i < count; i++)
            {
                string key   = ReadDescriptorID(r);
                object value = ParseDescriptorValue(r, depth);
                dict[key] = value;
            }
            return dict;
        }

        /// <summary>ID (長さ uint32、0 なら 4 バイト固定) を読む。</summary>
        static string ReadDescriptorID(BigEndianBinaryReader r)
        {
            uint len = r.ReadUInt32();
            if (len > 1024)
                throw new DescriptorException($"ディスクリプタ ID 長が不正です: {len}");
            return r.ReadAscii(len == 0 ? 4 : (int)len);
        }

        static object ParseDescriptorValue(BigEndianBinaryReader r, int depth)
        {
            string osType = r.ReadAscii(4);
            switch (osType)
            {
                case "Objc": // ネストディスクリプタ
                case "GlbO":
                    return ParseDescriptor(r, depth + 1);

                case "VlLs": // リスト
                {
                    uint count = r.ReadUInt32();
                    if (count > MaxDescriptorItems)
                        throw new DescriptorException($"リスト項目数が不正です: {count}");
                    var list = new List<object>((int)count);
                    for (uint i = 0; i < count; i++)
                        list.Add(ParseDescriptorValue(r, depth + 1));
                    return list;
                }

                case "doub": return r.ReadDoubleBE();
                case "UntF": r.ReadAscii(4); return r.ReadDoubleBE(); // 単位 4B + double
                case "TEXT": return r.ReadUnicodeString();
                case "enum": ReadDescriptorID(r); return ReadDescriptorID(r); // 型 ID は捨て、値 ID を返す
                case "long": return (long)r.ReadInt32();
                case "comp": return r.ReadInt64BE();
                case "bool": return r.ReadByte() != 0;

                case "type": // クラス参照 (値は使わない)
                case "GlbC":
                    r.ReadUnicodeString();
                    ReadDescriptorID(r);
                    return null;

                case "alis": // エイリアス / 生データ: 長さベースでスキップ
                case "tdta":
                {
                    uint len = r.ReadUInt32();
                    r.Skip(len);
                    return null;
                }

                case "obj ": // リファレンス
                    return ParseDescriptorReference(r);

                default:
                    // 長さ不明の型 (ObAr 等) は安全にスキップできないため中断
                    throw new DescriptorException($"未知の OSType です: '{osType}'");
            }
        }

        static object ParseDescriptorReference(BigEndianBinaryReader r)
        {
            uint count = r.ReadUInt32();
            if (count > MaxDescriptorItems)
                throw new DescriptorException($"リファレンス項目数が不正です: {count}");
            for (uint i = 0; i < count; i++)
            {
                string form = r.ReadAscii(4);
                switch (form)
                {
                    case "prop": r.ReadUnicodeString(); ReadDescriptorID(r); ReadDescriptorID(r); break;
                    case "Clss": r.ReadUnicodeString(); ReadDescriptorID(r); break;
                    case "Enmr": r.ReadUnicodeString(); ReadDescriptorID(r); ReadDescriptorID(r); ReadDescriptorID(r); break;
                    case "rele": r.ReadUnicodeString(); ReadDescriptorID(r); r.ReadUInt32(); break;
                    case "Idnt": r.ReadUInt32(); break;
                    case "indx": r.ReadUInt32(); break;
                    case "name": r.ReadUnicodeString(); ReadDescriptorID(r); r.ReadUnicodeString(); break;
                    default: throw new DescriptorException($"未知のリファレンス形式です: '{form}'");
                }
            }
            return null;
        }

        /// <summary>ディスクリプタから子ディスクリプタ (Objc) を取得する。無ければ null。</summary>
        static Dictionary<string, object> GetChildDescriptor(Dictionary<string, object> dict, string key)
        {
            if (dict != null && dict.TryGetValue(key, out object v))
                return v as Dictionary<string, object>;
            return null;
        }

        /// <summary>ディスクリプタから数値 (doub / long / UntF / bool) を取得する。</summary>
        static bool TryGetNumber(Dictionary<string, object> dict, string key, out double value)
        {
            value = 0;
            if (dict == null || !dict.TryGetValue(key, out object v)) return false;
            switch (v)
            {
                case double d: value = d;          return true;
                case long l:   value = l;          return true;
                case bool b:   value = b ? 1 : 0;  return true;
                default:       return false;
            }
        }

        // ════════════════════════════════════════════════════════════════
        //  デバッグダンプ
        // ════════════════════════════════════════════════════════════════

        static void DumpLayer(StringBuilder vlog, int index, PSDLayer layer)
        {
            if (vlog == null) return;
            string mask = layer.HasMask
                ? $"({layer.MaskLeft},{layer.MaskTop},{layer.MaskRight},{layer.MaskBottom}) def={layer.MaskDefaultColor}{(layer.MaskIsDisabled ? " 無効" : "")}"
                : "-";
            string adj = "";
            if (layer.Adjustment.HasBrightnessContrast) adj += $" brit({layer.Adjustment.Brightness},{layer.Adjustment.Contrast})";
            if (layer.Adjustment.HasHueSaturation)      adj += $" hue2({layer.Adjustment.Hue},{layer.Adjustment.Saturation},{layer.Adjustment.Lightness})";
            if (layer.Adjustment.HasSolidColor)         adj += $" SoCo({layer.Adjustment.SolidColor})";
            if (layer.Effects != null && layer.Effects.HasColorOverlay) adj += " ColorOverlay";

            vlog.AppendLine(
                $"  [{index}] '{layer.Name}' sect={layer.SectionType} " +
                $"rect=({layer.Left},{layer.Top},{layer.Right},{layer.Bottom}) " +
                $"blend={layer.BlendKeyRaw}({layer.BlendMode}) opacity={layer.Opacity} " +
                $"visible={layer.IsVisible} clip={layer.IsClipping} mask={mask}{adj}");
        }
    }
}
