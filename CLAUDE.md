# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

A Unity Editor extension that loads Adobe PSD files (parsed with a from-scratch C# binary parser — **no external libraries**, a deliberate constraint for asset-store licensing; see requirements.md), previews them with per-layer visibility/opacity/adjustment controls, and exports the composite as PNG. UI text and code comments are in Japanese.

This repo is the `Assets/dennokoworks/PSDSimpleEditor` folder of a Unity project (VRChat Creator Companion). There is no standalone build or test command — code is compiled by the Unity Editor itself. Open the tool via the Unity menu **dennokoworks > PSD Simple Editor**. Check the Unity Console for compile errors and `[PSDParser]` / `[PSDSimpleEditor]` log messages.

## Folder structure

```
PSDSimpleEditor/
├── Core/          # UnityEngine のみ依存 (ビルド非対象にしたい場合は Editor/ 以下へ移動)
│   ├── BigEndianBinaryReader.cs   — PSD big-endian 読み込みプリミティブ
│   ├── BigEndianBinaryWriter.cs   — PSD big-endian 書き込みプリミティブ
│   ├── ColorRangeMask.cs          — 色域選択マスク生成 (CPU)
│   ├── PSDData.cs                 — データモデル (PSDFile, PSDLayer, BlendMode, …)
│   └── PSDParser.cs               — PSD バイナリパーサー
├── Editor/        # UnityEditor / LayerCompositor 依存 (ビルドに含まれない)
│   ├── LayerCompositor.cs         — GPU 合成エンジン (RenderTexture + LayerBlend.shader)
│   ├── PSDSimpleEditorWindow.cs   — EditorWindow (UI, プレビュー, エクスポート)
│   └── PSDWriter.cs               — PSD バイナリライター (LayerCompositor 依存のため Editor/)
├── Shader/
│   └── LayerBlend.shader          — フルスクリーン合成シェーダー (27 ブレンドモード)
├── Docs/
│   └── USAGE_ja.md                — 日本語使用方法
├── CLAUDE.md
├── REWRITE_SPEC.md
└── requirements.md
```

## Architecture

Data flows in one direction: parse → CPU textures → GPU compositing → preview/export.

1. **Core/PSDParser.cs** (static) — reads the PSD binary via **Core/BigEndianBinaryReader.cs** (PSD is big-endian; all multi-byte reads are byte-swapped). Parses header, layer records, additional layer info (`lsct` groups, `brit`/`hue2`/`SoCo` adjustments, `lrFX`/`lfx2` color overlay), decompresses pixel data (Raw/RLE/ZIP), builds per-layer `Texture2D`s, and assembles the flat layer list into a tree (`BuildLayerTree` + `ReverseTree` — index 0 = bottom-most layer).
2. **Core/PSDData.cs** — pure data model: `PSDFile`, `PSDLayer` (tree via `Children`), `BlendMode` enum, adjustment/mask/effect data. Layers carry both parsed PSD values and mutable `UI*` fields (UIVisible, UIOpacity, UIHue…) that the window edits.
3. **Editor/LayerCompositor.cs** — composites the layer tree on the GPU using ping-pong RenderTextures and a pooled RT stack for nested groups. Handles pass-through vs. isolated group blending, clipping-mask stacks (consecutive `IsClipping` layers clipped to the base layer's alpha), layer masks, adjustment layers, SoCo solid fills, and Color Overlay effects. Loads the shader by hardcoded path `Assets/dennokoworks/PSDSimpleEditor/Shader/LayerBlend.shader` — moving the Shader/ folder breaks this.
4. **Shader/LayerBlend.shader** — single full-screen pass that does layer placement (`_LayerRect` in PSD top-left coordinates), all 27 blend modes, masks, and HSL/brightness/contrast adjustments.
5. **Editor/PSDSimpleEditorWindow.cs** — the EditorWindow: IMGUI layer panel + preview + PNG/PSD export. Sets `_needsRecomposite` on any UI change; recomposite runs during the Repaint event.
6. **Core/ColorRangeMask.cs** — CPU-side color range selection mask generator (Photoshop「色域指定」相当). Called from PSDSimpleEditorWindow for highlight preview and PNG mask export.

### Critical invariant

`BlendMode` enum int values map 1:1 to the `if/else if` branch numbers in LayerBlend.shader's blend function. Never renumber the enum; adding a blend mode requires changing both files in lockstep.

### Conventions

- Adjustment layers are detected by zero-area bounds (`PSDLayer.IsAdjustmentLayer` => `Width <= 0 || Height <= 0`).
- All runtime-created Unity objects (Texture2D, RenderTexture, Material) use `HideFlags.HideAndDontSave` and must be explicitly destroyed — see `Cleanup`/`Dispose` patterns. Leaking these causes editor memory growth.
- Coordinates: PSD uses top-left origin; conversion to Unity's bottom-left happens in the shader/texture-build steps.
- requirements.md is the original Japanese spec; the implementation has grown beyond it (all blend modes, ZIP compression, 16-bit, CMYK/LAB, groups, masks, clipping, effects).
