using System.Collections.Generic;
using UnityEngine;

namespace PSDSimpleEditor
{
    // ─── ブレンドモード (PSD 仕様の全モードを網羅) ──────────────────────────
    // int 値はシェーダーの分岐番号と 1:1 対応している。変更禁止 (REWRITE_SPEC.md §2)。
    public enum BlendMode
    {
        Normal       = 0,   // norm
        Multiply     = 1,   // mul(SP)
        Screen       = 2,   // scrn
        Overlay      = 3,   // over
        Dissolve     = 4,   // diss
        Darken       = 5,   // dark
        ColorBurn    = 6,   // idiv
        LinearBurn   = 7,   // lbrn
        DarkerColor  = 8,   // dkCl
        Lighten      = 9,   // lite
        ColorDodge   = 10,  // div(SP)
        LinearDodge  = 11,  // lddg (Add)
        LighterColor = 12,  // lgCl
        SoftLight    = 13,  // sLit
        HardLight    = 14,  // hLit
        VividLight   = 15,  // vLit
        LinearLight  = 16,  // lLit
        PinLight     = 17,  // pLit
        HardMix      = 18,  // hMix
        Difference   = 19,  // diff
        Exclusion    = 20,  // smud
        Subtract     = 21,  // fsub
        Divide       = 22,  // fdiv
        Hue          = 23,  // hue(SP)
        Saturation   = 24,  // sat(SP)
        Color        = 25,  // colr
        Luminosity   = 26,  // lum(SP)
        PassThrough  = 27,  // pass (グループ専用)
        Unknown      = 99   // 未知キー (合成時は Normal 扱い)
    }

    // ─── レイヤーセクション種別 (グループ判定用) ────────────────────────────
    public enum LayerSectionType
    {
        Normal,      // 通常のピクセル/調整レイヤー
        GroupBegin,  // フォルダ (lsct type 1=開, 2=閉)
        GroupEnd     // セクション終端マーカー (lsct type 3, ツリーには含めない)
    }

    // ─── チャンネル情報 ──────────────────────────────────────────────────────
    public class ChannelInfo
    {
        public short ChannelId;   // 0=R 1=G 2=B -1=Alpha -2=ユーザーマスク -3=ベクターマスク
        public int   DataLength;  // 圧縮種別 2 バイトを含む全長
    }

    // ─── 色調補正パラメータ ──────────────────────────────────────────────────
    public class AdjustmentData
    {
        public bool  HasBrightnessContrast;  // brit / CgEd
        public float Brightness;    // -150 .. 150
        public float Contrast;      // -50  .. 100

        public bool  HasHueSaturation;       // hue2
        public float Hue;           // -180 .. 180
        public float Saturation;    // -100 .. 100
        public float Lightness;     // -100 .. 100

        public bool  HasSolidColor;          // SoCo
        public Color SolidColor;
    }

    // ─── レイヤーエフェクト (lfx2 / lrFX, best effort) ───────────────────────
    public class LayerEffects
    {
        public bool      HasColorOverlay;
        public Color     OverlayColor;
        public BlendMode OverlayBlendMode = BlendMode.Normal;
        public float     OverlayOpacity   = 1f;
    }

    // ─── PSD レイヤー ────────────────────────────────────────────────────────
    public class PSDLayer
    {
        // ── バウンディングボックス (PSD 左上原点・キャンバス絶対座標) ──
        public int Top, Left, Bottom, Right;
        public int Width  => Right  - Left;
        public int Height => Bottom - Top;

        // ── チャンネルリスト ──
        public List<ChannelInfo> Channels = new List<ChannelInfo>();

        // ── 合成パラメータ ──
        public BlendMode BlendMode  = BlendMode.Normal;
        public string    BlendKeyRaw = "norm";  // デバッグ用の生キー
        public byte      Opacity    = 255;
        public bool      IsVisible  = true;     // flags bit1 == 0
        public bool      IsClipping;            // clipping byte != 0 (直下のベース層でクリップ)

        // ── レイヤー識別 ──
        public string Name = "";                // luni (UTF-16BE) を優先、無ければ Pascal 名
        public LayerSectionType SectionType = LayerSectionType.Normal;

        // ── グループ (SectionType == GroupBegin のみ有効) ──
        public List<PSDLayer> Children;          // null = 非グループ。index 0 = 最下層
        public BlendMode      GroupBlendMode = BlendMode.PassThrough;
        public bool           IsExpanded    = true;

        // ── 色調補正・エフェクト ──
        public AdjustmentData Adjustment = new AdjustmentData();
        public LayerEffects   Effects;           // null = エフェクトなし

        // ── レイヤーマスク (矩形はキャンバス絶対座標に変換済み) ──
        public bool      HasMask;
        public int       MaskTop, MaskLeft, MaskBottom, MaskRight;
        public byte      MaskDefaultColor;  // 矩形外の値 0=黒(非表示), 255=白(表示)
        public bool      MaskIsDisabled;    // mask flags bit1
        public Texture2D MaskTexture;       // R8, 上下反転済み

        // ── Unity テクスチャ (RGBA32, 上下反転済み = Unity 標準向き) ──
        public Texture2D Texture;

        // ── ランタイム UI 状態 (PSD 値で初期化) ──
        public bool  UIVisible;
        public float UIOpacity;      // 0 .. 1
        public float UIBrightness;   // -150 .. 150
        public float UIContrast;     // -50  .. 100
        public float UIHue;          // -180 .. 180
        public float UISaturation;   // -100 .. 100
        public float UILightness;    // -100 .. 100

        // ── ヘルパー ──
        public bool IsAdjustmentLayer => Width <= 0 || Height <= 0;

        // ── 内部 (テクスチャ構築後に null 化) ──
        [System.NonSerialized] public byte[] _rawPixels;
        [System.NonSerialized] public byte[] _rawMaskPixels;
    }

    // ─── PSD ファイル ─────────────────────────────────────────────────────────
    public class PSDFile
    {
        public ushort Version;
        public ushort Channels;
        public int    Height;
        public int    Width;
        public ushort BitDepth;           // 8 / 16
        public ushort ColorMode;          // 1=Gray 3=RGB 4=CMYK 9=LAB
        public List<PSDLayer> Layers = new List<PSDLayer>();  // index 0 = 最下層
        public Texture2D MergedComposite; // Section 5 の統合済み画像 (失敗時 null)
    }
}
