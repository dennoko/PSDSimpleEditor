namespace PSDSimpleEditor
{
    // ════════════════════════════════════════════════════════════════
    //  最小ディスクリプタライター (PSDDescriptorParser の逆)
    //
    //  ディスクリプタ形式の追加情報キー (SoCo / CgEd / GdFl) が使う
    //  Photoshop ディスクリプタ構造の書き込みプリミティブ。
    //  PSDDescriptorParser が読める形式のみをサポートする。
    // ════════════════════════════════════════════════════════════════
    internal static class PSDDescriptorWriter
    {
        internal static void WriteDescriptorHeader(BigEndianBinaryWriter w, string classId, int itemCount)
        {
            w.WriteUnicodeString("");      // クラス名 (Unicode) — 空
            WriteId(w, classId);           // クラス ID
            w.WriteUInt32((uint)itemCount);
        }

        // 4 文字 ID は長さ 0 + 4 文字、それ以外は長さ + 文字列
        internal static void WriteId(BigEndianBinaryWriter w, string id)
        {
            if (id.Length == 4) { w.WriteUInt32(0); }
            else                { w.WriteUInt32((uint)id.Length); }
            w.WriteAscii(id);
        }

        internal static void WriteKeyLong(BigEndianBinaryWriter w, string key, int value)
        {
            WriteId(w, key);
            w.WriteAscii("long");
            w.WriteInt32(value);
        }

        internal static void WriteKeyBool(BigEndianBinaryWriter w, string key, bool value)
        {
            WriteId(w, key);
            w.WriteAscii("bool");
            w.Write((byte)(value ? 1 : 0));
        }

        internal static void WriteKeyDouble(BigEndianBinaryWriter w, string key, double value)
        {
            WriteId(w, key);
            w.WriteAscii("doub");
            w.WriteDoubleBE(value);
        }

        internal static void WriteKeyUnitFloat(BigEndianBinaryWriter w, string key, string unit, double value)
        {
            WriteId(w, key);
            w.WriteAscii("UntF");
            w.WriteAscii(unit);
            w.WriteDoubleBE(value);
        }

        internal static void WriteKeyEnum(BigEndianBinaryWriter w, string key, string enumType, string enumValue)
        {
            WriteId(w, key);
            w.WriteAscii("enum");
            WriteId(w, enumType);
            WriteId(w, enumValue);
        }

        internal static void WriteKeyText(BigEndianBinaryWriter w, string key, string text)
        {
            WriteId(w, key);
            w.WriteAscii("TEXT");
            w.WriteUnicodeString(text);
        }

        /// <summary>ネストディスクリプタ (Objc) の開始。itemCount 個の WriteKey* が続くこと。</summary>
        internal static void WriteKeyObject(BigEndianBinaryWriter w, string key, string classId, int itemCount)
        {
            WriteId(w, key);
            w.WriteAscii("Objc");
            WriteDescriptorHeader(w, classId, itemCount);
        }

        /// <summary>リスト (VlLs) の開始。count 個のリスト要素が続くこと。</summary>
        internal static void WriteKeyListStart(BigEndianBinaryWriter w, string key, int count)
        {
            WriteId(w, key);
            w.WriteAscii("VlLs");
            w.WriteUInt32((uint)count);
        }

        /// <summary>リスト要素としての Objc の開始。itemCount 個の WriteKey* が続くこと。</summary>
        internal static void WriteListObjectStart(BigEndianBinaryWriter w, string classId, int itemCount)
        {
            w.WriteAscii("Objc");
            WriteDescriptorHeader(w, classId, itemCount);
        }
    }
}
