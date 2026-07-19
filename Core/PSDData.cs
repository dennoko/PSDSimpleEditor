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
        public bool  HueColorize;   // hue2 の colorization フラグ (着色モード。値はツール空間へ変換済み)

        public bool  HasSolidColor;          // SoCo
        public Color SolidColor;

        public bool  HasInvert;              // invr (データなし、キー存在のみで判定)

        public bool  HasThreshold;           // thrs
        public float ThresholdLevel = 128f;  // 0 .. 255

        public bool  HasPosterize;           // post
        public float PosterizeLevels = 4f;   // 2 .. 255

        public bool  HasLevels;              // levl (複合チャンネル。UI で編集可能)
        public float LevelsInputBlack  = 0f;    // 0 .. 255
        public float LevelsInputWhite  = 255f;  // 0 .. 255
        public float LevelsGamma       = 1f;    // 0.01 .. 9.99
        public float LevelsOutputBlack = 0f;    // 0 .. 255
        public float LevelsOutputWhite = 255f;  // 0 .. 255

        // levl の R/G/B チャンネル別レコード (合成へ反映のみ。UI 編集対象外)
        public bool      HasChannelLevels;
        public Vector4[] LevelsChannelRanges; // [3] = R/G/B: (入力黒, 入力白, 出力黒, 出力白) 0..255
        public float[]   LevelsChannelGamma;  // [3] = R/G/B のガンマ

        public bool          HasCurves;      // curv (複合チャンネル。UI で編集可能)
        public List<Vector2> CurvePoints;    // (入力, 出力) 0..255 空間の制御点。null = 未設定

        // curv の R/G/B チャンネル別カーブ (合成へ反映のみ。UI 編集対象外)
        public bool            HasChannelCurves;
        public List<Vector2>[] CurveChannelPoints; // [3] = R/G/B。null 要素 = そのチャンネルは恒等

        public bool     HasGradientMap;      // grdm (PSD のグラデーションマップ調整レイヤー)
        public Gradient GradientMapGradient; // パース済みグラデーション (null = 未設定)

        // グラデーション塗りつぶしレイヤー (GdFl)。線形/円形のみ対応、他タイプは線形フォールバック
        public bool     HasGradientFill;
        public Gradient GradientFillGradient; // 反転 (Rvrs) は適用済み
        public float    GradientFillAngle;    // 度 (0 = 左→右, 90 = 下→上)
        public bool     GradientFillRadial;   // true = 円形
        public float    GradientFillScale = 1f; // Scl / 100 (0.1 .. 1.5 程度)

        public bool    HasColorBalance;      // blnc
        public Vector3 CBShadows;            // シャドウ (CR, MG, YB) 各 -100..100
        public Vector3 CBMidtones;           // 中間調
        public Vector3 CBHighlights;         // ハイライト
        public bool    CBPreserveLuminosity = true;

        /// <summary>いずれかの調整/塗りつぶしデータをパース済みか (調整レイヤー判定用)。</summary>
        public bool HasAny =>
            HasBrightnessContrast || HasHueSaturation || HasSolidColor || HasInvert ||
            HasThreshold || HasPosterize || HasLevels || HasCurves ||
            HasGradientMap || HasGradientFill || HasColorBalance;
    }

    // ─── レイヤーエフェクト (lfx2 / lrFX, best effort) ───────────────────────
    public class LayerEffects
    {
        public bool      HasColorOverlay;
        public Color     OverlayColor;
        public BlendMode OverlayBlendMode = BlendMode.Normal;
        public float     OverlayOpacity   = 1f;
    }

    // ─── レイヤーの編集状態 (ランタイム UI 状態) ─────────────────────────────
    // パース済みの PSD 値 (PSDLayer / AdjustmentData) を初期値として InitUIState が設定し、
    // 以降は Editor 側 (window) が編集する可変状態。パース結果とは分離して保持する。
    public class LayerEditState
    {
        // ── 表示・不透明度 ──
        public bool  Visible;
        public float Opacity;      // 0 .. 1

        // ── 色調補正 ──
        public float Brightness;   // -150 .. 150
        public float Contrast;     // -50  .. 100
        public float Hue;          // -180 .. 180
        public float Saturation;   // -100 .. 100
        public float Lightness;    // -100 .. 100

        // ── 階調反転・しきい値・ポスタリゼーション (非破壊。全ピクセルレイヤーに適用可) ──
        public bool  Invert;               // 階調反転 ON/OFF (invr はパラメータを持たないため有効フラグそのもの)
        public bool  ThresholdEnabled;
        public float ThresholdLevel = 128f;  // 0 .. 255
        public bool  PosterizeEnabled;
        public float PosterizeLevels = 4f;   // 2 .. 255

        // ── レベル補正 (非破壊。全ピクセルレイヤーに適用可。既定値は恒等変換) ──
        public bool  LevelsEnabled;
        public float LevelsInputBlack  = 0f;    // 0 .. 255
        public float LevelsInputWhite  = 255f;  // 0 .. 255
        public float LevelsGamma       = 1f;    // 0.01 .. 9.99
        public float LevelsOutputBlack = 0f;    // 0 .. 255
        public float LevelsOutputWhite = 255f;  // 0 .. 255

        // ── トーンカーブ (非破壊。全ピクセルレイヤーに適用可) ──
        public bool             CurveEnabled;
        public AnimationCurve   Curve;         // 既定: 直線 (0,0)-(1,1)
        public AnimationCurve[] CurveChannels; // [3] = R/G/B 個別カーブ (PSD 由来・UI 編集対象外)

        // ── 着色 (Colorize): ON で絶対値の色相・彩度を強制し、白黒 (彩度0) にも着色する ──
        public bool Colorize;

        // ── 画像クリップ合成 (非破壊。任意画像をレイヤーα形状へクリップ・タイリング・ブレンド) ──
        public bool      ImageClipEnabled;
        public Texture2D ImageClipTex;
        public Vector2   ImageClipTile = Vector2.one;  // タイル反復数 (X,Y)
        public BlendMode ImageClipBlend = BlendMode.Normal;
        public float     ImageClipOpacity = 1f;        // 0 .. 1

        // ── グラデーションマップ (非破壊。ピクセル輝度 → グラデーション色へ) ──
        public bool     GradientMapEnabled;
        public Gradient Gradient;            // 既定: 黒→白
        public float    GradientMapOpacity = 1f; // 0 .. 1
        public bool     GradientMapNormalize;    // true: 輝度を対象レイヤーの最暗〜最明で 0..1 に正規化

        // ── カラーバランス (非破壊。シャドウ/中間調/ハイライトごとの色味シフト) ──
        public bool    ColorBalanceEnabled;
        public Vector3 CBShadows;              // (CR, MG, YB) 各 -100..100
        public Vector3 CBMidtones;
        public Vector3 CBHighlights;
        public bool    CBPreserveLuminosity = true;

        // ── UI フォールドアウト状態 (色調補正セクションの開閉) ──
        public bool AdjustExpanded;

        // ── マスク生成 (色域選択マスク / 不透明範囲マスクの親セクション) ──
        public bool MaskGenExpanded;

        // ── 色域選択マスク (このレイヤー自身の画素から、対象色 ± 閾値で選択範囲を作り PNG 出力) ──
        public bool  ColorRangeExpanded;
        public Color ColorRangeTarget    = Color.white; // 対象色
        public float ColorRangeThreshold = 0.1f;        // 閾値 0..1 (RGB 正規化距離)

        /// <summary>
        /// いずれかの非破壊色調補正が実際に効いている状態か。
        /// パススルーグループが補正を持つ場合に分離合成へ切り替える判定に使う
        /// (子へ直接合成するパススルー経路では補正を適用できないため)。
        /// </summary>
        public bool HasActiveAdjustments =>
            !Mathf.Approximately(Brightness, 0f) ||
            !Mathf.Approximately(Contrast,   0f) ||
            !Mathf.Approximately(Hue,        0f) ||
            !Mathf.Approximately(Saturation, 0f) ||
            !Mathf.Approximately(Lightness,  0f) ||
            Colorize || Invert || ThresholdEnabled || PosterizeEnabled ||
            LevelsEnabled || CurveEnabled || ColorBalanceEnabled ||
            GradientMapEnabled;
    }

    // ─── レイヤーの描画用ランタイムキャッシュ ─────────────────────────────
    // パース結果ではなく、Editor 側 (window) が生成・更新・破棄する一時データ。
    // Texture2D は HideFlags.HideAndDontSave で生成し、レイヤー破棄時に明示破棄すること。
    public class LayerRuntime
    {
        public Texture2D CurveLut;         // トーンカーブ 256×1 焼き込み LUT
        public Texture2D GradientLut;      // グラデーションマップ 256×1 LUT
        public Texture2D GradientFillLut;  // GdFl グラデーション塗りつぶし 256×1 LUT (α 込み)
        public float     GradientLumMin = 0f;  // グラデーションマップ正規化レンジ
        public float     GradientLumMax = 1f;
    }

    // ─── PSD レイヤー ────────────────────────────────────────────────────────
    public class PSDLayer
    {
        // Undo 用のレイヤー識別子。初回アクセス時に自動生成される (遅延初期化)。
        // PSD パース後にアクセスされた時点で一意な ID が割り当てられ、以降は不変。
        private string _guid;
        public string Guid
        {
            get
            {
                if (string.IsNullOrEmpty(_guid))
                    _guid = System.Guid.NewGuid().ToString();
                return _guid;
            }
            set => _guid = value;
        }

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
        public bool      BlendClippedAsGroup = true; // clbl (クリップ群をグループとして先に合成。既定 = true)

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

        // ── ランタイム編集状態 (PSD 値で初期化。詳細は LayerEditState を参照) ──
        [System.NonSerialized] public LayerEditState UI = new LayerEditState();

        // ── 描画用ランタイムキャッシュ (Editor 側が管理。常に非 null) ──
        [System.NonSerialized] public LayerRuntime Runtime = new LayerRuntime();

        // ── 本ツール製クリップ調整レイヤーの識別 (追加情報キー dPSE) ──
        // 書き出し時にピクセルレイヤーの非破壊調整をクリップ調整レイヤーへ変換した印。
        // 読み込み時にベースレイヤーの 編集状態 (UI) へ畳み戻す対象かどうかの判定に使う。
        public bool IsToolAdjustmentClip;

        // ── ヘルパー ──
        /// <summary>調整レイヤー判定: ピクセルを持たず (ゼロ面積)、かつ何らかの調整キーを
        /// パース済みのレイヤー。キーを持たないゼロ面積レイヤーは「空のピクセルレイヤー」であり対象外。</summary>
        public bool IsAdjustmentLayer =>
            (Width <= 0 || Height <= 0) &&
            SectionType == LayerSectionType.Normal &&
            Adjustment != null && Adjustment.HasAny;

        // ── 内部 (テクスチャ構築後に null 化。ボトムアップ = Unity 標準向きで格納) ──
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
