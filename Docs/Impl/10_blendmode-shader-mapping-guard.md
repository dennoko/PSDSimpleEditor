# 10: BlendMode enum ⇔ シェーダー分岐番号の整合を起動時に機械検証

- Issue #5 指摘2「BlendMode 凍結の脆弱性」
- 優先度: 中 (壊れるとコンパイルエラーなしで全ブレンド結果が狂う)
- 規模: 小 (検証用 static クラス 1 つ追加 + 呼び出し 1 箇所)
- 対象ファイル:
  - 新規: `Editor/Compositor/BlendModeMappingValidator.cs`
  - 変更: `Editor/Compositor/LayerCompositor.cs` (コンストラクタ、シェーダー解決直後)
  - 変更 (軽微): `Shader/LayerBlend.shader` (分岐コメントの体裁を揃える場合のみ)

## 背景

`BlendMode` enum の int 値 (`Core/PSDData.cs` 8〜39 行) は LayerBlend.shader の
ブレンド関数分岐 (360〜387 行付近の `if (mode == N)` 連鎖) と 1:1 対応しており、
「変更禁止」の凍結契約になっている。しかし現状この契約を守るのは人間の注意力だけで、
enum の並び替えや分岐の挿入ミスが起きてもコンパイルは通り、合成結果だけが静かに壊れる。

テストは導入しない方針のため、**エディタ実行時にシェーダーソースを読んで照合する
バリデーター**で代替する。

シェーダー側の分岐には既にモード名コメントが付いている:

```hlsl
else if (mode == 1)  return B_Multiply    (cb, cs);   // Multiply
else if (mode == 11) return B_LinearDodge (cb, cs);   // LinearDodge (Add)
else if (mode == 27) return cs;                       // PassThrough (Normal 扱い)
```

コメント先頭の 1 語 (`Multiply`, `LinearDodge`, `PassThrough`) が enum 名と一致する。
この「番号 → コメント名」ペアを抽出し、`System.Enum` の名前⇔値と突き合わせれば、
並び替え・欠落・重複を検出できる。

## 修正内容

### 1. バリデーター新規作成

`Editor/Compositor/BlendModeMappingValidator.cs`:

```csharp
using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace PSDSimpleEditor
{
    /// <summary>
    /// BlendMode enum の int 値と LayerBlend.shader のブレンド分岐番号の
    /// 1:1 対応 (凍結契約) をシェーダーソースの照合で検証する。
    /// ドメインリロードごとに初回 1 回のみ実行。不整合は LogError で報告する
    /// (合成は止めない — 誤検出でツールが使えなくなる方が害が大きい)。
    /// </summary>
    internal static class BlendModeMappingValidator
    {
        static bool s_done;

        // 例: "else if (mode == 11) return B_LinearDodge (cb, cs);   // LinearDodge (Add)"
        //  → 番号 11, 名前 "LinearDodge"
        static readonly Regex BranchPattern = new Regex(
            @"mode\s*==\s*(\d+)\s*\).*?//\s*(\w+)", RegexOptions.Compiled);

        internal static void ValidateOnce(Shader shader)
        {
            if (s_done || shader == null) return;
            s_done = true;

            string path = AssetDatabase.GetAssetPath(shader);
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return; // ビルトイン化等で読めない場合は黙ってスキップ

            string src;
            try { src = File.ReadAllText(path); }
            catch (Exception) { return; }

            int checkedCount = 0;
            foreach (Match m in BranchPattern.Matches(src))
            {
                int    num  = int.Parse(m.Groups[1].Value);
                string name = m.Groups[2].Value;
                if (!Enum.TryParse(name, out BlendMode mode))
                    continue; // enum 名でないコメント (説明文など) は無視
                checkedCount++;
                if ((int)mode != num)
                    Debug.LogError($"[LayerCompositor] BlendMode 凍結契約違反: シェーダー分岐 {num} のコメントは " +
                                   $"{name} だが、enum {name} の値は {(int)mode}。" +
                                   "PSDData.cs と LayerBlend.shader を確認してください。");
            }

            // 分岐の抽出自体に失敗した場合 (シェーダー改修で書式が変わった等) も気付けるようにする
            if (checkedCount < 20)
                Debug.LogWarning($"[LayerCompositor] BlendMode 整合チェック: 照合できた分岐が {checkedCount} 件しかありません。" +
                                 "LayerBlend.shader の分岐コメント書式が変わっていないか確認してください。");
        }
    }
}
```

### 2. 呼び出し追加

`LayerCompositor.cs` コンストラクタのシェーダー解決成功直後 (`_mat` 生成の前後どちらでも可) に 1 行:

```csharp
BlendModeMappingValidator.ValidateOnce(shader);
```

### 3. シェーダー側 (必要な場合のみ)

分岐コメントの先頭 1 語が enum 名と完全一致していることを全 28 分岐 (mode 0〜27) で確認する。
現状は一致しているはずなので、原則シェーダーの変更は不要。もし不一致があれば
**コメント側を enum 名に合わせて直す** (分岐番号や処理は絶対に変えない)。

## 検証方法

1. 通常状態でツールを開き PSD を読み込む → Console にエラー/警告が出ないこと。
2. わざと `LayerBlend.shader` の 1 分岐のコメント名を別のモード名に書き換えて
   ドメインリロード (スクリプト再コンパイル) → ツールを開くと LogError が出ること。確認後に戻す。
3. わざとコメントの `//` 書式を崩して 20 件未満にする → LogWarning が出ること。確認後に戻す。

## 注意事項

- 検証はドメインリロードごとに 1 回だけ。合成のホットパスに入れないこと。
- 不整合を検出しても**例外は投げない** (合成自体は続行し、ログで開発者に知らせるだけ)。
- `Regex` は行単位でなくソース全体に対して回すため、`RegexOptions.Multiline` は不要。
- このクラスは Editor/ 配下なので UnityEditor (AssetDatabase) を使ってよい。
