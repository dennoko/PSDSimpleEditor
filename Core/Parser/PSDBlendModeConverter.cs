using System.Collections.Generic;

namespace PSDSimpleEditor
{
    // ════════════════════════════════════════════════════════════════
    //  ブレンドモード変換
    // ════════════════════════════════════════════════════════════════
    // Editor アセンブリ (PSDWriter) からも KeyOf を参照するため public。
    // 内部専用のメソッドは internal のまま (Core アセンブリ内でのみ使用)。
    public static class PSDBlendModeConverter
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

        // 書き出し用の逆引き (BlendMode → 4 文字キー)。KeyToBlendMode 1 つを真実とし、
        // ここから自動生成する (read/write で別々の表を手で同期しないため)。
        static readonly Dictionary<BlendMode, string> BlendModeToKey = BuildReverse(KeyToBlendMode);

        static Dictionary<BlendMode, string> BuildReverse(Dictionary<string, BlendMode> forward)
        {
            var reverse = new Dictionary<BlendMode, string>();
            foreach (var kv in forward)
                if (!reverse.ContainsKey(kv.Value)) reverse[kv.Value] = kv.Key;
            return reverse;
        }

        /// <summary>BlendMode に対応する 4 文字キーを返す (未登録は "norm")。</summary>
        public static string KeyOf(BlendMode mode)
            => BlendModeToKey.TryGetValue(mode, out var key) ? key : "norm";

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
