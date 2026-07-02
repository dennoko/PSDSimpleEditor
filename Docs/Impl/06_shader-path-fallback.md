# 06: シェーダーパスのハードコード解消 (フォールバック追加)

- 優先度: 中 (フォルダ名変更・移動でツール全体が機能停止する単一障害点)
- 規模: 小 (コンストラクタ内のロード処理 1 箇所)
- 対象ファイル: `Editor/Compositor/LayerCompositor.cs` (コンストラクタ / `ShaderPath` 定数、現在 28 行・55 行付近)

## 背景

```csharp
const string ShaderPath = "Assets/dennokoworks/DennokoPSDEditor/Shader/LayerBlend.shader";
```

シェーダーを絶対アセットパスでロードしているため、配布先ユーザーがフォルダを
移動・リネームすると `LayerCompositor.IsValid == false` になり合成が一切動かなくなる。
実際、過去のフォルダ名変更 (`PSDSimpleEditor` → `DennokoPSDEditor`) の際にこの定数の
更新漏れが起きうる構造で、CLAUDE.md / REWRITE_SPEC.md の記載パスとも食い違っている。

シェーダー自体は `Shader "PSDSimpleEditor/LayerBlend"` という固有名を宣言しているので、
名前ベースの解決とアセット検索でフォールバックできる。

## 修正内容

コンストラクタのロード部分を 3 段フォールバックにする:

```csharp
var shader = LoadLayerBlendShader();
if (shader == null)
{
    Debug.LogError("[LayerCompositor] LayerBlend.shader が見つかりません。" +
                   "PSDSimpleEditor フォルダ内の Shader/LayerBlend.shader を確認してください。");
    return;
}
```

```csharp
/// <summary>
/// LayerBlend.shader を取得する。
/// 1) 既定パス → 2) シェーダー名 (Shader.Find) → 3) AssetDatabase 全体検索 の順で解決する
/// (フォルダ移動・リネームされた配布環境でも動作させるため)。
/// </summary>
static Shader LoadLayerBlendShader()
{
    // 1) 既定パス (最速)
    var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
    if (shader != null) return shader;

    // 2) シェーダー宣言名で解決 (コンパイル済みならフォルダ位置に依存しない)
    shader = Shader.Find("PSDSimpleEditor/LayerBlend");
    if (shader != null) return shader;

    // 3) アセット検索 (リネーム・移動された場合の最終手段)
    foreach (string guid in AssetDatabase.FindAssets("LayerBlend t:Shader"))
    {
        string path = AssetDatabase.GUIDToAssetPath(guid);
        var s = AssetDatabase.LoadAssetAtPath<Shader>(path);
        if (s != null && s.name == "PSDSimpleEditor/LayerBlend") return s;
    }
    return null;
}
```

あわせて:

- クラス先頭コメントの「Loads the shader by hardcoded path ...」相当の記述
  (CLAUDE.md の Architecture 3 項) を「既定パス + 名前検索フォールバック」に更新する。
- `ShaderPath` 定数は 1) 用にそのまま残してよい。

## 検証方法

1. 通常状態でツールを開き PSD を読み込み、プレビューが従来どおり表示されること。
2. 一時的に `ShaderPath` 定数をわざと存在しないパス (例: 末尾に `_x` を付ける) に変えて
   コンパイルし、それでもプレビューが表示されること (フォールバック 2/3 の確認)。
   確認後は元に戻す。
3. Console にエラー/警告が出ないこと。

## 注意事項

- `Shader.Find` はプロジェクト内でシェーダーがコンパイルされていれば動くが、
  インポート直後の一瞬などで null になり得るため、3) の AssetDatabase 検索まで
  用意しておく。
- 3) の検索は `s.name` (シェーダー宣言名) で照合すること。ファイル名 `LayerBlend` だけで
  一致させると、ユーザープロジェクト内の同名別シェーダーを誤って掴む恐れがある。
