using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using static PSDSimpleEditor.PSDDescriptorWriter;

namespace PSDSimpleEditor
{
    // ════════════════════════════════════════════════════════════════
    //  調整レイヤーキーのバイナリ組み立て (PSDAdditionalInfoParser の逆)
    //
    //  PSD 書き出し時に、調整レイヤー / SoCo べた塗り / GdFl グラデーション塗りつぶしの
    //  内容を追加情報ブロックへ書き戻す。値は UI で編集中のもの (LayerEditState) を採用するため、
    //  ツール内での編集がそのまま Photoshop 側の調整レイヤーとして往復する。
    //  バイナリ形式は psd-tools (adjustments.py) と照合済み。
    // ════════════════════════════════════════════════════════════════
    internal static class PSDAdjustmentInfoWriter
    {
        /// <summary>
        /// 本ツール製クリップ調整レイヤーを識別する追加情報キー (8BIM 追加情報ブロック)。
        ///
        /// ■ ブロック構造
        /// キー: "dPSE"
        /// データ: uint32 BE のバージョン番号のみ (現行 = 1)
        ///
        /// ■ 互換性ルール
        /// 1. パーサーは既知バージョン (1) のみ畳み戻しを行い、未知バージョンは通常の調整レイヤーとして扱います。
        /// 2. マーカーにデータ項目を追加するなど仕様変更を行う場合はバージョンを上げてください (古いパーサーが安全側へフォールバックするため)。
        /// 3. Photoshop 等の他ソフトは未知キー (dPSE) を保存時に破棄するため、外部編集されたファイルは
        ///    自動的に通常の調整レイヤーになります (畳み戻しは行われません)。
        /// </summary>
        internal const string ClipMarkerKey = "dPSE";

        /// <summary>クリップ調整レイヤーのマーカーブロック (version 1)。</summary>
        internal static ExportExtraBlock BuildClipMarkerBlock()
            => new ExportExtraBlock { Key = ClipMarkerKey, Data = new byte[] { 0, 0, 0, 1 } };

        /// <summary>
        /// レイヤーが書き戻すべき追加情報ブロック群を組み立てる。対象外・内容なしは null。
        /// 対象: ゼロ面積の調整レイヤー / SoCo / GdFl。ピクセルレイヤーの非破壊補正は
        /// PSDExportRecordBuilder がクリップ調整レイヤーとして書き出すため対象外。
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
                blocks.Add(EncodeBrit(layer.UI.Brightness, layer.UI.Contrast));
                blocks.Add(EncodeCgEd(layer.UI.Brightness, layer.UI.Contrast));
            }

            if (a.HasHueSaturation)
                blocks.Add(EncodeHue2(layer.UI.Hue, layer.UI.Saturation, layer.UI.Lightness, layer.UI.Colorize));

            if (a.HasInvert && layer.UI.Invert)
                blocks.Add(new ExportExtraBlock { Key = "nvrt", Data = new byte[0] });

            if (a.HasThreshold && layer.UI.ThresholdEnabled)
                blocks.Add(EncodeThrs(layer.UI.ThresholdLevel));

            if (a.HasPosterize && layer.UI.PosterizeEnabled)
                blocks.Add(EncodePost(layer.UI.PosterizeLevels));

            if (a.HasLevels && layer.UI.LevelsEnabled)
                blocks.Add(EncodeLevl(layer));

            if (a.HasCurves && layer.UI.CurveEnabled)
            {
                var b = EncodeCurv(layer);
                if (b != null) blocks.Add(b);
            }

            if (a.HasColorBalance && layer.UI.ColorBalanceEnabled)
                blocks.Add(EncodeBlnc(layer));

            if (a.HasGradientMap && layer.UI.GradientMapEnabled && layer.UI.Gradient != null)
                blocks.Add(EncodeGrdm(layer.UI.Gradient));

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
        internal static ExportExtraBlock EncodeBrit(float brightness, float contrast) => new ExportExtraBlock
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
        internal static ExportExtraBlock EncodeThrs(float level) => new ExportExtraBlock
        {
            Key = "thrs",
            Data = Build(w =>
            {
                w.WriteUInt16((ushort)RoundClamp(level, 1, 255));
                w.WriteUInt16(0); // パディング
            }),
        };

        // post: levels(2) + パディング(2)。version フィールドは存在しない
        internal static ExportExtraBlock EncodePost(float levels) => new ExportExtraBlock
        {
            Key = "post",
            Data = Build(w =>
            {
                w.WriteUInt16((ushort)RoundClamp(levels, 2, 255));
                w.WriteUInt16(0); // パディング
            }),
        };

        // blnc: (CR,MG,YB)×シャドウ/中間調/ハイライト の int16×9 + preserveLum(1) + パディング(1)
        internal static ExportExtraBlock EncodeBlnc(PSDLayer layer) => new ExportExtraBlock
        {
            Key = "blnc",
            Data = Build(w =>
            {
                WriteCbTriple(w, layer.UI.CBShadows);
                WriteCbTriple(w, layer.UI.CBMidtones);
                WriteCbTriple(w, layer.UI.CBHighlights);
                w.Write((byte)(layer.UI.CBPreserveLuminosity ? 1 : 0));
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
        // colorize=true では着色モードとして書く。ツール空間 (hue -180..180 / sat -100..100) から
        // Photoshop の着色レンジ (hue 0..360 / sat 0..100) へ変換する (パーサー側の逆変換)
        internal static ExportExtraBlock EncodeHue2(float hue, float saturation, float lightness, bool colorize) => new ExportExtraBlock
        {
            Key = "hue2",
            Data = Build(w =>
            {
                w.WriteUInt16(2);                    // version
                w.Write((byte)(colorize ? 1 : 0));   // colorization フラグ
                w.Write((byte)0);                    // パディング
                if (colorize)
                {
                    // colorization 用スロット (着色値)
                    int ph = Mathf.RoundToInt(hue);
                    ph = ((ph % 360) + 360) % 360;   // -180..180 → 0..360 の絶対色相
                    w.WriteInt16((short)ph);
                    w.WriteInt16(RoundClamp(saturation * 0.5f + 50f, 0, 100));
                    w.WriteInt16(RoundClamp(lightness, -100, 100));
                    // master 値 (未使用)
                    w.WriteInt16(0); w.WriteInt16(0); w.WriteInt16(0);
                }
                else
                {
                    // colorization 用スロット (未使用。Photoshop 既定値)
                    w.WriteInt16(0); w.WriteInt16(25); w.WriteInt16(0);
                    // master 値
                    w.WriteInt16(RoundClamp(hue,       -180, 180));
                    w.WriteInt16(RoundClamp(saturation, -100, 100));
                    w.WriteInt16(RoundClamp(lightness,  -100, 100));
                }
                // 6 色域レコード (レンジは既定値、補正値は 0)
                for (int i = 0; i < 6; i++)
                {
                    for (int j = 0; j < 4; j++) w.WriteInt16(Hue2DefaultRanges[i, j]);
                    w.WriteInt16(0); w.WriteInt16(0); w.WriteInt16(0);
                }
            }),
        };

        // levl: version(2, =2) + 10 バイトレコード × 29 ([0]=複合, [1..3]=R/G/B, 残りは恒等)
        internal static ExportExtraBlock EncodeLevl(PSDLayer layer) => new ExportExtraBlock
        {
            Key = "levl",
            Data = Build(w =>
            {
                w.WriteUInt16(2); // version
                WriteLevelRecord(w,
                    layer.UI.LevelsInputBlack, layer.UI.LevelsInputWhite,
                    layer.UI.LevelsOutputBlack, layer.UI.LevelsOutputWhite,
                    layer.UI.LevelsGamma);

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
        //       複合カーブは UI.Curve (編集値)、R/G/B はパース済み点列をそのまま書き戻す
        internal static ExportExtraBlock EncodeCurv(PSDLayer layer)
        {
            var composite = CurvePointsFrom(layer.UI.Curve);
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
        internal static ExportExtraBlock EncodeGrdm(Gradient gradient)
        {
            var colorKeys = gradient.colorKeys;
            var alphaKeys = gradient.alphaKeys;
            return new ExportExtraBlock
            {
                Key = "grdm",
                Data = Build(w =>
                {
                    w.WriteUInt16(1);  // version
                    w.Write((byte)0);  // reverse (UI.Gradient に反映済みのため常に 0)
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
        internal static ExportExtraBlock EncodeSoCo(Color color) => new ExportExtraBlock
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
        internal static ExportExtraBlock EncodeCgEd(float brightness, float contrast) => new ExportExtraBlock
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

    }
}
