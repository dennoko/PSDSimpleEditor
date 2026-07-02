using System.Collections.Generic;

namespace PSDSimpleEditor
{
    // ── 書き出し用に準備した 1 チャンネル分 (圧縮タグ込み) ──
    internal class ExportChannel
    {
        public short  Id;    // 0=R 1=G 2=B -1=A -2=Mask
        public byte[] Data;  // [uint16 compression][rowLens..][packbits rows..]
    }

    // ── 書き出し用の追加情報ブロック (調整キー等。luni/lsct の後に書かれる) ──
    internal class ExportExtraBlock
    {
        public string Key;   // 4 文字キー (brit / hue2 / SoCo / GdFl ...)
        public byte[] Data;  // ブロック本体 (奇数長の偶数化パディングはライターが行う)
    }

    // ── 書き出し用に準備した 1 レコード分 ──
    internal class ExportRecord
    {
        public int Top, Left, Bottom, Right;
        public List<ExportChannel> Channels = new List<ExportChannel>();
        public string BlendKey = "norm";
        public byte   Opacity  = 255;
        public byte   Clipping;
        public byte   Flags;          // bit1 = 非表示
        public string Name = "";

        public int    LsctType;       // 0=通常 1=開フォルダ 2=閉フォルダ 3=終端マーカー
        public string GroupBlendKey;  // フォルダの lsct ブレンドキー (type1/2 のみ)

        public List<ExportExtraBlock> ExtraBlocks; // 調整キー等の追加情報 (null = なし)

        public bool HasMask;
        public int  MaskTop, MaskLeft, MaskBottom, MaskRight;
        public byte MaskDefaultColor;
        public bool MaskDisabled;
    }
}
