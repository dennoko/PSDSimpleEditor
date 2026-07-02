# 05: PSD 書き出し: チャンネル 0 本レコードの回避 + 調整レイヤー消失の警告

- 優先度: **中** (書き出した PSD の互換性リスク + ユーザーが気づかないデータ消失)
- 規模: 小
- 対象ファイル:
  - `Editor/Writer/PSDExportRecordBuilder.cs` (`BuildPixelRecord`)
  - `Editor/Window/PSDSimpleEditorWindow.Export.cs` (`ExportPSD`)

## 背景 (バグ内容)

### (a) チャンネル 0 本のレイヤーレコード

`BuildPixelRecord` は `layer.Texture == null` かつマスクも無いレイヤー
(= 調整レイヤー、SoCo べた塗り、グラデーションマップ等のゼロ面積レイヤー) を
**チャンネルリストが空 (count = 0) のレコード** として出力する。
PSD のレイヤーレコードは RGB モードなら最低 R/G/B (+A) のチャンネルエントリを
持つのが通例で、チャンネル 0 本は他アプリ (Photoshop / CSP / krita 等) で
読み込みエラーや不正表示の原因になり得る。

フォルダ/終端マーカー用には既に `EmptyChannels()` (R,G,B,A の空 4ch、
各 `Data = {0,0}` = compression 0 / payload なし) が存在するので、これを流用する。

### (b) 調整レイヤーの内容が黙って消える

現状の書き出しは brit/hue2/levl/curv/thrs/post/blnc/grdm/SoCo などの調整キーと
lfx2 (Color Overlay) を一切書き戻さないため、書き出した PSD を Photoshop で開くと
調整が全て失われ見た目が変わる。フル対応 (キーの書き戻し) は別タスクとするが、
**少なくとも書き出し時にユーザーへ警告** して「知らないうちに消えた」を防ぐ。

## 修正内容

### (a) PSDExportRecordBuilder.BuildPixelRecord

ピクセルもマスクも追加されなかった場合に `EmptyChannels()` を入れる。
メソッド末尾 (return 直前) に追加:

```csharp
// ピクセルもマスクも無いレイヤー (調整レイヤー等) はチャンネル 0 本になり
// 他アプリで読み込みエラーの原因になるため、空の R/G/B/A チャンネルを入れる
if (rec.Channels.Count == 0)
    rec.Channels = EmptyChannels();
```

注意: マスクだけ持つレイヤー (Texture == null かつ HasMask) は現状
`-2` チャンネルのみのレコードになる。この場合も R/G/B/A が欠けるため、
`EmptyChannels()` を **先頭に挿入** して `-2` を末尾に残す形にするのがより安全:

```csharp
// R/G/B/A が 1 つも無ければ空チャンネルを先頭へ補う (-2 マスクのみのレコード対策)
bool hasColorChannel = rec.Channels.Exists(c => c.Id >= -1);
if (!hasColorChannel)
    rec.Channels.InsertRange(0, EmptyChannels());
```

こちらの形 (hasColorChannel 判定) を採用すれば上の単純版は不要。

### (b) PSDSimpleEditorWindow.ExportPSD に警告を追加

`PSDWriter.Save(...)` を呼ぶ **前** に、保持できない情報を持つレイヤーを数えて
確認ダイアログを出す:

```csharp
int lossCount = CountUnsupportedForPsdExport(_psdFile.Layers);
if (lossCount > 0)
{
    bool proceed = EditorUtility.DisplayDialog("PSD 書き出しの注意",
        $"調整レイヤー・べた塗り・レイヤー効果など、PSD 書き出しで保持できない" +
        $"設定を持つレイヤーが {lossCount} 件あります。\n" +
        "これらは空のレイヤーとして書き出され、Photoshop で開くと見た目が変わる可能性があります。\n\n" +
        "続行しますか?",
        "書き出す", "キャンセル");
    if (!proceed) return;
}
```

カウント用ヘルパー (同 partial class 内に追加。ツリー再帰):

```csharp
/// <summary>PSD 書き出しで内容を保持できないレイヤー (調整/SoCo/効果) を数える。</summary>
static int CountUnsupportedForPsdExport(List<PSDLayer> layers)
{
    if (layers == null) return 0;
    int count = 0;
    foreach (var l in layers)
    {
        var a = l.Adjustment;
        bool hasUnsupported =
            (l.IsAdjustmentLayer && a != null &&
             (a.HasBrightnessContrast || a.HasHueSaturation || a.HasSolidColor ||
              a.HasInvert || a.HasThreshold || a.HasPosterize || a.HasLevels ||
              a.HasCurves || a.HasGradientMap || a.HasColorBalance))
            || (l.Effects != null && l.Effects.HasColorOverlay);
        if (hasUnsupported) count++;
        count += CountUnsupportedForPsdExport(l.Children);
    }
    return count;
}
```

※ ピクセルレイヤーに乗せた非破壊補正 (UIBrightness 等) は
`RenderLayerForExport` で焼き込まれるため対象外。あくまで
「ゼロ面積の調整レイヤー / SoCo / Color Overlay」だけを数える。

## 検証方法

1. 調整レイヤー (明るさ・コントラスト等) と SoCo を含む PSD を読み込み、PSD 書き出し。
   - 警告ダイアログが件数付きで表示されること。キャンセルで中断できること。
2. 書き出した PSD を本ツールで再読み込みしてエラーが出ないこと。
3. 可能なら Photoshop / CSP で書き出した PSD を開き、レイヤー構造が壊れていないこと
   (調整レイヤーは空レイヤーになるが、他レイヤー・グループ・マスクは保持される)。
4. 調整レイヤーを含まない PSD では警告が出ずに従来どおり書き出せること (回帰確認)。

## 注意事項

- `EmptyChannels()` は `PSDExportRecordBuilder` 内の既存 private メソッド。流用する。
- ダイアログ文言は既存 UI と同じく日本語・である調ではなく丁寧語で。
- SoCo / 調整キーを実際に書き戻すフル対応は本タスクの範囲外 (別途中規模タスク)。
