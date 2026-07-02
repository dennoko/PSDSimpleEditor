# 03: グラデーションマップ (grdm) version 3 対応

- 優先度: **高** (新しめの Photoshop で作った PSD でグラデーションマップが黙って落ちる)
- 規模: 極小 (数行)
- 対象ファイル: `Core/Parser/PSDAdditionalInfoParser.cs` (`HandleGradientMap`, 現在 236 行付近)

## 背景 (バグ内容)

Photoshop 2020 前後から、grdm ブロックは version 3 で書き出され、
`dithered` の直後に **4 バイトの method フィールド** (グラデーション補間方式:
perceptual / linear / classic を表す 4 文字キー) が挿入される
(psd-tools `GradientMap.read`: `read_fmt("H2B")` → `if version == 3: read_fmt("4s")` と照合済み)。

現在の実装は version を読み捨てて常に version 1 のレイアウトを仮定しているため、
version 3 のファイルでは直後の Unicode 文字列 (グラデーション名) の読み出しが
4 バイトズレる。その結果、名前の「文字数」として巨大な値を読んで
`ReadUtf16BE` の上限チェック (4M 文字) に引っかかって例外 → 呼び出し側で警告 +
グラデーションマップ無効化、あるいはカラーストップ数の境界チェックで途中 return となり、
**グラデーションマップ調整レイヤーが黙って効かなくなる**。

## 修正内容

`HandleGradientMap` の先頭部分:

```csharp
// 修正前
r.ReadUInt16();                 // version (=1)
bool reverse = r.ReadByte() != 0;
r.ReadByte();                   // dithered (プレビューには未使用)
r.ReadUnicodeString();          // グラデーション名 (未使用)

// 修正後
ushort version = r.ReadUInt16(); // 1 または 3 (3 は method 4B が追加される)
bool reverse = r.ReadByte() != 0;
r.ReadByte();                   // dithered (プレビューには未使用)
if (version >= 3)
    r.ReadAscii(4);             // method (Perc/Lnr /Gcls 等の補間方式キー。プレビューには未使用)
r.ReadUnicodeString();          // グラデーション名 (未使用)
```

先頭のコメントブロック (「grdm バイナリ構造」の説明) にも
`version 3 では dithered の後に method(4B) が入る` 旨を追記すること。

## 検証方法

1. 新しめの Photoshop (2020 以降 / CC 2022+ 推奨) でグラデーションマップ調整レイヤー
   (例: 黒→白以外の 3 色グラデ) を含む PSD を作成。
2. 読み込んで、レイヤーパネルの「グラデーションマップ」トグルが ON になり、
   GradientField に PSD どおりの色が並ぶことを確認。
3. verbose ダンプ / Console に grdm 関連の警告が出ないこと。
4. 旧形式 (version 1) の PSD も引き続き読めること (回帰確認)。古い Photoshop が
   無ければ、修正前に正常に読めていた既存のテスト PSD で確認すればよい。

## 注意事項

- version が 1 でも 3 でもない値だった場合も、境界チェック
  (`if (r.Position + 2 > end) return;` 等) が既にあるため追加のガードは不要。
  ただし `version >= 3` の条件にしておくと将来の亜種にもズレにくい。
- method の値そのもの (補間方式) はプレビュー品質にほぼ影響しないため対応不要。
  読み飛ばすだけでよい。
