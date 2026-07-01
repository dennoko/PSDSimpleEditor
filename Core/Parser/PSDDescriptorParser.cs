using System.Collections.Generic;
using System.IO;

namespace PSDSimpleEditor
{
    // ════════════════════════════════════════════════════════════════
    //  最小ディスクリプタパーサー (SoCo / CgEd / lfx2 用)
    // ════════════════════════════════════════════════════════════════
    //  目的キーの抽出のみが目標。未知の OSType に遭遇したら DescriptorException を
    //  投げ、呼び出し側 (HandleAdditionalInfo の try/finally) が警告 + dataEnd への
    //  seek で回収するため、ストリーム位置は壊れない。
    internal static class PSDDescriptorParser
    {
        internal class DescriptorException : IOException
        {
            public DescriptorException(string message) : base(message) { }
        }

        const int MaxDescriptorDepth = 24;
        const int MaxDescriptorItems = 4096;

        /// <summary>ディスクリプタを Dictionary&lt;キー文字列, object&gt; として読む。
        /// 値: double / long / bool / string (TEXT・enum 値) / Dictionary (Objc) / List (VlLs) / null (スキップ型)。</summary>
        internal static Dictionary<string, object> ParseDescriptor(BigEndianBinaryReader r, int depth)
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
        internal static Dictionary<string, object> GetChildDescriptor(Dictionary<string, object> dict, string key)
        {
            if (dict != null && dict.TryGetValue(key, out object v))
                return v as Dictionary<string, object>;
            return null;
        }

        /// <summary>ディスクリプタから数値 (doub / long / UntF / bool) を取得する。</summary>
        internal static bool TryGetNumber(Dictionary<string, object> dict, string key, out double value)
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
    }
}
