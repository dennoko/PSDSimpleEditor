using System.Collections.Generic;
using UnityEngine;

namespace PSDSimpleEditor
{
    public enum BlendMode
    {
        Normal   = 0,
        Multiply = 1,
        Screen   = 2,
        Overlay  = 3,
        Unknown  = 99
    }

    public class ChannelInfo
    {
        public short ChannelId;   // 0=R, 1=G, 2=B, -1=Alpha
        public int   DataLength;  // includes 2-byte compression prefix
    }

    public class AdjustmentData
    {
        public bool  HasBrightnessContrast;
        public float Brightness;   // -150 .. 150
        public float Contrast;     // -50  .. 100

        public bool  HasHueSaturation;
        public float Hue;          // -180 .. 180
        public float Saturation;   // -100 .. 100
        public float Lightness;    // -100 .. 100

        public bool  HasSolidColor;
        public Color SolidColor;
    }

    public class PSDLayer
    {
        // Bounding box in canvas coordinates (PSD top-left origin)
        public int Top, Left, Bottom, Right;
        public int Width  => Right  - Left;
        public int Height => Bottom - Top;

        public List<ChannelInfo> Channels = new List<ChannelInfo>();
        public BlendMode         BlendMode = BlendMode.Normal;
        public byte              Opacity   = 255;
        public bool              IsVisible = true;
        public string            Name      = "";
        public AdjustmentData    Adjustment = new AdjustmentData();

        // Built Unity texture (null for adjustment-only layers)
        public Texture2D Texture;

        // ── Runtime UI state (initialised from parsed PSD values) ──
        public bool  UIVisible;
        public float UIOpacity;      // 0 .. 1
        public float UIBrightness;   // -150 .. 150
        public float UIContrast;     // -50  .. 100
        public float UIHue;          // -180 .. 180
        public float UISaturation;   // -100 .. 100
        public float UILightness;    // -100 .. 100

        // True when the layer carries no pixel data (e.g. Brightness/Contrast layer)
        public bool IsAdjustmentLayer => Width <= 0 || Height <= 0;

        // ── Internal – used only during parsing ──
        [System.NonSerialized]
        public byte[] _rawPixels;
    }

    public class PSDFile
    {
        public ushort Version;
        public ushort Channels;
        public int    Height;
        public int    Width;
        public ushort BitDepth;
        public ushort ColorMode;
        public List<PSDLayer> Layers = new List<PSDLayer>();
    }
}
