using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PSDSimpleEditor
{
    // ════════════════════════════════════════════════════════════════
    //  調整レイヤーキーのバイナリ組み立て (PSDAdditionalInfoParser の逆)
    //
    //  PSD 書き出し時に、調整レイヤー / SoCo べた塗り / GdFl グラデーション塗りつぶしの
    //  内容を追加情報ブロックへ書き戻す。値は UI で編集中のもの (UI*) を採用するため、
    //  ツール内での編集がそのまま Photoshop 側の調整レイヤーとして往復する。
    //  バイナリ形式は psd-tools (adjustments.py) と照合済み。
    // ════════════════════════════════════════════════════════════════
    internal static class PSDAdjustmentInfoWriter
    {
        /// <summary>
        /// レイヤーが書き戻すべき追加情報ブロック群を組み立てる。対象外・内容なしは null。
        /// 対象: ゼロ面積の調整レイヤー / SoCo / GdFl。ピクセルレイヤーの非破壊補正は
        /// RenderLayerForExport で画素へ焼き込まれるため対象外。
        /// </summary>
        internal static List<ExportExtraBlock> BuildBlocks(PSDLayer layer)
        {
            var a = layer.Adjustment;
            if (a == null) return null;
            if (!layer.IsAdjustmentLayer && !a.HasSolidColor && !a.HasGradientFill) return null;

            var blocks = new List<ExportExtraBlock>();

            if (a.HasSolidColor)
                blocks.Add(EncodeSoCo(a.SolidColor));

            if (a.HasGradientFill && a.GradientFillGradient != null)
                blocks.Add(EncodeGdFl(a));

            if (a.HasBrightnessContrast)
            {
                blocks.Add(EncodeBrit(layer.UIBrightness, layer.UIContrast));
                blocks.Add(EncodeCgEd(layer.UIBrightness, layer.UIContrast));
            }

            if (a.HasHueSaturation)
                blocks.Add(EncodeHue2(layer.UIHue, layer.UISaturation, layer.UILightness));

            if (a.HasInvert && layer.UIInvert)
                blocks.Add(new ExportExtraBlock { Key = "nvrt", Data = new byte[0] });

            if (a.HasThreshold && layer.UIThresholdEnabled)
                blocks.Add(EncodeThrs(layer.UIThresholdLevel));

            if (a.HasPosterize && layer.UIPosterizeEnabled)
                blocks.Add(EncodePost(layer.UIPosterizeLevels));

            if (a.HasLevels && layer.UILevelsEnabled)
                blocks.Add(EncodeLevl(layer));

            if (a.HasCurves && layer.UICurveEnabled)
            {
                var b = EncodeCurv(layer);
                if (b != null) blocks.Add(b);
            }

            if (a.HasColorBalance && layer.UIColorBalanceEnabled)
                blocks.Add(EncodeBlnc(layer));

            if (a.HasGradientMap && layer.UIGradientMapEnabled && layer.UIGradient != null)
                blocks.Add(EncodeGrdm(layer.UIGradient));

            return blocks.Count > 0 ? blocks : null;
        }

        // ────────────────────────────────────────────────────────────────
        //  共通: MemoryStream へ書いて byte[] 化
        // ────────────────────────────────────────────────────────────────

        static byte[] Build(Action<BigEndianBinaryWriter> write)
        {
            using (var ms = new MemoryStream())
            using (var w = new BigEndianBinaryWriter(ms))
            {
                write(w);
                w.Flush();
                return ms.ToArray();
            }
        }

        static short RoundClamp(float v, int min, int max)
            => (short)Mathf.Clamp(Mathf.RoundToInt(v), min, max);

        // ────────────────────────────────────────────────────────────────
        //  固定バイナリ形式のキー
        // ────────────────────────────────────────────────────────────────

        // brit (旧形式): brightness(2) contrast(2) mean(2) labOnly(1) + パディング(1)。
        // 旧形式のレンジは ±100 のためクランプする (正確な値は CgEd 側が持つ)
        static ExportExtraBlock EncodeBrit(float brightness, float contrast) => new ExportExtraBlock
        {
            Key = "brit",
            Data = Build(w =>
            {
                w.WriteInt16(RoundClamp(brightness, -100, 100));
                w.WriteInt16(RoundClamp(contrast,   -100, 100));
                w.WriteInt16(127);  // mean
                w.Write((byte)0);   // Lab のみフラグ
                w.Write((byte)0);   // パディング
            }),
        };

        // thrs: level(2) + パディング(2)。version フィールドは存在しない
        static ExportExtraBlock EncodeThrs(float level) => new ExportExtraBlock
        {
            Key = "thrs",
            Data = Build(w =>
            {
                w.WriteUInt16((ushort)RoundClamp(level, 1, 255));
                w.WriteUInt16(0); // パディング
            }),
        };

        // post: levels(2) + パディング(2)。version フィールドは存在しない
        static ExportExtraBlock EncodePost(float levels) => new ExportExtraBlock
        {
            Key = "post",
            Data = Build(w =>
            {
                w.WriteUInt16((ushort)RoundClamp(levels, 2, 255));
                w.WriteUInt16(0); // パディング
            }),
        };

        // blnc: (CR,MG,YB)×シャドウ/中間調/ハイライト の int16×9 + preserveLum(1) + パディング(1)
        static ExportExtraBlock EncodeBlnc(PSDLayer layer) => new ExportExtraBlock
        {
            Key = "blnc",
            Data = Build(w =>
            {
                WriteCbTriple(w, layer.UICBShadows);
                WriteCbTriple(w, layer.UICBMidtones);
                WriteCbTriple(w, layer.UICBHighlights);
                w.Write((byte)(layer.UICBPreserveLuminosity ? 1 : 0));
                w.Write((byte)0); // パディング
            }),
        };

        static void WriteCbTriple(BigEndianBinaryWriter w, Vector3 v)
        {
            w.WriteInt16(RoundClamp(v.x, -100, 100));
            w.WriteInt16(RoundClamp(v.y, -100, 100));
            w.WriteInt16(RoundClamp(v.z, -100, 100));
        }

        // Photoshop 既定の 6 色域レンジ (レッド/イエロー/グリーン/シアン/ブルー/マゼンタ)
        static readonly short[,] Hue2DefaultRanges =
        {
            { 315, 345,  15,  45 },
            {  15,  45,  75, 105 },
            {  75, 105, 135, 165 },
            { 135, 165, 195, 225 },
            { 195, 225, 255, 285 },
            { 255, 285, 315, 345 },
        };

        // hue2: version(2) colorization(1) pad(1) + colorization 値 3×int16 + master 値 3×int16
        //       + 6 色域レコード (レンジ 4×int16 + 補正 3×int16)
        static ExportExtraBlock EncodeHue2(float hue, float saturation, float lightness) => new ExportExtraBlock
        {
            Key = "hue2",
            Data = Build(w =>
            {
                w.WriteUInt16(2);  // version
                w.Write((byte)0);  // colorization フラグ (常に master 値として書く)
                w.Write((byte)0);  // パディング
                // colorization 用スロット (未使用。Photoshop 既定値)
                w.WriteInt16(0); w.WriteInt16(25); w.WriteInt16(0);
                // master 値
                w.WriteInt16(RoundClamp(hue,       -180, 180));
                w.WriteInt16(RoundClamp(saturation, -100, 100));
                w.WriteInt16(RoundClamp(lightness,  -100, 100));
                // 6 色域レコード (レンジは既定値、補正値は 0)
                for (int i = 0; i < 6; i++)
                {
                    for (int j = 0; j < 4; j++) w.WriteInt16(Hue2DefaultRanges[i, j]);
                    w.WriteInt16(0); w.WriteInt16(0); w.WriteInt16(0);
                }
            }),
        };

        // levl: version(2, =2) + 10 バイトレコード × 29 ([0]=複合, [1..3]=R/G/B, 残りは恒等)
        static ExportExtraBlock EncodeLevl(PSDLayer layer) => new ExportExtraBlock
        {
            Key = "levl",
            Data = Build(w =>
            {
                w.WriteUInt16(2); // version
                WriteLevelRecord(w,
                    layer.UILevelsInputBlack, layer.UILevelsInputWhite,
                    layer.UILevelsOutputBlack, layer.UILevelsOutputWhite,
                    layer.UILevelsGamma);

                var a = layer.Adjustment;
                for (int i = 1; i < 29; i++)
                {
                    int c = i - 1;
                    if (c < 3 && a.HasChannelLevels &&
                        a.LevelsChannelRanges != null && a.LevelsChannelGamma != null)
                    {
                        var v = a.LevelsChannelRanges[c];
                        WriteLevelRecord(w, v.x, v.y, v.z, v.w, a.LevelsChannelGamma[c]);
                    }
                    else
                    {
                        WriteLevelRecord(w, 0f, 255f, 0f, 255f, 1f); // 恒等
                    }
                }
            }),
        };

        static void WriteLevelRecord(BigEndianBinaryWriter w,
                                     float inB, float inW, float outB, float outW, float gamma)
        {
            w.WriteUInt16((ushort)RoundClamp(inB,  0, 255));
            w.WriteUInt16((ushort)RoundClamp(inW,  0, 255));
            w.WriteUInt16((ushort)RoundClamp(outB, 0, 255));
            w.WriteUInt16((ushort)RoundClamp(outW, 0, 255));
            w.WriteUInt16((ushort)Mathf.Clamp(Mathf.RoundToInt(gamma * 100f), 10, 999));
        }

        // curv: is_map(1, =0) + version(2, =1) + チャンネルビットマップ(4)
        //       + チャンネルごと (ビット昇順): pointCount(2) + 点 (output(2), input(2))×N
        //       複合カーブは UICurve (編集値)、R/G/B はパース済み点列をそのまま書き戻す
        static ExportExtraBlock EncodeCurv(PSDLayer layer)
        {
            var composite = CurvePointsFrom(layer.UICurve);
            if (composite == null) composite = layer.Adjustment.CurvePoints;
            if (composite == null || composite.Count < 2) return null;

            var chPts = layer.Adjustment.HasChannelCurves ? layer.Adjustment.CurveChannelPoints : null;
            uint bitmap = 1u; // bit0 = 複合チャンネル
            if (chPts != null)
                for (int c = 0; c < 3; c++)
                    if (chPts[c] != null && chPts[c].Count >= 2)
                        bitmap |= 1u << (c + 1);

            var compositeFinal = composite;
            return new ExportExtraBlock
            {
                Key = "curv",
                Data = Build(w =>
                {
                    w.Write((byte)0);      // is_map = 0 (ポイント形式)
                    w.WriteUInt16(1);      // version
                    w.WriteUInt32(bitmap);
                    WriteCurvePoints(w, compositeFinal);
                    if (chPts != null)
                        for (int c = 0; c < 3; c++)
                            if ((bitmap & (1u << (c + 1))) != 0)
                                WriteCurvePoints(w, chPts[c]);
                }),
            };
        }

        /// <summary>AnimationCurve のキーを 0..255 空間の (入力, 出力) 点列へ変換する
        /// (入力昇順・重複除去・仕様上限 19 点に間引き)。2 点未満は null。</summary>
        static List<Vector2> CurvePointsFrom(AnimationCurve curve)
        {
            if (curve == null || curve.length < 2) return null;

            var pts = new List<Vector2>(curve.length);
            foreach (var k in curve.keys)
                pts.Add(new Vector2(
                    Mathf.Clamp01(k.time)  * 255f,
                    Mathf.Clamp01(k.value) * 255f));
            pts.Sort((x, y) => x.x.CompareTo(y.x));

            // 丸め後に入力値が重複する点は除去 (Photoshop は入力の昇順一意を要求する)
            var result = new List<Vector2>(pts.Count);
            int lastInput = -1;
            foreach (var p in pts)
            {
                int input = Mathf.RoundToInt(p.x);
                if (input == lastInput) continue;
                lastInput = input;
                result.Add(p);
            }
            if (result.Count < 2) return null;

            // 仕様上限 19 点。超過は端点を保って等間隔に間引く
            if (result.Count > 19)
            {
                var trimmed = new List<Vector2>(19);
                for (int i = 0; i < 19; i++)
                    trimmed.Add(result[Mathf.RoundToInt(i * (result.Count - 1) / 18f)]);
                result = trimmed;
            }
            return result;
        }

        static void WriteCurvePoints(BigEndianBinaryWriter w, List<Vector2> pts)
        {
            w.WriteUInt16((ushort)pts.Count);
            foreach (var p in pts)
            {
                w.WriteUInt16((ushort)RoundClamp(p.y, 0, 255)); // output
                w.WriteUInt16((ushort)RoundClamp(p.x, 0, 255)); // input
            }
        }

        // grdm: version(2, =1) reverse(1) dithered(1) name(Unicode)
        //       + カラーストップ (location(4, 0..4096) midpoint(4, %) colorSpace(2) 成分 4×2 pad(2))
        //       + 透明ストップ (location(4) midpoint(4) opacity(2, %))
        //       + 末尾フィールド (ソリッドグラデーションの標準値)
        static ExportExtraBlock EncodeGrdm(Gradient gradient)
        {
            var colorKeys = gradient.colorKeys;
            var alphaKeys = gradient.alphaKeys;
            return new ExportExtraBlock
            {
                Key = "grdm",
                Data = Build(w =>
                {
                    w.WriteUInt16(1);  // version
                    w.Write((byte)0);  // reverse (UIGradient に反映済みのため常に 0)
                    w.Write((byte)0);  // dithered
                    w.WriteUnicodeString("Custom");

                    // カラーストップ (成分は 16bit 0..65535 スケール)
                    w.WriteUInt16((ushort)colorKeys.Length);
                    foreach (var k in colorKeys)
                    {
                        w.WriteUInt32((uint)Mathf.RoundToInt(Mathf.Clamp01(k.time) * 4096f));
                        w.WriteUInt32(50);                 // midpoint (%)
                        w.WriteUInt16(0);                  // カラースペース = RGB
                        w.WriteUInt16(To16(k.color.r));
                        w.WriteUInt16(To16(k.color.g));
                        w.WriteUInt16(To16(k.color.b));
                        w.WriteUInt16(0);                  // 第 4 成分 (RGB では未使用)
                        w.WriteUInt16(0);                  // パディング
                    }

                    // 透明ストップ
                    w.WriteUInt16((ushort)alphaKeys.Length);
                    foreach (var k in alphaKeys)
                    {
                        w.WriteUInt32((uint)Mathf.RoundToInt(Mathf.Clamp01(k.time) * 4096f));
                        w.WriteUInt32(50);                 // midpoint (%)
                        w.WriteUInt16((ushort)Mathf.RoundToInt(Mathf.Clamp01(k.alpha) * 100f));
                    }

                    // 末尾フィールド (ソリッドグラデーションの標準値)
                    w.WriteUInt16(2);      // expansion
                    w.WriteUInt16(4096);   // interpolation (滑らかさ 100%)
                    w.WriteUInt16(32);     // length
                    w.WriteUInt16(0);      // mode = ソリッド
                    w.WriteUInt32(0);      // random seed
                    w.WriteUInt16(0);      // show transparency
                    w.WriteUInt16(0);      // use vector color
                    w.WriteUInt32(0);      // roughness
                    w.WriteUInt16(0);      // color model
                    for (int i = 0; i < 4; i++) w.WriteUInt16(0); // minimum color
                    for (int i = 0; i < 4; i++) w.WriteUInt16(0); // maximum color
                    w.WriteUInt16(0);      // パディング
                }),
            };
        }

        static ushort To16(float v) => (ushort)Mathf.RoundToInt(Mathf.Clamp01(v) * 65535f);

        // ────────────────────────────────────────────────────────────────
        //  ディスクリプタ形式のキー (SoCo / CgEd / GdFl)
        // ────────────────────────────────────────────────────────────────

        // SoCo: version(4, =16) + ディスクリプタ { 'Clr ' (Objc RGBC) { Rd/Grn/Bl (doub 0..255) } }
        static ExportExtraBlock EncodeSoCo(Color color) => new ExportExtraBlock
        {
            Key = "SoCo",
            Data = Build(w =>
            {
                w.WriteUInt32(16);
                WriteDescriptorHeader(w, "null", 1);
                WriteKeyObject(w, "Clr ", "RGBC", 3);
                WriteKeyDouble(w, "Rd  ", color.r * 255.0);
                WriteKeyDouble(w, "Grn ", color.g * 255.0);
                WriteKeyDouble(w, "Bl  ", color.b * 255.0);
            }),
        };

        // CgEd: 新形式の明るさ・コントラスト (レンジ ±150 / -50..100 の正確な値を持つ)
        static ExportExtraBlock EncodeCgEd(float brightness, float contrast) => new ExportExtraBlock
        {
            Key = "CgEd",
            Data = Build(w =>
            {
                w.WriteUInt32(16);
                WriteDescriptorHeader(w, "null", 7);
                WriteKeyLong(w, "Vrsn", 1);
                WriteKeyLong(w, "Brghtnss", Mathf.Clamp(Mathf.RoundToInt(brightness), -150, 150));
                WriteKeyLong(w, "Cntrst",   Mathf.Clamp(Mathf.RoundToInt(contrast),   -50, 100));
                WriteKeyLong(w, "means", 127);
                WriteKeyBool(w, "Lab ", false);
                WriteKeyBool(w, "useLegacy", false);
                WriteKeyBool(w, "Auto", false);
            }),
        };

        // GdFl: version(4, =16) + ディスクリプタ { Angl / Type / Scl / Grad { Nm/GrdF/Intr/Clrs/Trns } }
        static ExportExtraBlock EncodeGdFl(AdjustmentData a)
        {
            var colorKeys = a.GradientFillGradient.colorKeys;
            var alphaKeys = a.GradientFillGradient.alphaKeys;
            return new ExportExtraBlock
            {
                Key = "GdFl",
                Data = Build(w =>
                {
                    w.WriteUInt32(16);
                    WriteDescriptorHeader(w, "null", 4);
                    WriteKeyUnitFloat(w, "Angl", "#Ang", a.GradientFillAngle);
                    WriteKeyEnum(w, "Type", "GrdT", a.GradientFillRadial ? "Rdl " : "Lnr ");
                    WriteKeyUnitFloat(w, "Scl ", "#Prc", a.GradientFillScale * 100.0);

                    // Grad (Grdn): 名前 / 形式 / 滑らかさ / カラーストップ / 透明ストップ
                    WriteKeyObject(w, "Grad", "Grdn", 5);
                    WriteKeyText(w, "Nm  ", "Custom");
                    WriteKeyEnum(w, "GrdF", "GrdF", "CstS");
                    WriteKeyDouble(w, "Intr", 4096.0);

                    WriteKeyListStart(w, "Clrs", colorKeys.Length);
                    foreach (var k in colorKeys)
                    {
                        WriteListObjectStart(w, "Clrt", 4);
                        WriteKeyObject(w, "Clr ", "RGBC", 3);
                        WriteKeyDouble(w, "Rd  ", Mathf.Clamp01(k.color.r) * 255.0);
                        WriteKeyDouble(w, "Grn ", Mathf.Clamp01(k.color.g) * 255.0);
                        WriteKeyDouble(w, "Bl  ", Mathf.Clamp01(k.color.b) * 255.0);
                        WriteKeyEnum(w, "Type", "Clry", "UsrS");
                        WriteKeyLong(w, "Lctn", Mathf.RoundToInt(Mathf.Clamp01(k.time) * 4096f));
                        WriteKeyLong(w, "Mdpn", 50);
                    }

                    WriteKeyListStart(w, "Trns", alphaKeys.Length);
                    foreach (var k in alphaKeys)
                    {
                        WriteListObjectStart(w, "TrnS", 3);
                        WriteKeyUnitFloat(w, "Opct", "#Prc", Mathf.Clamp01(k.alpha) * 100.0);
                        WriteKeyLong(w, "Lctn", Mathf.RoundToInt(Mathf.Clamp01(k.time) * 4096f));
                        WriteKeyLong(w, "Mdpn", 50);
                    }
                }),
            };
        }

        // ── 最小ディスクリプタライター (PSDDescriptorParser が読める形式) ──

        static void WriteDescriptorHeader(BigEndianBinaryWriter w, string classId, int itemCount)
        {
            w.WriteUnicodeString("");      // クラス名 (Unicode) — 空
            WriteId(w, classId);           // クラス ID
            w.WriteUInt32((uint)itemCount);
        }

        // 4 文字 ID は長さ 0 + 4 文字、それ以外は長さ + 文字列
        static void WriteId(BigEndianBinaryWriter w, string id)
        {
            if (id.Length == 4) { w.WriteUInt32(0); }
            else                { w.WriteUInt32((uint)id.Length); }
            w.WriteAscii(id);
        }

        static void WriteKeyLong(BigEndianBinaryWriter w, string key, int value)
        {
            WriteId(w, key);
            w.WriteAscii("long");
            w.WriteInt32(value);
        }

        static void WriteKeyBool(BigEndianBinaryWriter w, string key, bool value)
        {
            WriteId(w, key);
            w.WriteAscii("bool");
            w.Write((byte)(value ? 1 : 0));
        }

        static void WriteKeyDouble(BigEndianBinaryWriter w, string key, double value)
        {
            WriteId(w, key);
            w.WriteAscii("doub");
            w.WriteDoubleBE(value);
        }

        static void WriteKeyUnitFloat(BigEndianBinaryWriter w, string key, string unit, double value)
        {
            WriteId(w, key);
            w.WriteAscii("UntF");
            w.WriteAscii(unit);
            w.WriteDoubleBE(value);
        }

        static void WriteKeyEnum(BigEndianBinaryWriter w, string key, string enumType, string enumValue)
        {
            WriteId(w, key);
            w.WriteAscii("enum");
            WriteId(w, enumType);
            WriteId(w, enumValue);
        }

        static void WriteKeyText(BigEndianBinaryWriter w, string key, string text)
        {
            WriteId(w, key);
            w.WriteAscii("TEXT");
            w.WriteUnicodeString(text);
        }

        /// <summary>ネストディスクリプタ (Objc) の開始。itemCount 個の WriteKey* が続くこと。</summary>
        static void WriteKeyObject(BigEndianBinaryWriter w, string key, string classId, int itemCount)
        {
            WriteId(w, key);
            w.WriteAscii("Objc");
            WriteDescriptorHeader(w, classId, itemCount);
        }

        /// <summary>リスト (VlLs) の開始。count 個のリスト要素が続くこと。</summary>
        static void WriteKeyListStart(BigEndianBinaryWriter w, string key, int count)
        {
            WriteId(w, key);
            w.WriteAscii("VlLs");
            w.WriteUInt32((uint)count);
        }

        /// <summary>リスト要素としての Objc の開始。itemCount 個の WriteKey* が続くこと。</summary>
        static void WriteListObjectStart(BigEndianBinaryWriter w, string classId, int itemCount)
        {
            w.WriteAscii("Objc");
            WriteDescriptorHeader(w, classId, itemCount);
        }
    }
}
