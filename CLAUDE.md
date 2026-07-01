# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

A Unity Editor extension that loads Adobe PSD files (parsed with a from-scratch C# binary parser — **no external libraries**, a deliberate constraint for asset-store licensing; see requirements.md), previews them with per-layer visibility/opacity/adjustment controls, and exports the composite as PNG. UI text and code comments are in Japanese.

This repo is the `Assets/dennokoworks/PSDSimpleEditor` folder of a Unity project (VRChat Creator Companion). There is no standalone build or test command — code is compiled by the Unity Editor itself. Open the tool via the Unity menu **dennokoworks > Dennoko PSD Editor**. Check the Unity Console for compile errors and `[PSDParser]` / `[PSDSimpleEditor]` log messages.

## Folder structure

```
PSDSimpleEditor/
├── Core/          # UnityEngine のみ依存 (ビルド非対象にしたい場合は Editor/ 以下へ移動)
│   ├── BigEndianBinaryReader.cs   — PSD big-endian 読み込みプリミティブ
│   ├── BigEndianBinaryWriter.cs   — PSD big-endian 書き込みプリミティブ
│   ├── ColorRangeMask.cs          — 色域選択マスク生成 (CPU)
│   ├── PSDData.cs                 — データモデル (PSDFile, PSDLayer, BlendMode, …)
│   └── Parser/                    — PSDParser の実装分割 (責務ごとの独立 static クラス、単一方向依存)
│       ├── PSDParser.cs               — 公開 API (`Parse`) + パース全体のオーケストレーション
│       ├── PSDHeaderReader.cs         — Section 1 ヘッダ + Section 2/3 スキップ
│       ├── PSDLayerRecordParser.cs    — Section 4 レイヤーレコード (bounds/blend/mask/name)
│       ├── PSDAdditionalInfoParser.cs — 追加情報ハンドラ (lsct グループ, brit/hue2/SoCo, lrFX/lfx2)
│       ├── PSDDescriptorParser.cs     — 最小ディスクリプタパーサー (SoCo/CgEd/lfx2 用)
│       ├── PSDChannelDecoder.cs       — チャンネル画像データの解凍 (Raw/RLE/ZIP+prediction)
│       ├── PSDLayerAssembler.cs       — Texture2D 構築 + レイヤーツリー構築 (BuildLayerTree) + UI 初期値
│       ├── PSDMergedImageParser.cs    — Section 5 マージ済み画像 (RGB/Grayscale/CMYK/Lab)
│       └── PSDBlendModeConverter.cs   — ブレンドキー ⇔ BlendMode enum 変換
├── Editor/        # UnityEditor / LayerCompositor 依存 (ビルドに含まれない)
│   ├── Compositor/                — LayerCompositor の実装分割 (partial class、共有状態あり)
│   │   ├── LayerCompositor.cs         — 合成パイプライン本体 (Composite/CompositeList/CompositeGroup/DrawLayer)
│   │   ├── LayerCompositor.Params.cs  — シェーダー uniform 設定 (DrawParams, ApplyParams)
│   │   ├── LayerCompositor.Export.cs  — 書き出し用レンダリング (RenderLayerForExport 等) + クリップ用 α 抽出
│   │   └── LayerCompositor.RT.cs      — RenderTexture プール管理 + ソリッドテクスチャキャッシュ
│   ├── Window/                     — PSDSimpleEditorWindow の実装分割 (partial class、共有状態あり)
│   │   ├── PSDSimpleEditorWindow.cs               — フィールド, ライフサイクル, OnGUI, PSD 読み込み, 合成実行
│   │   ├── PSDSimpleEditorWindow.Toolbar.cs        — ツールバー (PSD パス/Export Dir 入力, 履歴メニュー)
│   │   ├── PSDSimpleEditorWindow.LayerPanel.cs     — レイヤーツリー UI + ブレンドモード Popup
│   │   ├── PSDSimpleEditorWindow.Adjustments.cs    — 色調補正・グラデーションマップ・画像クリップ (非破壊)
│   │   ├── PSDSimpleEditorWindow.Preview.cs        — プレビュー描画 + チェッカー背景 + マージ参照小窓
│   │   ├── PSDSimpleEditorWindow.ColorRangeMask.cs — 色域選択マスク (スポイト/ハイライト/PNG 出力)
│   │   ├── PSDSimpleEditorWindow.Export.cs         — 下部バー + PNG/PSD 書き出し
│   │   └── PSDPathHistory.cs                       — PSD 読み込み履歴 (EditorPrefs 永続化、独立クラス)
│   └── Writer/                     — PSDWriter の実装分割 (責務ごとの独立 static クラス)
│       ├── PSDWriter.cs               — 公開 API (`Save`) + ヘッダ/マージ画像セクション書き込み
│       ├── PSDLayerRecordWriter.cs    — レイヤーレコードのバイナリ書き込み
│       ├── PSDExportRecordBuilder.cs  — レイヤーツリー → フラットレコード組み立て (BuildLayerTree の逆)
│       ├── PSDExportRecord.cs         — 書き出し用中間データ (ExportChannel/ExportRecord)
│       └── PSDPixelEncoder.cs         — RLE (PackBits) 圧縮 + テクスチャ読み戻し
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

1. **Core/Parser/** (static, `PSDParser.Parse` is the sole public entry point) — reads the PSD binary via **Core/BigEndianBinaryReader.cs** (PSD is big-endian; all multi-byte reads are byte-swapped). `PSDHeaderReader` parses the header; `PSDLayerRecordParser` reads layer records and delegates additional-info blocks (`lsct` groups, `brit`/`hue2`/`SoCo` adjustments, `lrFX`/`lfx2` color overlay) to `PSDAdditionalInfoParser` (which uses `PSDDescriptorParser` for the descriptor-format blocks); `PSDChannelDecoder` decompresses pixel data (Raw/RLE/ZIP); `PSDLayerAssembler` builds per-layer `Texture2D`s and assembles the flat layer list into a tree (`BuildLayerTree` — index 0 = bottom-most layer) and sets initial `UI*` state. Dependencies flow one way: `PSDParser` → record/merged-image parsers → decoder/assembler/descriptor/blend-mode helpers (no cycles).
2. **Core/PSDData.cs** — pure data model: `PSDFile`, `PSDLayer` (tree via `Children`), `BlendMode` enum, adjustment/mask/effect data. Layers carry both parsed PSD values and mutable `UI*` fields (UIVisible, UIOpacity, UIHue…) that the window edits.
3. **Editor/Compositor/** (`LayerCompositor`, split as `partial class` across 4 files sharing the compositor's private RT/Material state) — composites the layer tree on the GPU using ping-pong RenderTextures and a pooled RT stack for nested groups. Handles pass-through vs. isolated group blending, clipping-mask stacks (consecutive `IsClipping` layers clipped to the base layer's alpha), layer masks, adjustment layers, SoCo solid fills, and Color Overlay effects. Loads the shader by hardcoded path `Assets/dennokoworks/PSDSimpleEditor/Shader/LayerBlend.shader` — moving the Shader/ folder breaks this (moving the Compositor/ subfolder itself does not, since the path is a project-relative constant).
4. **Shader/LayerBlend.shader** — single full-screen pass that does layer placement (`_LayerRect` in PSD top-left coordinates), all 27 blend modes, masks, and HSL/brightness/contrast adjustments.
5. **Editor/Window/** (`PSDSimpleEditorWindow`, split as `partial class` across 7 files sharing the window's private UI/state fields) — the EditorWindow: IMGUI layer panel + preview + PNG/PSD export. Sets `_needsRecomposite` on any UI change; recomposite runs during the Repaint event. `PSDPathHistory` is the one non-partial helper class here (self-contained EditorPrefs-backed load history).
6. **Editor/Writer/** (static, `PSDWriter.Save` is the sole public entry point) — the inverse of Core/Parser/: `PSDExportRecordBuilder` flattens the layer tree back into records (baking adjustments/gradient maps via `LayerCompositor.RenderLayerForExport` where needed), `PSDLayerRecordWriter` serializes those records, and `PSDPixelEncoder` handles RLE (PackBits) compression and texture read-back.
7. **Core/ColorRangeMask.cs** — CPU-side color range selection mask generator (Photoshop「色域指定」相当). Called from PSDSimpleEditorWindow for highlight preview and PNG mask export.

### Critical invariant

`BlendMode` enum int values map 1:1 to the `if/else if` branch numbers in LayerBlend.shader's blend function. Never renumber the enum; adding a blend mode requires changing both files in lockstep.

### Conventions

- Adjustment layers are detected by zero-area bounds (`PSDLayer.IsAdjustmentLayer` => `Width <= 0 || Height <= 0`).
- All runtime-created Unity objects (Texture2D, RenderTexture, Material) use `HideFlags.HideAndDontSave` and must be explicitly destroyed — see `Cleanup`/`Dispose` patterns. Leaking these causes editor memory growth.
- Coordinates: PSD uses top-left origin; conversion to Unity's bottom-left happens in the shader/texture-build steps.
- requirements.md is the original Japanese spec; the implementation has grown beyond it (all blend modes, ZIP compression, 16-bit, CMYK/LAB, groups, masks, clipping, effects).
