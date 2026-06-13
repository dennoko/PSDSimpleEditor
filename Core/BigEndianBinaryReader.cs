using System;
using System.IO;
using System.Text;

namespace PSDSimpleEditor
{
    /// <summary>
    /// PSD ファイル (ビッグエンディアン格納) 用のバイナリリーダー。
    /// 整数のバイトスワップ読み取りと、Pascal 文字列 / Unicode (UTF-16BE) 文字列の
    /// 読み取り、ストリーム位置管理ヘルパー (Position / Seek / Skip) を提供する。
    /// </summary>
    public class BigEndianBinaryReader : BinaryReader
    {
        // Pascal 文字列のデコードに使うエンコーディング。
        // 日本語環境の PSD では Shift-JIS のことが多いため、利用可能なら CP932 を使う。
        // (Unicode レイヤー名は luni ブロックで上書きされるため、ここは best effort で良い)
        static readonly Encoding PascalEncoding;

        static BigEndianBinaryReader()
        {
            Encoding enc;
            try { enc = Encoding.GetEncoding(932); }   // Shift-JIS
            catch { enc = Encoding.UTF8; }             // 環境的に使えなければ UTF-8 で代替
            PascalEncoding = enc;
        }

        public BigEndianBinaryReader(Stream stream) : base(stream) { }

        // ─── ストリーム位置管理 ──────────────────────────────────────────

        /// <summary>現在のストリーム位置。</summary>
        public long Position
        {
            get => BaseStream.Position;
            set => BaseStream.Position = value;
        }

        /// <summary>ストリーム全長。</summary>
        public long Length => BaseStream.Length;

        /// <summary>絶対位置へ移動する。境界 seek (end = pos + len パターン) に使用する。</summary>
        public void Seek(long position)
        {
            BaseStream.Position = position;
        }

        /// <summary>現在位置から指定バイト数だけ読み飛ばす。</summary>
        public void Skip(long count)
        {
            if (count != 0)
                BaseStream.Position += count;
        }

        // ─── ビッグエンディアン整数 ──────────────────────────────────────

        public override ushort ReadUInt16()
        {
            int b0 = base.ReadByte();
            int b1 = base.ReadByte();
            return (ushort)((b0 << 8) | b1);
        }

        public override short ReadInt16() => (short)ReadUInt16();

        public override uint ReadUInt32()
        {
            uint hi = ReadUInt16();
            uint lo = ReadUInt16();
            return (hi << 16) | lo;
        }

        public override int ReadInt32() => (int)ReadUInt32();

        /// <summary>ビッグエンディアン 64bit 整数を読み取る (ディスクリプタの 'comp' 用)。</summary>
        public long ReadInt64BE()
        {
            long hi = ReadUInt32();
            long lo = ReadUInt32();
            return (hi << 32) | lo;
        }

        /// <summary>ビッグエンディアン IEEE754 double を読み取る (ディスクリプタの 'doub'/'UntF' 用)。</summary>
        public double ReadDoubleBE()
        {
            return BitConverter.Int64BitsToDouble(ReadInt64BE());
        }

        // ─── バイト列・文字列 ────────────────────────────────────────────

        /// <summary>
        /// 指定バイト数を必ず読み取る。不足していたら EndOfStreamException を投げる
        /// (BinaryReader.ReadBytes は不足時に短い配列を黙って返すため)。
        /// </summary>
        public byte[] ReadBytesExact(int count)
        {
            if (count < 0)
                throw new IOException($"読み取りバイト数が不正です: {count}");
            byte[] bytes = ReadBytes(count);
            if (bytes.Length != count)
                throw new EndOfStreamException("ファイル終端を超えて読み取ろうとしました");
            return bytes;
        }

        /// <summary>指定バイト数を ASCII 文字列として読み取る (シグネチャ・キー用)。</summary>
        public string ReadAscii(int length)
        {
            return Encoding.ASCII.GetString(ReadBytesExact(length));
        }

        /// <summary>
        /// Pascal 文字列を読み取る。先頭 1 バイトが長さ、続いて文字列本体。
        /// 全体 (1 + len) が padMultiple の倍数になるようパディングを読み飛ばす。
        /// レイヤーレコード内では padMultiple = 4。
        /// </summary>
        public string ReadPascalString(int padMultiple)
        {
            byte len = base.ReadByte();
            string s;
            if (len == 0)
            {
                s = "";
            }
            else
            {
                byte[] bytes = ReadBytesExact(len);
                try { s = PascalEncoding.GetString(bytes); }
                catch { s = Encoding.ASCII.GetString(bytes); }
            }
            int total = 1 + len;
            int rem = total % padMultiple;
            if (rem != 0)
                Skip(padMultiple - rem);
            return s;
        }

        /// <summary>
        /// PSD の Unicode 文字列を読み取る。
        /// 形式: 文字数 (uint32) + UTF-16BE 文字列 (文字数 × 2 バイト)。
        /// luni ブロックやディスクリプタの 'TEXT' で使用。末尾の NUL は除去する。
        /// </summary>
        public string ReadUnicodeString()
        {
            uint count = ReadUInt32();
            return ReadUtf16BE((int)count);
        }

        /// <summary>UTF-16BE 文字列を指定文字数 (コードユニット数) ぶん読み取る。</summary>
        public string ReadUtf16BE(int charCount)
        {
            if (charCount <= 0) return "";
            if (charCount > 4 * 1024 * 1024)
                throw new IOException($"Unicode 文字列長が不正です: {charCount}");
            byte[] bytes = ReadBytesExact(charCount * 2);
            return Encoding.BigEndianUnicode.GetString(bytes).TrimEnd('\0');
        }
    }
}
