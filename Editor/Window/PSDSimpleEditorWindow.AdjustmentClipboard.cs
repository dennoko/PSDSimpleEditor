using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace PSDSimpleEditor
{
    // ── 調整パラメーターのコピー&ペースト: 種類ごとに直近1件を保持する単純なクリップボード ──
    // ─── partial 見取り図 ───────────────────────────────────────────
    // 責務   : 補正パラメータ（色調補正/トーンカーブ等）のコピー＆ペーストクリップボード機能
    // 宣言   : ClipboardKind, 各種 Snapshot 構造体
    // 参照   : _needsRecomposite (RW)
    // 依存   : RowSpace (.LayerPanel.cs), AdjustmentLutBaker (LUT ベイク処理)
    // ────────────────────────────────────────────────────────────────
    public partial class PSDSimpleEditorWindow
    {
        internal enum ClipboardKind
        {
            FullAdjustmentSection,
            Colorize,
            Invert,
            Threshold,
            Posterize,
            Levels,
            ColorBalance,
            Curve,
            GradientMap,
            ImageClip,
            ColorRangeMask,
        }

        // ── スナップショット型 (種類ごとに必要なフィールドのみを保持) ──
        struct ColorizeSnapshot   { public bool Value; }
        struct InvertSnapshot     { public bool Value; }
        struct ThresholdSnapshot  { public bool Enabled; public float Level; }
        struct PosterizeSnapshot  { public bool Enabled; public float Levels; }

        struct LevelsSnapshot
        {
            public bool  Enabled;
            public float InputBlack, InputWhite, Gamma, OutputBlack, OutputWhite;
        }

        struct ColorBalanceSnapshot
        {
            public bool    Enabled;
            public Vector3 Shadows, Midtones, Highlights;
            public bool    PreserveLuminosity;
        }

        class CurveSnapshot
        {
            public bool           Enabled;
            public AnimationCurve Curve;
        }

        class GradientMapSnapshot
        {
            public bool     Enabled;
            public Gradient Gradient;
            public float    Opacity;
            public bool     Normalize;
        }

        class ImageClipSnapshot
        {
            public bool      Enabled;
            public Texture2D Tex;
            public Vector2   Tile;
            public BlendMode Blend;
            public float     Opacity;
        }

        struct ColorRangeMaskSnapshot { public Color Target; public float Threshold; }

        class FullAdjustmentSnapshot
        {
            public float Brightness, Contrast, Hue, Saturation, Lightness;
            public bool  Colorize, Invert;
            public ThresholdSnapshot     Threshold;
            public PosterizeSnapshot     Posterize;
            public LevelsSnapshot        Levels;
            public ColorBalanceSnapshot  ColorBalance;
            public CurveSnapshot         Curve;
            public GradientMapSnapshot   GradientMap;
            public ImageClipSnapshot     ImageClip;
        }

        static readonly Dictionary<ClipboardKind, object> _adjustmentClipboard = new Dictionary<ClipboardKind, object>();

        static bool HasClipboard(ClipboardKind kind) => _adjustmentClipboard.ContainsKey(kind);

        static AnimationCurve CloneCurve(AnimationCurve src) =>
            src != null ? new AnimationCurve(src.keys) : AdjustmentLutBaker.CreateDefaultCurve();

        static Gradient CloneGradient(Gradient src)
        {
            var g = new Gradient();
            if (src != null) g.SetKeys(src.colorKeys, src.alphaKeys);
            else             return AdjustmentLutBaker.CreateDefaultGradient();
            return g;
        }

        static CurveSnapshot CopyCurveSnapshot(PSDLayer layer) =>
            new CurveSnapshot { Enabled = layer.UI.CurveEnabled, Curve = CloneCurve(layer.UI.Curve) };

        static GradientMapSnapshot CopyGradientMapSnapshot(PSDLayer layer) =>
            new GradientMapSnapshot
            {
                Enabled   = layer.UI.GradientMapEnabled,
                Gradient  = CloneGradient(layer.UI.Gradient),
                Opacity   = layer.UI.GradientMapOpacity,
                Normalize = layer.UI.GradientMapNormalize,
            };

        static ImageClipSnapshot CopyImageClipSnapshot(PSDLayer layer) =>
            new ImageClipSnapshot
            {
                Enabled = layer.UI.ImageClipEnabled,
                Tex     = layer.UI.ImageClipTex,
                Tile    = layer.UI.ImageClipTile,
                Blend   = layer.UI.ImageClipBlend,
                Opacity = layer.UI.ImageClipOpacity,
            };

        /// <summary>指定した種類の調整パラメーターを layer からクリップボードへコピーする。</summary>
        void CopyAdjustment(ClipboardKind kind, PSDLayer layer)
        {
            switch (kind)
            {
                case ClipboardKind.Colorize:
                    _adjustmentClipboard[kind] = new ColorizeSnapshot { Value = layer.UI.Colorize };
                    break;
                case ClipboardKind.Invert:
                    _adjustmentClipboard[kind] = new InvertSnapshot { Value = layer.UI.Invert };
                    break;
                case ClipboardKind.Threshold:
                    _adjustmentClipboard[kind] = new ThresholdSnapshot { Enabled = layer.UI.ThresholdEnabled, Level = layer.UI.ThresholdLevel };
                    break;
                case ClipboardKind.Posterize:
                    _adjustmentClipboard[kind] = new PosterizeSnapshot { Enabled = layer.UI.PosterizeEnabled, Levels = layer.UI.PosterizeLevels };
                    break;
                case ClipboardKind.Levels:
                    _adjustmentClipboard[kind] = new LevelsSnapshot
                    {
                        Enabled     = layer.UI.LevelsEnabled,
                        InputBlack  = layer.UI.LevelsInputBlack,
                        InputWhite  = layer.UI.LevelsInputWhite,
                        Gamma       = layer.UI.LevelsGamma,
                        OutputBlack = layer.UI.LevelsOutputBlack,
                        OutputWhite = layer.UI.LevelsOutputWhite,
                    };
                    break;
                case ClipboardKind.ColorBalance:
                    _adjustmentClipboard[kind] = new ColorBalanceSnapshot
                    {
                        Enabled            = layer.UI.ColorBalanceEnabled,
                        Shadows            = layer.UI.CBShadows,
                        Midtones           = layer.UI.CBMidtones,
                        Highlights         = layer.UI.CBHighlights,
                        PreserveLuminosity = layer.UI.CBPreserveLuminosity,
                    };
                    break;
                case ClipboardKind.Curve:
                    _adjustmentClipboard[kind] = CopyCurveSnapshot(layer);
                    break;
                case ClipboardKind.GradientMap:
                    _adjustmentClipboard[kind] = CopyGradientMapSnapshot(layer);
                    break;
                case ClipboardKind.ImageClip:
                    _adjustmentClipboard[kind] = CopyImageClipSnapshot(layer);
                    break;
                case ClipboardKind.ColorRangeMask:
                    _adjustmentClipboard[kind] = new ColorRangeMaskSnapshot { Target = layer.UI.ColorRangeTarget, Threshold = layer.UI.ColorRangeThreshold };
                    break;
                case ClipboardKind.FullAdjustmentSection:
                    _adjustmentClipboard[kind] = new FullAdjustmentSnapshot
                    {
                        Brightness   = layer.UI.Brightness,
                        Contrast     = layer.UI.Contrast,
                        Hue          = layer.UI.Hue,
                        Saturation   = layer.UI.Saturation,
                        Lightness    = layer.UI.Lightness,
                        Colorize     = layer.UI.Colorize,
                        Invert       = layer.UI.Invert,
                        Threshold    = new ThresholdSnapshot { Enabled = layer.UI.ThresholdEnabled, Level = layer.UI.ThresholdLevel },
                        Posterize    = new PosterizeSnapshot { Enabled = layer.UI.PosterizeEnabled, Levels = layer.UI.PosterizeLevels },
                        Levels       = new LevelsSnapshot
                        {
                            Enabled     = layer.UI.LevelsEnabled,
                            InputBlack  = layer.UI.LevelsInputBlack,
                            InputWhite  = layer.UI.LevelsInputWhite,
                            Gamma       = layer.UI.LevelsGamma,
                            OutputBlack = layer.UI.LevelsOutputBlack,
                            OutputWhite = layer.UI.LevelsOutputWhite,
                        },
                        ColorBalance = new ColorBalanceSnapshot
                        {
                            Enabled            = layer.UI.ColorBalanceEnabled,
                            Shadows            = layer.UI.CBShadows,
                            Midtones           = layer.UI.CBMidtones,
                            Highlights         = layer.UI.CBHighlights,
                            PreserveLuminosity = layer.UI.CBPreserveLuminosity,
                        },
                        Curve        = CopyCurveSnapshot(layer),
                        GradientMap  = CopyGradientMapSnapshot(layer),
                        ImageClip    = CopyImageClipSnapshot(layer),
                    };
                    break;
            }
        }

        static void ApplyCurveSnapshot(PSDLayer layer, CurveSnapshot s)
        {
            layer.UI.CurveEnabled = s.Enabled;
            layer.UI.Curve        = CloneCurve(s.Curve);
            AdjustmentLutBaker.BakeCurveLut(layer);
        }

        static void ApplyGradientMapSnapshot(PSDLayer layer, GradientMapSnapshot s)
        {
            layer.UI.GradientMapEnabled   = s.Enabled;
            layer.UI.Gradient             = CloneGradient(s.Gradient);
            layer.UI.GradientMapOpacity   = s.Opacity;
            layer.UI.GradientMapNormalize = s.Normalize;
            AdjustmentLutBaker.BakeGradientLut(layer);
            if (s.Normalize) AdjustmentLutBaker.ComputeGradientLumRange(layer);
        }

        static void ApplyImageClipSnapshot(PSDLayer layer, ImageClipSnapshot s)
        {
            layer.UI.ImageClipEnabled = s.Enabled;
            layer.UI.ImageClipTex     = s.Tex;
            layer.UI.ImageClipTile    = s.Tile;
            layer.UI.ImageClipBlend   = s.Blend;
            layer.UI.ImageClipOpacity = s.Opacity;
        }

        /// <summary>クリップボードから指定した種類の調整パラメーターを layer へ貼り付ける。データが無ければ false。</summary>
        bool PasteAdjustment(ClipboardKind kind, PSDLayer layer)
        {
            if (!_adjustmentClipboard.TryGetValue(kind, out object raw)) return false;

            RegisterUndo($"Paste {kind}");

            switch (kind)
            {
                case ClipboardKind.Colorize:
                    layer.UI.Colorize = ((ColorizeSnapshot)raw).Value;
                    break;
                case ClipboardKind.Invert:
                    layer.UI.Invert = ((InvertSnapshot)raw).Value;
                    break;
                case ClipboardKind.Threshold:
                {
                    var s = (ThresholdSnapshot)raw;
                    layer.UI.ThresholdEnabled = s.Enabled;
                    layer.UI.ThresholdLevel   = s.Level;
                    break;
                }
                case ClipboardKind.Posterize:
                {
                    var s = (PosterizeSnapshot)raw;
                    layer.UI.PosterizeEnabled = s.Enabled;
                    layer.UI.PosterizeLevels  = s.Levels;
                    break;
                }
                case ClipboardKind.Levels:
                {
                    var s = (LevelsSnapshot)raw;
                    layer.UI.LevelsEnabled     = s.Enabled;
                    layer.UI.LevelsInputBlack  = s.InputBlack;
                    layer.UI.LevelsInputWhite  = s.InputWhite;
                    layer.UI.LevelsGamma       = s.Gamma;
                    layer.UI.LevelsOutputBlack = s.OutputBlack;
                    layer.UI.LevelsOutputWhite = s.OutputWhite;
                    break;
                }
                case ClipboardKind.ColorBalance:
                {
                    var s = (ColorBalanceSnapshot)raw;
                    layer.UI.ColorBalanceEnabled  = s.Enabled;
                    layer.UI.CBShadows            = s.Shadows;
                    layer.UI.CBMidtones           = s.Midtones;
                    layer.UI.CBHighlights         = s.Highlights;
                    layer.UI.CBPreserveLuminosity = s.PreserveLuminosity;
                    break;
                }
                case ClipboardKind.Curve:
                    ApplyCurveSnapshot(layer, (CurveSnapshot)raw);
                    break;
                case ClipboardKind.GradientMap:
                    ApplyGradientMapSnapshot(layer, (GradientMapSnapshot)raw);
                    break;
                case ClipboardKind.ImageClip:
                    ApplyImageClipSnapshot(layer, (ImageClipSnapshot)raw);
                    break;
                case ClipboardKind.ColorRangeMask:
                {
                    var s = (ColorRangeMaskSnapshot)raw;
                    layer.UI.ColorRangeTarget    = s.Target;
                    layer.UI.ColorRangeThreshold = s.Threshold;
                    BeginColorRangePreview(layer);
                    break;
                }
                case ClipboardKind.FullAdjustmentSection:
                {
                    var s = (FullAdjustmentSnapshot)raw;
                    layer.UI.Brightness = s.Brightness;
                    layer.UI.Contrast   = s.Contrast;
                    layer.UI.Hue        = s.Hue;
                    layer.UI.Saturation = s.Saturation;
                    layer.UI.Lightness  = s.Lightness;
                    layer.UI.Colorize   = s.Colorize;
                    layer.UI.Invert     = s.Invert;

                    layer.UI.ThresholdEnabled = s.Threshold.Enabled;
                    layer.UI.ThresholdLevel   = s.Threshold.Level;

                    layer.UI.PosterizeEnabled = s.Posterize.Enabled;
                    layer.UI.PosterizeLevels  = s.Posterize.Levels;

                    layer.UI.LevelsEnabled     = s.Levels.Enabled;
                    layer.UI.LevelsInputBlack  = s.Levels.InputBlack;
                    layer.UI.LevelsInputWhite  = s.Levels.InputWhite;
                    layer.UI.LevelsGamma       = s.Levels.Gamma;
                    layer.UI.LevelsOutputBlack = s.Levels.OutputBlack;
                    layer.UI.LevelsOutputWhite = s.Levels.OutputWhite;

                    layer.UI.ColorBalanceEnabled  = s.ColorBalance.Enabled;
                    layer.UI.CBShadows            = s.ColorBalance.Shadows;
                    layer.UI.CBMidtones           = s.ColorBalance.Midtones;
                    layer.UI.CBHighlights         = s.ColorBalance.Highlights;
                    layer.UI.CBPreserveLuminosity = s.ColorBalance.PreserveLuminosity;

                    ApplyCurveSnapshot(layer, s.Curve);
                    ApplyGradientMapSnapshot(layer, s.GradientMap);
                    ApplyImageClipSnapshot(layer, s.ImageClip);
                    break;
                }
                default:
                    return false;
            }

            SaveStatesToSerialized();
            MarkDirty();
            return true;
        }

        /// <summary>トグル行・セクション見出しの右端に置く歯車ボタン。クリックでコピー/ペーストのメニューを開く。</summary>
        void DrawAdjustmentGearMenu(ClipboardKind kind, PSDLayer layer)
        {
            if (!GUILayout.Button(new GUIContent("⚙", "この設定のコピー・ペースト"), PSDEditorTheme.FoldoutButtonStyle,
                                  GUILayout.Width(18), GUILayout.Height(RowH)))
                return;

            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("コピー"), false, () => CopyAdjustment(kind, layer));
            if (HasClipboard(kind))
            {
                menu.AddItem(new GUIContent("ペースト"), false, () =>
                {
                    PasteAdjustment(kind, layer);
                    Repaint();
                });
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("ペースト"));
            }
            menu.ShowAsContext();
        }
    }
}
