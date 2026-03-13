using System;
using System.IO;
using System.Text;

namespace PSDSimpleEditor
{
    /// <summary>
    /// BinaryReader を拡張し、PSD ファイル仕様のビッグエンディアン整数を読み取る。
    /// Windows (リトルエンディアン) 環境でのバイトスワップ処理を内包する。
    /// </summary>
    public class BigEndianBinaryReader : BinaryReader
    {
        public BigEndianBinaryReader(Stream stream) : base(stream) { }

        public override short ReadInt16()
        {
            var b = ReadBytes(2);
            Array.Reverse(b);
            return BitConverter.ToInt16(b, 0);
        }

        public override ushort ReadUInt16()
        {
            var b = ReadBytes(2);
            Array.Reverse(b);
            return BitConverter.ToUInt16(b, 0);
        }

        public override int ReadInt32()
        {
            var b = ReadBytes(4);
            Array.Reverse(b);
            return BitConverter.ToInt32(b, 0);
        }

        public override uint ReadUInt32()
        {
            var b = ReadBytes(4);
            Array.Reverse(b);
            return BitConverter.ToUInt32(b, 0);
        }

        /// <summary>指定バイト数を ASCII 文字列として読み取る。</summary>
        public string ReadString(int length)
        {
            return Encoding.ASCII.GetString(ReadBytes(length));
        }

        /// <summary>
        /// Pascal 文字列を読み取る。
        /// 先頭 1 バイトが文字列長、続いて文字列本体、全体が偶数バイトになるようパディング。
        /// </summary>
        public string ReadPascalString()
        {
            byte len = ReadByte();
            string s  = len == 0 ? "" : Encoding.ASCII.GetString(ReadBytes(len));
            // 合計 (1 + len) バイト、偶数でなければパディングバイトをスキップ
            if ((1 + len) % 2 != 0)
                ReadByte();
            return s;
        }
    }
}
