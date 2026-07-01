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

        public bool  HasInvert;              // invr (データなし、キー存在のみで判定)

        public bool  HasThreshold;           // thrs
        public float ThresholdLevel = 128f;  // 0 .. 255

        public bool  HasPosterize;           // post
        public float PosterizeLevels = 4f;   // 2 .. 255

        public bool  HasLevels;              // levl (複合/コンポジットチャンネルのみ v1 対応)
        public float LevelsInputBlack  = 0f;    // 0 .. 255
        public float LevelsInputWhite  = 255f;  // 0 .. 255
        public float LevelsGamma       = 1f;    // 0.01 .. 9.99
        public float LevelsOutputBlack = 0f;    // 0 .. 255
        public float LevelsOutputWhite = 255f;  // 0 .. 255

        public bool          HasCurves;      // curv (複合/コンポジットチャンネルのみ v1 対応)
        public List<Vector2> CurvePoints;    // (入力, 出力) 0..255 空間の制御点。null = 未設定

        public bool     HasGradientMap;      // grdm (PSD のグラデーションマップ調整レイヤー)
        public Gradient GradientMapGradient; // パース済みグラデーション (null = 未設定)

        public bool    HasColorBalance;      // blnc
        public Vector3 CBShadows;            // シャドウ (CR, MG, YB) 各 -100..100
        public Vector3 CBMidtones;           // 中間調
        public Vector3 CBHighlights;         // ハイライト
        public bool    CBPreserveLuminosity = true;
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

        // ── 階調反転・しきい値・ポスタリゼーション (非破壊。全ピクセルレイヤーに適用可) ──
        public bool  UIInvert;               // 階調反転 ON/OFF (invr はパラメータを持たないため有効フラグそのもの)
        public bool  UIThresholdEnabled;
        public float UIThresholdLevel = 128f;  // 0 .. 255
        public bool  UIPosterizeEnabled;
        public float UIPosterizeLevels = 4f;   // 2 .. 255

        // ── レベル補正 (非破壊。全ピクセルレイヤーに適用可。既定値は恒等変換) ──
        public bool  UILevelsEnabled;
        public float UILevelsInputBlack  = 0f;    // 0 .. 255
        public float UILevelsInputWhite  = 255f;  // 0 .. 255
        public float UILevelsGamma       = 1f;    // 0.01 .. 9.99
        public float UILevelsOutputBlack = 0f;    // 0 .. 255
        public float UILevelsOutputWhite = 255f;  // 0 .. 255

        // ── トーンカーブ (非破壊。全ピクセルレイヤーに適用可) ──
        [System.NonSerialized] public bool           UICurveEnabled;
        [System.NonSerialized] public AnimationCurve  UICurve;    // 既定: 直線 (0,0)-(1,1)
        [System.NonSerialized] public Texture2D       _curveLut;  // 256×1 焼き込み LUT (window が管理)

        // ── 着色 (Colorize): ON で絶対値の色相・彩度を強制し、白黒 (彩度0) にも着色する ──
        [System.NonSerialized] public bool UIColorize;

        // ── 画像クリップ合成 (非破壊。任意画像をレイヤーα形状へクリップ・タイリング・ブレンド) ──
        [System.NonSerialized] public bool      UIImageClipEnabled;
        [System.NonSerialized] public Texture2D UIImageClipTex;
        [System.NonSerialized] public Vector2   UIImageClipTile = Vector2.one;  // タイル反復数 (X,Y)
        [System.NonSerialized] public BlendMode UIImageClipBlend = BlendMode.Normal;
        [System.NonSerialized] public float     UIImageClipOpacity = 1f;        // 0 .. 1

        // ── グラデーションマップ (非破壊。ピクセル輝度 → グラデーション色へ) ──
        [System.NonSerialized] public bool      UIGradientMapEnabled;
        [System.NonSerialized] public Gradient  UIGradient;            // 既定: 黒→白
        [System.NonSerialized] public float     UIGradientMapOpacity = 1f; // 0 .. 1
        [System.NonSerialized] public Texture2D _gradientLut;          // 256×1 焼き込み LUT (window が管理)
        [System.NonSerialized] public bool      UIGradientMapNormalize;    // true: 輝度を対象レイヤーの最暗〜最明で 0..1 に正規化
        [System.NonSerialized] public float     _gradientLumMin = 0f;      // 正規化用レンジ (window が非透明画素から計算)
        [System.NonSerialized] public float     _gradientLumMax = 1f;

        // ── カラーバランス (非破壊。シャドウ/中間調/ハイライトごとの色味シフト) ──
        public bool    UIColorBalanceEnabled;
        public Vector3 UICBShadows;              // (CR, MG, YB) 各 -100..100
        public Vector3 UICBMidtones;
        public Vector3 UICBHighlights;
        public bool    UICBPreserveLuminosity = true;

        // ── UI フォールドアウト状態 (色調補正セクションの開閉) ──
        [System.NonSerialized] public bool UIAdjustExpanded;

        // ── 色域選択マスク (このレイヤー自身の画素から、対象色 ± 閾値で選択範囲を作り PNG 出力) ──
        [System.NonSerialized] public bool  UIColorRangeExpanded;
        [System.NonSerialized] public Color UIColorRangeTarget    = Color.white; // 対象色
        [System.NonSerialized] public float UIColorRangeThreshold = 0.1f;        // 閾値 0..1 (RGB 正規化距離)

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
