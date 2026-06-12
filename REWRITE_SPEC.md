# PSD Simple Editor 再実装 仕様契約書

全モジュールの再実装はこの文書に従う。**「凍結」と記載された項目は変更禁止**（モジュール間の契約のため）。

## 0. 共通ルール

- 既存ファイル名をそのまま上書きする。新しいファイルは追加しない（.meta の GUID 維持と、シェーダーパス固定のため）。.meta ファイルには触れない。
- 外部ライブラリ禁止。C# 標準 (System.IO.Compression 含む) と UnityEngine/UnityEditor のみ使用可。
- コメント・UI 文言は日本語。
- 例外を投げるのは致命的エラー（シグネチャ不正・未対応バージョン）のみ。レイヤー単位の失敗は `Debug.LogWarning` + スキップで続行する。
- **ストリーム位置は常に長さフィールドによる境界 seek で管理する。** 未知データ・パース失敗でストリーム位置がずれてはならない（旧実装の主要な不具合原因）。各ブロックの読み取り前に `end = pos + len` を計算し、処理後は必ず `Seek(end)` する。
- 対応範囲: PSD version 1 / 8bit・16bit / RGB (3) を主対象。Grayscale (1) は R 複製で対応。CMYK/LAB は警告を出してマージ画像のみ表示。version 2 (PSB) と 32bit は明確なエラーメッセージで中断。

## 1. ファイル責務

| ファイル | 責務 |
|---|---|
| BigEndianBinaryReader.cs | ビッグエンディアン読み取り・Pascal/Unicode 文字列 |
| PSDData.cs | データモデル（**作成済み・変更禁止**） |
| PSDParser.cs | PSD バイナリ → PSDFile（テクスチャ構築含む） |
| LayerBlend.shader | 1 パスのブレンド/マスク/色調補正シェーダー |
| LayerCompositor.cs | レイヤーツリーの GPU 再帰合成 |
| PSDSimpleEditorWindow.cs | EditorWindow UI・PNG 出力 |

## 2. BlendMode 番号と PSD キー対応【凍結】

enum の int 値はシェーダーの分岐番号と 1:1 対応。

| enum (int) | PSD キー | | enum (int) | PSD キー |
|---|---|---|---|---|
| Normal=0 | `norm` | | SoftLight=13 | `sLit` |
| Multiply=1 | `mul ` | | HardLight=14 | `hLit` |
| Screen=2 | `scrn` | | VividLight=15 | `vLit` |
| Overlay=3 | `over` | | LinearLight=16 | `lLit` |
| Dissolve=4 | `diss` | | PinLight=17 | `pLit` |
| Darken=5 | `dark` | | HardMix=18 | `hMix` |
| ColorBurn=6 | `idiv` | | Difference=19 | `diff` |
| LinearBurn=7 | `lbrn` | | Exclusion=20 | `smud` |
| DarkerColor=8 | `dkCl` | | Subtract=21 | `fsub` |
| Lighten=9 | `lite` | | Divide=22 | `fdiv` |
| ColorDodge=10 | `div ` | | Hue=23 | `hue ` |
| LinearDodge=11 | `lddg` | | Saturation=24 | `sat ` |
| LighterColor=12 | `lgCl` | | Color=25 | `colr` |
| | | | Luminosity=26 | `lum ` |
| | | | PassThrough=27 | `pass` |

未知キーは `Unknown=99`（合成時は Normal 扱い）。

## 3. シェーダー uniform インターフェース【凍結】

シェーダー名: `PSDSimpleEditor/LayerBlend`。1 SubShader / 1 Pass。`Cull Off ZWrite Off ZTest Always`。

| uniform | 型 | 意味 |
|---|---|---|
| _MainTex | 2D | 背景（Graphics.Blit のソース） |
| _LayerTex | 2D | レイヤー画像 |
| _MaskTex | 2D | レイヤーマスク（.r を参照） |
| _ClipMaskTex | 2D | クリッピングマスク（キャンバス全面 RT、.a を参照） |
| _CanvasSize | Vector | (キャンバス幅, 高さ, 0, 0) px |
| _LayerRect | Vector | (L, T, W, H) — PSD 座標（左上原点）px |
| _MaskRect | Vector | (L, T, W, H) — 同上 |
| _MaskDefault | Float | マスク矩形外の値 0..1 |
| _Opacity | Float | 0..1 |
| _BlendMode | Int | §2 の番号 |
| _IsAdjustment | Int | 1=調整パス（_LayerTex 不使用、背景へ色調補正を適用。適用範囲はマスク/クリップで制御） |
| _HasMask / _HasClipMask | Int | 0/1 |
| _Brightness | Float | -1..1（実値 / 150） |
| _Contrast | Float | -1..1（実値 / 100） |
| _Hue | Float | -1..1（実値 / 180） |
| _Saturation / _Lightness | Float | -1..1（実値 / 100） |

通常パス (_IsAdjustment=0) でも色調補正 uniform はレイヤー色に適用する（グループを 1 枚として合成する際に使用）。

### 座標規約【凍結】

- パーサーが構築する Texture2D は**上下反転済み**（Unity 標準向き。UV (0,0)=画像左下）。
- シェーダー内: `psdX = uv.x * _CanvasSize.x` / `psdY = (1 - uv.y) * _CanvasSize.y`
- レイヤー UV: `lu = (psdX - L) / W` / `lv = 1 - (psdY - T) / H`。lu/lv が [0,1] 外なら α=0（マスクは _MaskDefault）。

### 合成式【凍結】

ストレートアルファ。Photoshop / W3C compositing 準拠:

```
αo = αs + αb·(1-αs)
Co = ( αs·(1-αb)·Cs + αs·αb·B(Cb,Cs) + (1-αs)·αb·Cb ) / αo   (αo=0 なら 0)
```

αs にはレイヤー α × _Opacity × マスク値 × クリップマスク値を畳み込む。B はブレンド関数（Photoshop 準拠の式。SoftLight は W3C/PDF 式。Hue/Sat/Color/Lum は Lum()/Sat()/SetLum()/SetSat()/ClipColor() による非分離型。Dissolve はハッシュノイズ近似で可）。

### 色空間【凍結】

- 全 Texture2D は `linear: true`、全 RenderTexture は `RenderTextureReadWrite.Linear` で作成し、**GPU の sRGB 変換を一切通さない**。
- ブレンド演算は sRGB 値（バイト値そのまま）で行う = Photoshop 互換。
- これにより ReadPixels → EncodeToPNG がバイト値そのまま出力される。
- Linear カラースペースのプロジェクトでの二重変換（旧実装の不具合候補）を避けるのが目的。

## 4. LayerCompositor 公開 API【凍結】

```csharp
public LayerCompositor(int canvasW, int canvasH)
public bool IsValid { get; }
public Texture2D Composite(List<PSDLayer> layers)  // index 0 = 最下層
public void Dispose()
```

シェーダーは `AssetDatabase.LoadAssetAtPath<Shader>("Assets/Editor/PSDSimpleEditor/LayerBlend.shader")` で取得。

### 合成ロジック要件

- レイヤーリストは index 0 = 最下層。昇順に合成。
- ピンポン RT + グループ用 RT プール。
- グループ: `PassThrough` → 子を現在のバッファへ直接合成。それ以外 → 透明 RT に子を合成後、1 枚のレイヤーとしてグループの BlendMode / Opacity / マスクを適用して合成。
- クリッピング: 連続する `IsClipping==true` 群を直下のベース層でクリップ。ベース層を透明 RT へ単独描画して得た α をクリップマスク (_ClipMaskTex) としてクリッピング層に渡す。ベース層自体は通常どおり合成。非表示ベース層の場合はクリッピング群ごとスキップ。
- 調整レイヤー（`IsAdjustmentLayer && !HasSolidColor`）: 現在の合成バッファへ補正適用（自然にグループスコープが効く）。
- SoCo: 1×1 ソリッドテクスチャを全面レイヤーとして合成（キャッシュ可）。
- Dispose で Material / RT / キャッシュテクスチャを全て DestroyImmediate。生成オブジェクトは `HideFlags.HideAndDontSave`。

## 5. パーサー要件チェックリスト

### Section 1: ヘッダ
`8BPS` / version==1（2 は「PSB 非対応」エラー）/ 予約 6B / channels / height / width / depth(8 or 16。32 はエラー) / colorMode。

### Section 2, 3
長さ uint32 を読んでスキップ。

### Section 4: レイヤーとマスク情報
- sectionLen uint32（0 → レイヤーなし）。sectionEnd を保持し最後に必ず seek。
- layerInfoLen uint32 → layerInfoEnd。
- count int16。**負値は abs**（最下層が透明 α を持つ印）。
- レイヤーレコード × count（ファイル格納順 = 最下層 → 最上層）:
  - rect: top, left, bottom, right (各 int32)
  - チャンネル数 uint16 → 各 (id int16, dataLen uint32)。id: 0=R 1=G 2=B -1=A -2=ユーザーマスク -3=ベクターマスク
  - `8BIM` 検証 + ブレンドキー 4B
  - opacity 1B / clipping 1B (≠0 → IsClipping) / flags 1B / filler 1B
  - **可視判定: `IsVisible = (flags & 0x02) == 0`**（bit1 が立っていたら非表示）
  - extraLen uint32 → **extraEnd = pos + extraLen。レコード末尾で必ず seek(extraEnd)**
    - マスクデータ: len uint32 (0/20/36...)。len>=20 なら rect(4×int32)・defaultColor 1B・flags 1B を読み、残りは seek で飛ばす。flags bit0=位置がレイヤー相対（→ left/top を加算して**絶対座標に変換して格納**）、bit1=マスク無効。
    - ブレンディングレンジ: len uint32 → skip
    - Pascal 名: 1B 長 + 文字列、**(1+len) を 4 の倍数にパディング**（Shift-JIS の可能性があるため後述 luni を優先）
    - 追加情報ループ（pos < extraEnd - 8 の間）: sig (`8BIM` or `8B64`) → key 4B → len uint32 → **dataEnd = pos + len（奇数なら +1 パディング）→ 処理後 seek(dataEnd)**。sig が一致しなければ即 seek(extraEnd) して脱出。
      - `luni`: count uint32 → UTF-16BE × count 文字 → **Name を上書き（日本語名対応・最重要）**
      - `lsct`: type uint32 (0=通常 1=開フォルダ 2=閉フォルダ 3=終端マーカー)。len>=12 なら追加で sig+blendkey（グループのブレンドモード）
      - `brit`: brightness int16, contrast int16
      - `CgEd`: ディスクリプタから `Brghtnss`/`Cntrst` を best effort で抽出し brit を上書き（失敗時は無視）
      - `hue2`: version uint16, colorization 1B, pad 1B → master hue/sat/lightness 各 int16（colorization=0 時）
      - `SoCo`: ディスクリプタ version uint32(=16) → `Clr ` (Objc/RGBC) 内の `Rd  `/`Grn `/`Bl  ` (doub, 0..255) を抽出
      - `lfx2`/`lrFX`: Color Overlay を best effort（失敗時はスキップ。実装困難なら未対応で可）
      - その他: skip
- チャンネル画像データ（レイヤー順 × チャンネル順に連続格納）:
  - channelStart = pos。各チャンネル末尾で **seek(channelStart + dataLen)** 必須。
  - 圧縮 uint16 + データ (dataLen-2)。
  - **使用する矩形: id==-2 → マスク矩形 / id==-3 → 読まずに skip / その他 → レイヤー矩形**（旧実装の不具合候補）。幅か高さが 0 なら skip。
  - 0=Raw: h×w×(depth/8) バイト
  - 1=RLE: h × uint16 行バイト数 → 行ごとに PackBits 解凍（出力 w×(depth/8) バイト/行）
  - 2=ZIP: zlib ストリーム（**先頭 2B の zlib ヘッダを skip して DeflateStream**）
  - 3=ZIP+prediction: 解凍後、行ごとに横方向デルタ復元（16bit はビッグエンディアン uint16 単位）
  - 16bit→8bit: 上位バイト採用（BE なので各ペアの先頭バイト）
- レイヤー情報後: グローバルレイヤーマスク情報等が続くが、layerInfoEnd / sectionEnd への seek で処理。

### テクスチャ構築
- レイヤー: `Texture2D(w, h, TextureFormat.RGBA32, false, linear: true)`、**上下反転**コピー、α チャンネル無しは 255。Grayscale は R を GB に複製。
- マスク: `TextureFormat.R8, linear: true`、上下反転。
- 構築後 `_rawPixels` 等は null 化してメモリ解放。

### グループツリー構築
- ファイル格納順（最下層→最上層）に走査。`lsct type 3`（終端マーカー）→ 新しいグループのスコープ開始（スタックに push）。`type 1/2` → スタックを pop してそのレイヤーをフォルダ（Children 確定、IsExpanded = type==1）にする。マーカー自体はツリーに含めない。
- 結果: `PSDFile.Layers` も各 `Children` も **index 0 = 最下層**。
- UI 初期値: UIVisible=IsVisible, UIOpacity=Opacity/255f, UI 調整値=Adjustment の値。

### Section 5: マージ済み画像
- 圧縮 uint16（全体で 1 つ）。RLE の場合、**全チャンネル分の行カウント (channels × height × uint16) が先頭にまとめて**格納され、その後チャンネルごと（planar）にデータが続く。
- channels>=4 なら 4ch 目を α に。失敗しても警告のみで続行。

### デバッグ
`PSDParser.Parse(path, bool verbose=false)` のオーバーロードか静的フラグで、レイヤー名/矩形/モード/不透明度/可視/セクション種別のダンプログを出せること。

## 6. Window 要件

- メニュー: `dennokoworks/PSD Simple Editor`
- ツールバー: パス入力 / Browse... / Load / マージ参照トグル
- レイヤーパネル（左・固定幅 ~270px）: ツリー表示（フォルダ折りたたみ）、**上が最上層**（リストを逆順描画）、表示トグル / 不透明度 / brit / hue2 スライダー / SoCo カラー / マスク有効状態。クリッピングやマスクはアイコン的プレフィックスで表示。
- プレビューパネル（右）: アスペクト維持。透明部可視化のためチェッカー背景を描画。マージ参照は右下に小窓オーバーレイ。
- 変更時は `_needsRecomposite = true` → Repaint イベント中に合成実行。
- 下部バー: 画像情報 + Export PNG... ボタン（EncodeToPNG）。
- Load は進捗バー + try/catch + エラーダイアログ。Cleanup で全テクスチャ・コンポジターを破棄。
