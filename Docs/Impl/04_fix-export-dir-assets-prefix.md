# 04: 出力先フォルダの "Assets" プレフィックス誤判定の修正

- 優先度: 低 (発生条件が限定的だが、発生すると意図しない場所へファイルが作られる)
- 規模: 極小 (条件式 1 箇所)
- 対象ファイル: `Editor/Window/PSDSimpleEditorWindow.Export.cs` (`GetUniqueExportPath`, 現在 160 行付近)

## 背景 (バグ内容)

```csharp
if (dir.StartsWith("Assets", StringComparison.OrdinalIgnoreCase))
{
    string subPath = dir.Substring(6).TrimStart('/', '\\');
    targetDir = Path.Combine(Application.dataPath, subPath);
}
```

この判定は `Assets/Foo` だけでなく **`AssetsBackup` や `assets_export` のような
「Assets で始まるだけの別フォルダ名」にもマッチ**してしまう。
例: 出力先に相対パス `AssetsBackup` を指定すると `<プロジェクト>/Assets/Backup` に
展開され、意図しない場所にフォルダが作成される。

## 修正内容

「`Assets` 単体」または「`Assets/` (либо `Assets\`) で始まる」場合のみ
プロジェクト内相対パスとして扱う:

```csharp
bool isAssetsRelative =
    dir.Equals("Assets", StringComparison.OrdinalIgnoreCase) ||
    dir.StartsWith("Assets/",  StringComparison.OrdinalIgnoreCase) ||
    dir.StartsWith("Assets\\", StringComparison.OrdinalIgnoreCase);
if (isAssetsRelative)
{
    string subPath = dir.Substring(6).TrimStart('/', '\\');
    targetDir = Path.Combine(Application.dataPath, subPath);
}
```

## 検証方法

1. 出力先を `Assets/PSDSE_exported` (既定値) にして PNG 書き出し →
   従来どおり `<プロジェクト>/Assets/PSDSE_exported/` に出力されること。
2. 出力先を `Assets` にして書き出し → `<プロジェクト>/Assets/` 直下に出力されること。
3. 出力先を絶対パス (例: `C:/temp/psdout`) にして書き出し → そのまま絶対パスに
   出力されること (回帰確認)。
4. 出力先を `AssetsBackup` のような相対名にした場合、`Path.GetFullPath` により
   カレントディレクトリ (プロジェクトルート) 基準の `<プロジェクト>/AssetsBackup/` に
   解決されること (従来の `Assets/Backup` 誤変換が起きないこと)。

## 注意事項

- 同ファイル内の PNG/TGA/PSD 書き出しと色域選択マスク出力
  (`PSDSimpleEditorWindow.ColorRangeMask.cs`) はすべて `GetUniqueExportPath` を
  経由するため、修正はこの 1 箇所でよい。
