namespace PSDSimpleEditor
{
    // ════════════════════════════════════════════════════════════════
    //  レイヤーレコードのバイナリ書き込み
    // ════════════════════════════════════════════════════════════════
    internal static class PSDLayerRecordWriter
    {
        internal static void WriteLayerRecord(BigEndianBinaryWriter w, ExportRecord rec)
        {
            w.WriteInt32(rec.Top);
            w.WriteInt32(rec.Left);
            w.WriteInt32(rec.Bottom);
            w.WriteInt32(rec.Right);

            w.WriteUInt16((ushort)rec.Channels.Count);
            foreach (var ch in rec.Channels)
            {
                w.WriteInt16(ch.Id);
                w.WriteUInt32((uint)ch.Data.Length);
            }

            w.WriteAscii("8BIM");
            w.WriteAscii(rec.BlendKey);
            w.Write(rec.Opacity);
            w.Write(rec.Clipping);
            w.Write(rec.Flags);
            w.Write((byte)0); // filler

            // ── エクストラデータ ──
            long extraLenPos = w.Position;
            w.WriteUInt32(0); // プレースホルダ
            long extraStart = w.Position;

            // マスクデータ
            if (rec.HasMask)
            {
                w.WriteUInt32(20);
                w.WriteInt32(rec.MaskTop);
                w.WriteInt32(rec.MaskLeft);
                w.WriteInt32(rec.MaskBottom);
                w.WriteInt32(rec.MaskRight);
                w.Write(rec.MaskDefaultColor);
                w.Write((byte)(rec.MaskDisabled ? 0x02 : 0x00)); // bit0=0 → 絶対座標
            }
            else
            {
                w.WriteUInt32(0);
            }

            // ブレンディングレンジ (なし)
            w.WriteUInt32(0);

            // Pascal 名 (4 バイト境界パディング)
            w.WritePascalString(rec.Name, 4);

            // 追加情報: luni (Unicode 名)
            WriteAdditionalLuni(w, rec.Name);

            // 追加情報: lsct (グループ/終端マーカー)
            if (rec.LsctType != 0)
                WriteAdditionalLsct(w, rec);

            PSDWriter.Backpatch(w, extraLenPos, (uint)(w.Position - extraStart));
        }

        static void WriteAdditionalLuni(BigEndianBinaryWriter w, string name)
        {
            w.WriteAscii("8BIM");
            w.WriteAscii("luni");
            long lenPos = w.Position;
            w.WriteUInt32(0);
            long start = w.Position;
            w.WriteUnicodeString(name ?? "");
            PSDWriter.Backpatch(w, lenPos, (uint)(w.Position - start));
        }

        static void WriteAdditionalLsct(BigEndianBinaryWriter w, ExportRecord rec)
        {
            w.WriteAscii("8BIM");
            w.WriteAscii("lsct");
            if (rec.LsctType == 3)
            {
                // 終端マーカー: type のみ
                w.WriteUInt32(4);
                w.WriteUInt32(3);
            }
            else
            {
                // フォルダ: type + 8BIM + ブレンドキー
                w.WriteUInt32(12);
                w.WriteUInt32((uint)rec.LsctType);
                w.WriteAscii("8BIM");
                w.WriteAscii(rec.GroupBlendKey ?? "pass");
            }
        }
    }
}
