using System;
using System.IO;
using System.Text;

namespace PSDSimpleEditor
{
    /// <summary>
    /// PSD ファイル (ビッグエンディアン格納) 用のバイナリライター。
    /// <see cref="BigEndianBinaryReader"/> の鏡像。整数のバイトスワップ書き込みと、
    /// Pascal 文字列 / Unicode (UTF-16BE) 文字列の書き込み、長さプレースホルダの
    /// バックパッチ (Position / Seek) を提供する。
    /// </summary>
    public class BigEndianBinaryWriter : BinaryWriter
    {
        public BigEndianBinaryWriter(Stream stream) : base(stream) { }

        // ─── ストリーム位置管理 (長さプレースホルダのバックパッチ用) ──────────

        /// <summary>現在のストリーム位置。</summary>
        public long Position
        {
            get { Flush(); return BaseStream.Position; }
            set { Flush(); BaseStream.Position = value; }
        }

        /// <summary>絶対位置へ移動する。</summary>
        public void Seek(long position)
        {
            Flush();
            BaseStream.Position = position;
        }

        // ─── ビッグエンディアン整数 ──────────────────────────────────────

        public void WriteUInt16(ushort value)
        {
            base.Write((byte)(value >> 8));
            base.Write((byte)(value & 0xFF));
        }

        public void WriteInt16(short value) => WriteUInt16((ushort)value);

        public void WriteUInt32(uint value)
        {
            base.Write((byte)(value >> 24));
            base.Write((byte)((value >> 16) & 0xFF));
            base.Write((byte)((value >> 8) & 0xFF));
            base.Write((byte)(value & 0xFF));
        }

        public void WriteInt32(int value) => WriteUInt32((uint)value);

        /// <summary>ビッグエンディアン 64bit 整数を書き込む。</summary>
        public void WriteInt64BE(long value)
        {
            WriteUInt32((uint)(value >> 32));
            WriteUInt32((uint)(value & 0xFFFFFFFF));
        }

        /// <summary>ビッグエンディアン IEEE754 double を書き込む (ディスクリプタの 'doub'/'UntF' 用)。</summary>
        public void WriteDoubleBE(double value)
        {
            WriteInt64BE(BitConverter.DoubleToInt64Bits(value));
        }

        // ─── バイト列・文字列 ────────────────────────────────────────────

        /// <summary>バイト列をそのまま書き込む。</summary>
        public void WriteBytesExact(byte[] bytes)
        {
            if (bytes != null && bytes.Length > 0)
                base.Write(bytes);
        }

        /// <summary>ASCII 文字列をそのまま書き込む (シグネチャ・4 文字キー用。長さは呼び出し側が保証)。</summary>
        public void WriteAscii(string s)
        {
            if (string.IsNullOrEmpty(s)) return;
            base.Write(Encoding.ASCII.GetBytes(s));
        }

        /// <summary>
        /// Pascal 文字列を書き込む。先頭 1 バイトが長さ (最大 255)、続いて本体。
        /// 全体 (1 + len) が padMultiple の倍数になるよう 0 パディングする。
        /// </summary>
        public void WritePascalString(string s, int padMultiple)
        {
            s = s ?? "";
            byte[] bytes;
            try { bytes = Encoding.GetEncoding(932).GetBytes(s); }
            catch { bytes = Encoding.ASCII.GetBytes(s); }
            if (bytes.Length > 255) Array.Resize(ref bytes, 255);

            base.Write((byte)bytes.Length);
            WriteBytesExact(bytes);

            int total = 1 + bytes.Length;
            int rem = total % padMultiple;
            if (rem != 0)
                for (int i = 0; i < padMultiple - rem; i++) base.Write((byte)0);
        }

        /// <summary>
        /// PSD の Unicode 文字列を書き込む。
        /// 形式: 文字数 (uint32, NUL 終端を含む) + UTF-16BE 文字列 + 終端 NUL (0x0000)。
        /// (Reader 側は末尾 NUL を TrimEnd するため往復で一致する。)
        /// </summary>
        public void WriteUnicodeString(string s)
        {
            s = s ?? "";
            uint count = (uint)(s.Length + 1); // 終端 NUL を含む文字数
            WriteUInt32(count);
            foreach (char c in s) WriteUInt16(c); // UTF-16BE (コードユニット単位)
            WriteUInt16(0);                        // 終端 NUL
        }
    }
}
