# DennokoPSDEditor — Unity 6 移行調査

- 調査日: 2026-09-06
- 現行: Unity 2022.3.22f1 / Built-in RP
- 目標: Unity 6 (6000.0 LTS) / **BiRP 維持**
- 共通調査: [`unity6-migration-overview.md`](unity6-migration-overview.md)

## 判定

🔍 **要検証** — Unity 6 非対応の API は **0 件**。修正すべきコードはない。
外部依存も **なし**。ただし **ワークスペース内で最も RenderTexture / Graphics.Blit に依存する**ため、
Unity 6 上での描画結果の検証が重要。

## 構成

| 項目 | 内容 |
|---|---|
| 規模 | C# 49 ファイル / 約 12,390 行（ワークスペース最大級）＋ シェーダ 1 |
| asmdef | なし（プロジェクト既定アセンブリ） |
| エントリ | `MenuItem("dennokoworks/Dennoko PSD Editor")` |
| UI | **UI Toolkit**（`PSDEditorTheme.uss`）+ IMGUI 併用 |
| 外部依存 | **なし**（VRChat SDK / NDMF / lilToon すべて非依存） |

**外部依存がゼロ**のため、VRChat SDK に依存せず単体検証できる。
規模が大きく描画依存も強いため、**共通調査フェーズ 2（先行検証）の重点対象**。

## 検出事項

### 1. RenderTexture / Graphics.Blit によるレイヤー合成パイプライン（🔍 重点検証）

`Editor/Compositor/LayerCompositor.cs` を中心に、ワークスペース最大の描画コードを持つ。

`Editor/Compositor/LayerCompositor.cs:12`

```
1 レイヤー合成するごとに Graphics.Blit(cur, next, _mat) → Swap(cur, next)。
```

`Editor/Compositor/LayerCompositor.cs:20`

```
色空間: RT は全て RenderTextureReadWrite.Linear、戻り Texture2D も linear:true。
```

主な使用 API と Unity 6 での状況:

| API | 使用箇所 | Unity 6 での状況 |
|---|---|---|
| `Graphics.Blit(src, dst, mat)` | `LayerCompositor.cs:194` ほか | **BiRP 維持なら変更なし** |
| `new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)` | `LayerCompositor.cs:188` | 変更なし。廃止フォーマット非該当 |
| `RenderTexture.active` の退避／復元 | `LayerCompositor.cs:146,163,179,199` | 変更なし。対称に復元されている |
| `RenderTexture` プール（`Stack<RenderTexture>`） | `LayerCompositor.cs:55`, `LayerCompositor.RT.cs` | 変更なし |
| `Texture2D` + `ReadPixels` | `LayerCompositor.Export.cs:195,251,306` | 変更なし |

**Unity 6 で削除された `GraphicsFormat.DepthAuto` / `ShadowAuto` / `VideoAuto` は未使用**。
使用しているのは `RenderTextureFormat.ARGB32` のみで、コンパイルエラーにはならない。

**重点検証すべき理由**: 色空間（Linear / sRGB）の扱いを明示的に制御しており、
Unity 6 での既定挙動のわずかな差でも**合成結果の色がずれる**可能性がある。
ピクセル単位での比較検証を推奨する。

**検証方法**: 同一 PSD を 2022.3 と Unity 6 の両方で読み込み、
書き出した PNG をピクセル比較して差分がないことを確認する。

### 2. `LayerBlend.shader`（✅ 影響なし）

`Shader/LayerBlend.shader` — `CGPROGRAM` + `#pragma vertex vert` / `#pragma fragment frag` +
`#include "UnityCG.cginc"` の BiRP 標準構成。

BiRP を維持する限り Unity 6 で有効。**修正不要。**
（URP へ移行する場合は全面書き換えが必要になるが、本計画の対象外。）

### 3. LUT テクスチャ生成（✅ 影響なし）

`Editor/AdjustmentLutBaker.cs:64,110,130`

```csharp
new Texture2D(N, 1, TextureFormat.RGBA32, false, linear: true)
```

トーンカーブ / グラデーション / グラデーションフィル用の 1D LUT。
`mipChain: false` のため Unity 6 の mipmap limit 変更の影響を受けない。**修正不要。**

### 4. `mipChain: true` での Texture2D 生成（🔍 軽微な確認）

`Editor/Window/PSDSimpleEditorWindow.cs:329`

```csharp
var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, mipChain: true, linear: false)
```

Unity 6 の「ランタイム生成 Texture2D が既定で mipmap limit に従わなくなる」変更に該当し得る
唯一の箇所。ただしエディタ内プレビュー用途であり、mipmap limit（品質設定によるミップ切り捨て）は
通常適用されない。**影響は軽微だが、プレビュー表示を目視確認する。**

### 5. UI Toolkit（🔍 視覚検証のみ）

`Editor/Window/PSDSimpleEditorWindow.UIToolkit.cs`,
`Editor/Window/PSDSimpleEditorWindow.UIToolkit.LayerTree.cs`,
`Editor/Window/PSDSimpleEditorWindow.Selection.cs`
USS: `Editor/USS/PSDEditorTheme.uss`

- Unity 6 で Obsolete 化する `ExecuteDefaultAction` / `ExecuteDefaultActionAtTarget` /
  `PreventDefault` は **未使用**。
- `UxmlFactory` / `UxmlTraits` も **未使用**（カスタム要素は C# で直接構築）。
- **コード修正不要。**

**対応**: Unity 6 の既定 USS 変更による見た目のずれのみ確認する。
特に **レイヤーツリー**（`LayerTree`）は入れ子・インデント・アイコン配置が
既定スタイルに影響されやすいため重点確認。

### 6. IMGUI テーマ: 共有 `EditorStyles` の書き換え（🔍 視覚検証）

`Editor/PSDEditorTheme.cs:414,423` ほか

共通調査 3.3 節と同じパターン。UI Toolkit と IMGUI を併用しているため、
IMGUI 側のスタイル上書きが Unity 6 の Editor スキン刷新で崩れる可能性がある。

**対応**: 目視確認し、必要なら `RectOffset` の数値のみ調整。

### 7. フォント生成（`UnityEngine.TextCore.Text.FontAsset`）（🔍 実機確認）

`Editor/DennokoUIFont.cs:5,136`

```csharp
using FontAsset = UnityEngine.TextCore.Text.FontAsset;
foreach (var fa in Resources.FindObjectsOfTypeAll<FontAsset>())
```

- `UnityEngine.TextCore.Text` はビルトインモジュールであり、Unity 6 の
  TextMeshPro パッケージ統合の**影響を受けない**（共通調査 3.5 節）。
- `Resources.FindObjectsOfTypeAll<T>()` は **Obsolete ではない**。
  `Object.FindObjectsOfType` と混同して置換しないこと。**修正不要。**

**対応**: Unity 6 で日本語が正しく表示されるか実機確認する。

### 8. PSD パーサ（✅ 影響なし）

`Core/Parser/PSDLayerAssembler.cs:55` — `new Texture2D(w, h, format, false, linear)`。

PSD バイナリのパース処理は純粋な C# であり、Unity API に依存しない。
C# 言語バージョンは 2022.3 / Unity 6 ともに C# 9 で変更なし。**修正不要。**

### 9. バージョンチェッカー（✅ 影響なし）

`Editor/PSDEditorVersion.cs:97` — `Resources.FindObjectsOfTypeAll<PSDSimpleEditorWindow>()`（Obsolete 対象外）
`Editor/DennokoVersionChecker.cs:119` — `#if UNITY_2020_2_OR_NEWER`（Unity 6 でも true）

**いずれも修正不要。**

## 非該当の確認

| 確認項目 | 結果 |
|---|---|
| `Object.FindObjectsOfType` / `FindObjectOfType` | **なし**（`Resources.FindObjectsOfTypeAll` のみ = Obsolete 対象外） |
| UnityEditor 内部 API へのリフレクション | **なし** |
| `GraphicsFormat.DepthAuto` / `ShadowAuto` / `VideoAuto` | **なし** |
| UI Toolkit の Obsolete API | **なし** |
| Compute シェーダ | **なし**（フラグメントシェーダによる合成） |
| VRChat SDK / NDMF / lilToon 依存 | **なし** |
| `Lightmapping` / 物理 API | **なし** |

## 移行手順

### フェーズ 1（Unity 2022.3.22f1 のまま実施可）

- [ ] 検証用の基準データを作る: 代表的な PSD を数点用意し、2022.3 で書き出した PNG を保存しておく
      （Unity 6 でのピクセル比較に使う）
- [ ] `MenuItem("Tools/Your Tool Name")` というテンプレート由来の未整理メニュー項目の確認

### フェーズ 2（Unity 6 検証プロジェクト・先行実施可）

**外部依存がゼロのため、Unity 6 の空プロジェクトに `DennokoPSDEditor/` を
コピーするだけで完全な検証ができる。共通調査フェーズ 2 の重点対象。**

- [ ] コンパイルエラー・警告が 0 件
- [ ] `LayerBlend.shader` がコンパイルエラーなく読み込まれる

## 検証チェックリスト（Unity 6）

### 描画パイプライン（最重点）

- [ ] PSD を読み込み、レイヤー合成プレビューが正しく表示される
- [ ] **フェーズ 1 で保存した基準 PNG とピクセル一致する**（色空間ずれの検出）
- [ ] 各ブレンドモードの結果が 2022.3 と一致する
- [ ] クリッピングマスクが正しく適用される
- [ ] 調整レイヤー（トーンカーブ / グラデーション / グラデーションフィル）の LUT が正しい
- [ ] カラーレンジマスクが正しく生成される
- [ ] 選択レイヤーのハイライト表示が出る
- [ ] レイヤー単体書き出し / キャンバス全体書き出しの両方が正しい
- [ ] `RenderTexture` プールが正しく解放され、リークしていない
      （長時間操作後に Profiler でメモリを確認）

### UI

- [ ] ウィンドウが開き、**日本語が正しく表示される**（TextCore フォント生成の確認）
- [ ] UI Toolkit の**レイヤーツリー**のインデント・アイコン・行高が崩れていない
- [ ] IMGUI 側のテキストフィールド / Popup / ObjectField の枠線・余白が崩れていない
- [ ] チェッカー背景（透明部分の市松模様）が正しく表示される
- [ ] ウィンドウを閉じた後、他の Editor ウィンドウのスタイルが元に戻っている
