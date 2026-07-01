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

                case "nvrt": // 階調反転 (仕様書の正式キー。パラメータなし、キー存在のみで判定)
                case "invr": // 階調反転 (旧実装の別名。後方互換のため併記)
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

                case "grdm": // グラデーションマップ
                    HandleGradientMap(r, layer, len);
                    break;

                case "blnc": // カラーバランス
                    HandleColorBalance(r, layer, len);
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

        static void HandleGradientMap(BigEndianBinaryReader r, PSDLayer layer, uint len)
        {
            // grdm バイナリ構造 (libpsd / ag-psd 準拠):
            //   version(2) reverse(1) dithered(1) name(Unicode) colorStopCount(2)
            //   各カラーストップ: location(4, 0..4096) midpoint(4) colorSpace(2) 色成分(2×4) colorType(2)
            //   transparencyStopCount(2) 各: location(4) midpoint(4) opacity(2, 0..100)
            // 実バイト配置が想定と異なる場合は境界チェックで検出し途中で return する
            // (呼び出し側が必ず境界 seek するため、部分読みでも全体のパースは壊れない)。
            long end = r.Position + len;

            r.ReadUInt16();                 // version (=1)
            bool reverse = r.ReadByte() != 0;
            r.ReadByte();                   // dithered (プレビューには未使用)
            r.ReadUnicodeString();          // グラデーション名 (未使用)

            if (r.Position + 2 > end) return;
            int colorCount = r.ReadUInt16();
            if (colorCount <= 0 || colorCount > 256) return;

            // カラーストップを読み取り、成分の最大値から 8bit/16bit スケールを判定する
            var locs  = new List<float>(colorCount);
            var comps = new List<Vector3Int>(colorCount);
            int maxComp = 0;
            for (int i = 0; i < colorCount; i++)
            {
                if (r.Position + 20 > end) return; // 想定レイアウトと不一致
                uint location = r.ReadUInt32();    // 0..4096
                r.ReadUInt32();                     // midpoint (未使用)
                r.ReadUInt16();                     // colorSpace (0=RGB 前提)
                int c1 = r.ReadUInt16(), c2 = r.ReadUInt16(), c3 = r.ReadUInt16();
                r.ReadUInt16();                     // 4 成分目 (RGB では未使用)
                r.ReadUInt16();                     // colorType (user/fg/bg)
                locs.Add(Mathf.Clamp01(location / 4096f));
                comps.Add(new Vector3Int(c1, c2, c3));
                maxComp = Mathf.Max(maxComp, Mathf.Max(c1, Mathf.Max(c2, c3)));
            }
            float scale = maxComp > 255 ? 65535f : 255f;

            // 透明ストップ (best effort。失敗しても色キーは活かす)
            var alphaLocs = new List<float>();
            var alphaVals = new List<float>();
            if (r.Position + 2 <= end)
            {
                int transCount = r.ReadUInt16();
                if (transCount > 0 && transCount <= 256)
                {
                    for (int i = 0; i < transCount; i++)
                    {
                        if (r.Position + 10 > end) break;
                        uint location = r.ReadUInt32();
                        r.ReadUInt32();              // midpoint
                        int opacity = r.ReadUInt16(); // 0..100
                        alphaLocs.Add(Mathf.Clamp01(location / 4096f));
                        alphaVals.Add(Mathf.Clamp01(opacity / 100f));
                    }
                }
            }

            var grad = BuildGradient(locs, comps, scale, alphaLocs, alphaVals, reverse);
            if (grad == null) return;

            layer.Adjustment.HasGradientMap      = true;
            layer.Adjustment.GradientMapGradient = grad;
        }

        // grdm のカラー/透明ストップから Unity Gradient を構築する。
        // Unity のキー数上限 (色 8 / α 8) を超える場合は端点を保ちつつ等間隔で間引く。
        static Gradient BuildGradient(
            List<float> locs, List<Vector3Int> comps, float scale,
            List<float> alphaLocs, List<float> alphaVals, bool reverse)
        {
            int n = locs.Count;
            if (n == 0) return null;

            var color = new List<GradientColorKey>(n);
            for (int i = 0; i < n; i++)
            {
                float t = reverse ? 1f - locs[i] : locs[i];
                var c = comps[i];
                color.Add(new GradientColorKey(new Color(c.x / scale, c.y / scale, c.z / scale), t));
            }
            color.Sort((a, b) => a.time.CompareTo(b.time));

            var alpha = new List<GradientAlphaKey>(alphaLocs.Count);
            for (int i = 0; i < alphaLocs.Count; i++)
            {
                float t = reverse ? 1f - alphaLocs[i] : alphaLocs[i];
                alpha.Add(new GradientAlphaKey(alphaVals[i], t));
            }
            alpha.Sort((a, b) => a.time.CompareTo(b.time));
            if (alpha.Count == 0)
            {
                alpha.Add(new GradientAlphaKey(1f, 0f));
                alpha.Add(new GradientAlphaKey(1f, 1f));
            }

            var g = new Gradient { mode = GradientMode.Blend };
            g.SetKeys(Downsample(color, 8), Downsample(alpha, 8));
            return g;
        }

        // 端点を保持しつつ最大数まで等間隔サンプリングする。
        static T[] Downsample<T>(List<T> keys, int max)
        {
            if (keys.Count <= max) return keys.ToArray();
            var outp = new T[max];
            for (int i = 0; i < max; i++)
                outp[i] = keys[Mathf.RoundToInt(i * (keys.Count - 1) / (float)(max - 1))];
            return outp;
        }

        static void HandleColorBalance(BigEndianBinaryReader r, PSDLayer layer, uint len)
        {
            // blnc バイナリ構造: シャドウ/中間調/ハイライトごとに int16×3
            //   (cyan-red, magenta-green, yellow-blue, 各 -100..100)、末尾に preserveLuminosity(1B)。
            // 一部ファイルは末尾バイトを欠くため best effort で読む。
            long end = r.Position + len;
            if (r.Position + 18 > end) return; // 9×int16 が入らない → 想定外レイアウト

            short sr = r.ReadInt16(), sg = r.ReadInt16(), sb = r.ReadInt16();
            short mr = r.ReadInt16(), mg = r.ReadInt16(), mb = r.ReadInt16();
            short hr = r.ReadInt16(), hg = r.ReadInt16(), hb = r.ReadInt16();
            bool preserveLum = true;
            if (r.Position + 1 <= end)
                preserveLum = r.ReadByte() != 0;

            layer.Adjustment.HasColorBalance       = true;
            layer.Adjustment.CBShadows             = new Vector3(sr, sg, sb);
            layer.Adjustment.CBMidtones            = new Vector3(mr, mg, mb);
            layer.Adjustment.CBHighlights          = new Vector3(hr, hg, hb);
            layer.Adjustment.CBPreserveLuminosity  = preserveLum;
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
