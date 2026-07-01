using System.IO;
using System.Text;
using UnityEngine;

namespace PSDSimpleEditor
{
    // ════════════════════════════════════════════════════════════════
    //  Section 1: ヘッダ / Section 2・3: 長さプレフィックス付きブロックのスキップ
    // ════════════════════════════════════════════════════════════════
    internal static class PSDHeaderReader
    {
        internal static void ParseHeader(BigEndianBinaryReader r, PSDFile psd, StringBuilder vlog)
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

        internal static bool IsLayerSupportedColorMode(ushort mode) => mode == 3 /*RGB*/ || mode == 1 /*Grayscale*/;

        internal static string ColorModeName(ushort mode)
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

        internal static void SkipLengthPrefixedBlock(BigEndianBinaryReader r)
        {
            uint len = r.ReadUInt32();
            r.Skip(len);
        }
    }
}
