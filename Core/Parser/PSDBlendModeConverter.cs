using System.Collections.Generic;

namespace PSDSimpleEditor
{
    // ════════════════════════════════════════════════════════════════
    //  ブレンドモード変換
    // ════════════════════════════════════════════════════════════════
    internal static class PSDBlendModeConverter
    {
        static readonly Dictionary<string, BlendMode> KeyToBlendMode = new Dictionary<string, BlendMode>
        {
            { "norm", BlendMode.Normal       }, { "mul ", BlendMode.Multiply     },
            { "scrn", BlendMode.Screen       }, { "over", BlendMode.Overlay      },
            { "diss", BlendMode.Dissolve     }, { "dark", BlendMode.Darken       },
            { "idiv", BlendMode.ColorBurn    }, { "lbrn", BlendMode.LinearBurn   },
            { "dkCl", BlendMode.DarkerColor  }, { "lite", BlendMode.Lighten      },
            { "div ", BlendMode.ColorDodge   }, { "lddg", BlendMode.LinearDodge  },
            { "lgCl", BlendMode.LighterColor }, { "sLit", BlendMode.SoftLight    },
            { "hLit", BlendMode.HardLight    }, { "vLit", BlendMode.VividLight   },
            { "lLit", BlendMode.LinearLight  }, { "pLit", BlendMode.PinLight     },
            { "hMix", BlendMode.HardMix      }, { "diff", BlendMode.Difference   },
            { "smud", BlendMode.Exclusion    }, { "fsub", BlendMode.Subtract     },
            { "fdiv", BlendMode.Divide       }, { "hue ", BlendMode.Hue          },
            { "sat ", BlendMode.Saturation   }, { "colr", BlendMode.Color        },
            { "lum ", BlendMode.Luminosity   }, { "pass", BlendMode.PassThrough  },
        };

        internal static BlendMode BlendModeFromKey(string key)
        {
            return KeyToBlendMode.TryGetValue(key, out var mode) ? mode : BlendMode.Unknown;
        }

        // ディスクリプタの enum 値 (BlnM) → BlendMode (lfx2 用)
        static readonly Dictionary<string, BlendMode> EnumToBlendMode = new Dictionary<string, BlendMode>
        {
            { "Nrml", BlendMode.Normal       }, { "Dslv", BlendMode.Dissolve     },
            { "Drkn", BlendMode.Darken       }, { "Mltp", BlendMode.Multiply     },
            { "CBrn", BlendMode.ColorBurn    }, { "linearBurn", BlendMode.LinearBurn },
            { "darkerColor", BlendMode.DarkerColor }, { "Lghn", BlendMode.Lighten },
            { "Scrn", BlendMode.Screen       }, { "CDdg", BlendMode.ColorDodge   },
            { "linearDodge", BlendMode.LinearDodge }, { "lighterColor", BlendMode.LighterColor },
            { "Ovrl", BlendMode.Overlay      }, { "SftL", BlendMode.SoftLight    },
            { "HrdL", BlendMode.HardLight    }, { "vividLight", BlendMode.VividLight },
            { "linearLight", BlendMode.LinearLight }, { "pinLight", BlendMode.PinLight },
            { "hardMix", BlendMode.HardMix   }, { "Dfrn", BlendMode.Difference   },
            { "Xclu", BlendMode.Exclusion    }, { "blendSubtraction", BlendMode.Subtract },
            { "blendDivide", BlendMode.Divide }, { "H   ", BlendMode.Hue         },
            { "Strt", BlendMode.Saturation   }, { "Clr ", BlendMode.Color        },
            { "Lmns", BlendMode.Luminosity   },
        };

        internal static BlendMode BlendModeFromEnumValue(string value)
        {
            return EnumToBlendMode.TryGetValue(value, out var mode) ? mode : BlendMode.Normal;
        }
    }
}
