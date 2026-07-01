using System.Collections.Generic;
using UnityEngine;

namespace PSDSimpleEditor
{
    // ════════════════════════════════════════════════════════════════
    //  追加情報 (Additional Layer Information) ハンドラ
    //  (lsct グループ / brit・hue2・SoCo 調整 / lrFX・lfx2 エフェクト)
    // ════════════════════════════════════════════════════════════════
    internal static class PSDAdditionalInfoParser
    {
        internal static void HandleAdditionalInfo(BigEndianBinaryReader r, PSDLayer layer, string key, uint len)
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

                case "invr": // 階調反転 (パラメータなし。キー存在のみで判定)
                    layer.Adjustment.HasInvert = true;
                    break;

                case "thrs": // しきい値
                    HandleThreshold(r, layer);
                    break;

                case "post": // ポスタリゼーション
                    HandlePosterize(r, layer);
                    break;

                case "levl": // レベル補正
                    HandleLevels(r, layer);
                    break;

                case "curv": // トーンカーブ
                    HandleCurves(r, layer);
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
                    var mode = PSDBlendModeConverter.BlendModeFromKey(r.ReadAscii(4));
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
            var d = PSDDescriptorParser.ParseDescriptor(r, 0);
            bool any = false;
            if (PSDDescriptorParser.TryGetNumber(d, "Brghtnss", out double b)) { layer.Adjustment.Brightness = (float)b; any = true; }
            if (PSDDescriptorParser.TryGetNumber(d, "Cntrst",   out double c)) { layer.Adjustment.Contrast   = (float)c; any = true; }
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
            var d   = PSDDescriptorParser.ParseDescriptor(r, 0);
            var clr = PSDDescriptorParser.GetChildDescriptor(d, "Clr ");
            if (clr != null &&
                PSDDescriptorParser.TryGetNumber(clr, "Rd  ", out double cr) &&
                PSDDescriptorParser.TryGetNumber(clr, "Grn ", out double cg) &&
                PSDDescriptorParser.TryGetNumber(clr, "Bl  ", out double cb))
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

        static void HandleThreshold(BigEndianBinaryReader r, PSDLayer layer)
        {
            r.ReadUInt16();                     // version (=1)
            ushort level = r.ReadUInt16();       // 0 .. 255
            layer.Adjustment.HasThreshold  = true;
            layer.Adjustment.ThresholdLevel = level;
        }

        static void HandlePosterize(BigEndianBinaryReader r, PSDLayer layer)
        {
            r.ReadUInt16();                     // version (=1)
            ushort levels = r.ReadUInt16();      // 2 .. 255
            layer.Adjustment.HasPosterize     = true;
            layer.Adjustment.PosterizeLevels = Mathf.Max(2, (int)levels);
        }

        static void HandleLevels(BigEndianBinaryReader r, PSDLayer layer)
        {
            // 形式: version(2B, =2) + 複合チャンネルの 10 バイトレコード
            //       (残りの per-channel レコードは呼び出し側の境界 seek でスキップされる)
            r.ReadUInt16();                       // version
            ushort shadowInput     = r.ReadUInt16();
            ushort highlightInput  = r.ReadUInt16();
            ushort shadowOutput    = r.ReadUInt16();
            ushort highlightOutput = r.ReadUInt16();
            ushort midtoneGamma    = r.ReadUInt16(); // 実値 ×100 (100 = ガンマ 1.00)

            layer.Adjustment.HasLevels        = true;
            layer.Adjustment.LevelsInputBlack  = shadowInput;
            layer.Adjustment.LevelsInputWhite  = highlightInput;
            layer.Adjustment.LevelsGamma       = Mathf.Max(0.01f, midtoneGamma / 100f);
            layer.Adjustment.LevelsOutputBlack = shadowOutput;
            layer.Adjustment.LevelsOutputWhite = highlightOutput;
        }

        static void HandleCurves(BigEndianBinaryReader r, PSDLayer layer)
        {
            // 形式: version(2B, =1) + channelCount(2B) + 各チャンネル (channelId(2B) + pointCount(2B)
            //       + pointCount × (outputValue(2B) inputValue(2B)))。
            //       複合/コンポジットチャンネル (先頭) のみ v1 で対応する。
            r.ReadUInt16();                       // version
            ushort channelCount = r.ReadUInt16();
            if (channelCount == 0) return;

            r.ReadUInt16();                       // channelId (先頭チャンネル)
            ushort pointCount = r.ReadUInt16();
            var points = new List<Vector2>(pointCount);
            for (int i = 0; i < pointCount; i++)
            {
                ushort output = r.ReadUInt16();
                ushort input  = r.ReadUInt16();
                points.Add(new Vector2(input, output));
            }
            if (points.Count < 2) return;

            layer.Adjustment.HasCurves    = true;
            layer.Adjustment.CurvePoints = points;
        }

        static void HandleLfx2(BigEndianBinaryReader r, PSDLayer layer)
        {
            r.ReadUInt32();                  // オブジェクトバージョン (0)
            uint descVersion = r.ReadUInt32();
            if (descVersion != 16) return;
            var d = PSDDescriptorParser.ParseDescriptor(r, 0);

            // エフェクト全体の有効スイッチ
            if (d.TryGetValue("masterFXSwitch", out object sw) && sw is bool master && !master) return;

            var sofi = PSDDescriptorParser.GetChildDescriptor(d, "SoFi"); // Color Overlay (Solid Fill)
            if (sofi == null) return;
            if (sofi.TryGetValue("enab", out object en) && en is bool enabled && !enabled) return;

            var clr = PSDDescriptorParser.GetChildDescriptor(sofi, "Clr ");
            if (clr == null) return;
            if (!PSDDescriptorParser.TryGetNumber(clr, "Rd  ", out double cr) ||
                !PSDDescriptorParser.TryGetNumber(clr, "Grn ", out double cg) ||
                !PSDDescriptorParser.TryGetNumber(clr, "Bl  ", out double cb)) return;

            var fx = new LayerEffects
            {
                HasColorOverlay = true,
                OverlayColor = new Color(
                    Mathf.Clamp01((float)(cr / 255.0)),
                    Mathf.Clamp01((float)(cg / 255.0)),
                    Mathf.Clamp01((float)(cb / 255.0)), 1f),
            };
            if (PSDDescriptorParser.TryGetNumber(sofi, "Opct", out double opct))
                fx.OverlayOpacity = Mathf.Clamp01((float)(opct / 100.0)); // UntF パーセント
            if (sofi.TryGetValue("Md  ", out object md) && md is string mode)
                fx.OverlayBlendMode = PSDBlendModeConverter.BlendModeFromEnumValue(mode);

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
                            OverlayBlendMode = PSDBlendModeConverter.BlendModeFromKey(bkey),
                            OverlayOpacity   = opacity / 255f,
                        };
                    }
                }
                r.Seek(effectEnd);
            }
        }
    }
}
