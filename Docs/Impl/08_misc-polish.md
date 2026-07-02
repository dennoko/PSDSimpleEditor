# 08: 細かい磨き込み (ソリッドテクスチャキャッシュ / パススルー不透明度 UI)

- 優先度: 低
- 規模: 極小 ×2
- 対象ファイル:
  - `Editor/Compositor/LayerCompositor.RT.cs` (`GetSolidTexture`)
  - `Editor/Window/PSDSimpleEditorWindow.LayerPanel.cs` (`DrawGroupNode`)

2 件は独立した修正だが、どちらも数行のため 1 タスクにまとめる。

## (a) ソリッドテクスチャキャッシュの無制限成長

### 背景

`_solidCache` は SoCo / Color Overlay 用の 1×1 テクスチャを **色ごとに** キャッシュする。
SoCo の「塗り色」を ColorField でドラッグ編集すると、ドラッグ中のフレームごとに
微妙に違う色が発生し、1×1 Texture2D が数百個単位で蓄積する
(`Dispose` までリーク。エディタメモリがじわじわ増える)。

### 修正内容

キャッシュに上限を設け、超えたら全クリアして作り直す (1×1 テクスチャは再生成が
安価なので LRU などは不要):

```csharp
const int SolidCacheMax = 64; // ドラッグ編集で無制限に増えるのを防ぐ上限

Texture2D GetSolidTexture(Color color)
{
    if (_solidCache.TryGetValue(color, out var cached) && cached != null)
        return cached;

    // 上限超過時は全破棄してから登録し直す (1×1 なので再生成コストは無視できる)
    if (_solidCache.Count >= SolidCacheMax)
    {
        foreach (var t in _solidCache.Values)
            if (t != null) Object.DestroyImmediate(t);
        _solidCache.Clear();
    }

    var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false, linear: true)
    { hideFlags = HideFlags.HideAndDontSave };
    tex.SetPixel(0, 0, color);
    tex.Apply(false);
    _solidCache[color] = tex;
    return tex;
}
```

### 検証方法

SoCo レイヤーを含む PSD を読み込み、塗り色を長時間ドラッグしてもプレビューが
正常に更新され続けること。Console にエラーが出ないこと。

## (b) パススルーグループの不透明度スライダーが「効かないバグ」に見える

### 背景

パススルーグループの不透明度とマスクは仕様上の割り切りで合成時に無視される
(`LayerCompositor.CompositeGroup` のコメント参照)。しかしレイヤーパネルの
グループカードには常に不透明度スライダーが表示されるため、動かしても何も
変わらず、ユーザーにはバグに見える。

### 修正内容

`DrawGroupNode` のボディ部で、`GroupBlendMode == BlendMode.PassThrough` のときは
スライダーを無効化し、理由をツールチップで示す:

```csharp
// パススルーグループの不透明度は合成で無視される (仕様上の簡略化) ため無効化して明示する
bool isPassThrough = layer.GroupBlendMode == BlendMode.PassThrough;
using (new EditorGUI.DisabledScope(isPassThrough))
{
    DrawOpacitySlider(layer, 0);
}
if (isPassThrough)
{
    EditorGUILayout.BeginHorizontal();
    GUILayout.Space(18f);
    GUILayout.Label("※ パススルー時は不透明度は適用されません",
                    PSDEditorTheme.CaptionStyle);
    EditorGUILayout.EndHorizontal();
}
```

注釈行が冗長に感じる場合はツールチップのみでも可 (DisabledScope は必須)。
`DrawOpacitySlider` 自体は他レイヤーと共用のため変更しないこと。

### 検証方法

1. パススルーグループ: 不透明度スライダーがグレーアウトされること。
2. ブレンドモードを Normal 等へ変えるとスライダーが有効になり、
   ドラッグで合成結果が変化すること。
3. 通常レイヤー・非パススルーグループのスライダー挙動が従来どおりであること。
