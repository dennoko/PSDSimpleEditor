using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace PSDSimpleEditor
{
    // ── 調整パラメーターのコピー&ペースト: 種類ごとに直近1件を保持する単純なクリップボード ──
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
            src != null ? new AnimationCurve(src.keys) : CreateDefaultCurve();

        static Gradient CloneGradient(Gradient src)
        {
            var g = new Gradient();
            if (src != null) g.SetKeys(src.colorKeys, src.alphaKeys);
            else             return CreateDefaultGradient();
            return g;
        }

        static CurveSnapshot CopyCurveSnapshot(PSDLayer layer) =>
            new CurveSnapshot { Enabled = layer.UICurveEnabled, Curve = CloneCurve(layer.UICurve) };

        static GradientMapSnapshot CopyGradientMapSnapshot(PSDLayer layer) =>
            new GradientMapSnapshot
            {
                Enabled   = layer.UIGradientMapEnabled,
                Gradient  = CloneGradient(layer.UIGradient),
                Opacity   = layer.UIGradientMapOpacity,
                Normalize = layer.UIGradientMapNormalize,
            };

        static ImageClipSnapshot CopyImageClipSnapshot(PSDLayer layer) =>
            new ImageClipSnapshot
            {
                Enabled = layer.UIImageClipEnabled,
                Tex     = layer.UIImageClipTex,
                Tile    = layer.UIImageClipTile,
                Blend   = layer.UIImageClipBlend,
                Opacity = layer.UIImageClipOpacity,
            };

        /// <summary>指定した種類の調整パラメーターを layer からクリップボードへコピーする。</summary>
        void CopyAdjustment(ClipboardKind kind, PSDLayer layer)
        {
            switch (kind)
            {
                case ClipboardKind.Colorize:
                    _adjustmentClipboard[kind] = new ColorizeSnapshot { Value = layer.UIColorize };
                    break;
                case ClipboardKind.Invert:
                    _adjustmentClipboard[kind] = new InvertSnapshot { Value = layer.UIInvert };
                    break;
                case ClipboardKind.Threshold:
                    _adjustmentClipboard[kind] = new ThresholdSnapshot { Enabled = layer.UIThresholdEnabled, Level = layer.UIThresholdLevel };
                    break;
                case ClipboardKind.Posterize:
                    _adjustmentClipboard[kind] = new PosterizeSnapshot { Enabled = layer.UIPosterizeEnabled, Levels = layer.UIPosterizeLevels };
                    break;
                case ClipboardKind.Levels:
                    _adjustmentClipboard[kind] = new LevelsSnapshot
                    {
                        Enabled     = layer.UILevelsEnabled,
                        InputBlack  = layer.UILevelsInputBlack,
                        InputWhite  = layer.UILevelsInputWhite,
                        Gamma       = layer.UILevelsGamma,
                        OutputBlack = layer.UILevelsOutputBlack,
                        OutputWhite = layer.UILevelsOutputWhite,
                    };
                    break;
                case ClipboardKind.ColorBalance:
                    _adjustmentClipboard[kind] = new ColorBalanceSnapshot
                    {
                        Enabled            = layer.UIColorBalanceEnabled,
                        Shadows            = layer.UICBShadows,
                        Midtones           = layer.UICBMidtones,
                        Highlights         = layer.UICBHighlights,
                        PreserveLuminosity = layer.UICBPreserveLuminosity,
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
                    _adjustmentClipboard[kind] = new ColorRangeMaskSnapshot { Target = layer.UIColorRangeTarget, Threshold = layer.UIColorRangeThreshold };
                    break;
                case ClipboardKind.FullAdjustmentSection:
                    _adjustmentClipboard[kind] = new FullAdjustmentSnapshot
                    {
                        Brightness   = layer.UIBrightness,
                        Contrast     = layer.UIContrast,
                        Hue          = layer.UIHue,
                        Saturation   = layer.UISaturation,
                        Lightness    = layer.UILightness,
                        Colorize     = layer.UIColorize,
                        Invert       = layer.UIInvert,
                        Threshold    = new ThresholdSnapshot { Enabled = layer.UIThresholdEnabled, Level = layer.UIThresholdLevel },
                        Posterize    = new PosterizeSnapshot { Enabled = layer.UIPosterizeEnabled, Levels = layer.UIPosterizeLevels },
                        Levels       = new LevelsSnapshot
                        {
                            Enabled     = layer.UILevelsEnabled,
                            InputBlack  = layer.UILevelsInputBlack,
                            InputWhite  = layer.UILevelsInputWhite,
                            Gamma       = layer.UILevelsGamma,
                            OutputBlack = layer.UILevelsOutputBlack,
                            OutputWhite = layer.UILevelsOutputWhite,
                        },
                        ColorBalance = new ColorBalanceSnapshot
                        {
                            Enabled            = layer.UIColorBalanceEnabled,
                            Shadows            = layer.UICBShadows,
                            Midtones           = layer.UICBMidtones,
                            Highlights         = layer.UICBHighlights,
                            PreserveLuminosity = layer.UICBPreserveLuminosity,
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
            layer.UICurveEnabled = s.Enabled;
            layer.UICurve        = CloneCurve(s.Curve);
            BakeCurveLut(layer);
        }

        static void ApplyGradientMapSnapshot(PSDLayer layer, GradientMapSnapshot s)
        {
            layer.UIGradientMapEnabled   = s.Enabled;
            layer.UIGradient             = CloneGradient(s.Gradient);
            layer.UIGradientMapOpacity   = s.Opacity;
            layer.UIGradientMapNormalize = s.Normalize;
            BakeGradientLut(layer);
            if (s.Normalize) ComputeGradientLumRange(layer);
        }

        static void ApplyImageClipSnapshot(PSDLayer layer, ImageClipSnapshot s)
        {
            layer.UIImageClipEnabled = s.Enabled;
            layer.UIImageClipTex     = s.Tex;
            layer.UIImageClipTile    = s.Tile;
            layer.UIImageClipBlend   = s.Blend;
            layer.UIImageClipOpacity = s.Opacity;
        }

        /// <summary>クリップボードから指定した種類の調整パラメーターを layer へ貼り付ける。データが無ければ false。</summary>
        bool PasteAdjustment(ClipboardKind kind, PSDLayer layer)
        {
            if (!_adjustmentClipboard.TryGetValue(kind, out object raw)) return false;

            switch (kind)
            {
                case ClipboardKind.Colorize:
                    layer.UIColorize = ((ColorizeSnapshot)raw).Value;
                    break;
                case ClipboardKind.Invert:
                    layer.UIInvert = ((InvertSnapshot)raw).Value;
                    break;
                case ClipboardKind.Threshold:
                {
                    var s = (ThresholdSnapshot)raw;
                    layer.UIThresholdEnabled = s.Enabled;
                    layer.UIThresholdLevel   = s.Level;
                    break;
                }
                case ClipboardKind.Posterize:
                {
                    var s = (PosterizeSnapshot)raw;
                    layer.UIPosterizeEnabled = s.Enabled;
                    layer.UIPosterizeLevels  = s.Levels;
                    break;
                }
                case ClipboardKind.Levels:
                {
                    var s = (LevelsSnapshot)raw;
                    layer.UILevelsEnabled     = s.Enabled;
                    layer.UILevelsInputBlack  = s.InputBlack;
                    layer.UILevelsInputWhite  = s.InputWhite;
                    layer.UILevelsGamma       = s.Gamma;
                    layer.UILevelsOutputBlack = s.OutputBlack;
                    layer.UILevelsOutputWhite = s.OutputWhite;
                    break;
                }
                case ClipboardKind.ColorBalance:
                {
                    var s = (ColorBalanceSnapshot)raw;
                    layer.UIColorBalanceEnabled  = s.Enabled;
                    layer.UICBShadows            = s.Shadows;
                    layer.UICBMidtones           = s.Midtones;
                    layer.UICBHighlights         = s.Highlights;
                    layer.UICBPreserveLuminosity = s.PreserveLuminosity;
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
                    layer.UIColorRangeTarget    = s.Target;
                    layer.UIColorRangeThreshold = s.Threshold;
                    BeginColorRangePreview(layer);
                    break;
                }
                case ClipboardKind.FullAdjustmentSection:
                {
                    var s = (FullAdjustmentSnapshot)raw;
                    layer.UIBrightness = s.Brightness;
                    layer.UIContrast   = s.Contrast;
                    layer.UIHue        = s.Hue;
                    layer.UISaturation = s.Saturation;
                    layer.UILightness  = s.Lightness;
                    layer.UIColorize   = s.Colorize;
                    layer.UIInvert     = s.Invert;

                    layer.UIThresholdEnabled = s.Threshold.Enabled;
                    layer.UIThresholdLevel   = s.Threshold.Level;

                    layer.UIPosterizeEnabled = s.Posterize.Enabled;
                    layer.UIPosterizeLevels  = s.Posterize.Levels;

                    layer.UILevelsEnabled     = s.Levels.Enabled;
                    layer.UILevelsInputBlack  = s.Levels.InputBlack;
                    layer.UILevelsInputWhite  = s.Levels.InputWhite;
                    layer.UILevelsGamma       = s.Levels.Gamma;
                    layer.UILevelsOutputBlack = s.Levels.OutputBlack;
                    layer.UILevelsOutputWhite = s.Levels.OutputWhite;

                    layer.UIColorBalanceEnabled  = s.ColorBalance.Enabled;
                    layer.UICBShadows            = s.ColorBalance.Shadows;
                    layer.UICBMidtones           = s.ColorBalance.Midtones;
                    layer.UICBHighlights         = s.ColorBalance.Highlights;
                    layer.UICBPreserveLuminosity = s.ColorBalance.PreserveLuminosity;

                    ApplyCurveSnapshot(layer, s.Curve);
                    ApplyGradientMapSnapshot(layer, s.GradientMap);
                    ApplyImageClipSnapshot(layer, s.ImageClip);
                    break;
                }
                default:
                    return false;
            }

            _needsRecomposite = true;
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
