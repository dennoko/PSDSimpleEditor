# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

A Unity Editor extension that loads Adobe PSD files (parsed with a from-scratch C# binary parser — **no external libraries**, a deliberate constraint for asset-store licensing; see requirements.md), previews them with per-layer visibility/opacity/adjustment controls, and exports the composite as PNG. UI text and code comments are in Japanese.

This repo is the `Assets/Editor/PSDSimpleEditor` folder of a Unity project (VRChat Creator Companion). There is no standalone build or test command — code is compiled by the Unity Editor itself. Open the tool via the Unity menu **dennokoworks > PSD Simple Editor**. Check the Unity Console for compile errors and `[PSDParser]` / `[PSDSimpleEditor]` log messages.

## Architecture

Data flows in one direction: parse → CPU textures → GPU compositing → preview/export.

1. **PSDParser.cs** (static) — reads the PSD binary via **BigEndianBinaryReader.cs** (PSD is big-endian; all multi-byte reads are byte-swapped). Parses header, layer records, additional layer info (`lsct` groups, `brit`/`hue2`/`SoCo` adjustments, `lrFX`/`lfx2` color overlay), decompresses pixel data (Raw/RLE/ZIP), builds per-layer `Texture2D`s, and assembles the flat layer list into a tree (`BuildLayerTree` + `ReverseTree` — index 0 = bottom-most layer).
2. **PSDData.cs** — pure data model: `PSDFile`, `PSDLayer` (tree via `Children`), `BlendMode` enum, adjustment/mask/effect data. Layers carry both parsed PSD values and mutable `UI*` fields (UIVisible, UIOpacity, UIHue…) that the window edits.
3. **LayerCompositor.cs** — composites the layer tree on the GPU using ping-pong RenderTextures and a pooled RT stack for nested groups. Handles pass-through vs. isolated group blending, clipping-mask stacks (consecutive `IsClipping` layers clipped to the base layer's alpha), layer masks, adjustment layers, SoCo solid fills, and Color Overlay effects. Loads the shader by hardcoded path `Assets/dennokoworks/PSDSimpleEditor/LayerBlend.shader` — moving the folder breaks this.
4. **LayerBlend.shader** — single full-screen pass that does layer placement (`_LayerRect` in PSD top-left coordinates), all 27 blend modes, masks, and HSL/brightness/contrast adjustments.
5. **PSDSimpleEditorWindow.cs** — the EditorWindow: IMGUI layer panel + preview + PNG export. Sets `_needsRecomposite` on any UI change; recomposite runs during the Repaint event.

### Critical invariant

`BlendMode` enum int values map 1:1 to the `if/else if` branch numbers in LayerBlend.shader's blend function. Never renumber the enum; adding a blend mode requires changing both files in lockstep.

### Conventions

- Adjustment layers are detected by zero-area bounds (`PSDLayer.IsAdjustmentLayer` => `Width <= 0 || Height <= 0`).
- All runtime-created Unity objects (Texture2D, RenderTexture, Material) use `HideFlags.HideAndDontSave` and must be explicitly destroyed — see `Cleanup`/`Dispose` patterns. Leaking these causes editor memory growth.
- Coordinates: PSD uses top-left origin; conversion to Unity's bottom-left happens in the shader/texture-build steps.
- requirements.md is the original Japanese spec; the implementation has grown beyond it (all blend modes, ZIP compression, 16-bit, CMYK/LAB, groups, masks, clipping, effects).
