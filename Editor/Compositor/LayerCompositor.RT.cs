using UnityEngine;

namespace PSDSimpleEditor
{
    // ════════════════════════════════════════════════════════════════
    //  RenderTexture / ソリッドテクスチャ ユーティリティ
    // ════════════════════════════════════════════════════════════════
    // ─── partial 見取り図 ───────────────────────────────────────────
    // 責務   : RenderTexture およびソリッドテクスチャの生成・プール管理・破棄
    // 宣言   : SolidCacheMax
    // 参照   : _canvasW (R), _canvasH (R), _pool (RW), _solidCache (RW)
    // 依存   : なし
    // ────────────────────────────────────────────────────────────────
    public partial class LayerCompositor
    {
        RenderTexture CreateRT()
        {
            // sRGB 変換を通さない Linear RT (REWRITE_SPEC.md §3 色空間【凍結】)
            var rt = new RenderTexture(_canvasW, _canvasH, 0,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
            { hideFlags = HideFlags.HideAndDontSave };
            rt.Create();
            return rt;
        }

        RenderTexture AcquireRT(bool clearToTransparent)
        {
            var rt = _pool.Count > 0 ? _pool.Pop() : CreateRT();
            if (clearToTransparent) ClearRT(rt);
            return rt;
        }

        void ReleaseToPool(RenderTexture rt)
        {
            if (rt == null) return;
            _pool.Push(rt);
        }

        static void ClearRT(RenderTexture rt)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            GL.Clear(true, true, Color.clear);
            RenderTexture.active = prev;
        }

        static void Swap(ref RenderTexture a, ref RenderTexture b)
        {
            var t = a; a = b; b = t;
        }

        static void ReleaseRT(ref RenderTexture rt)
        {
            if (rt == null) return;
            rt.Release();
            Object.DestroyImmediate(rt);
            rt = null;
        }

        const int SolidCacheMax = 64; // ドラッグ編集で無制限に増えるのを防ぐ上限

        // SoCo / Color Overlay 用の 1×1 ソリッドテクスチャ (色ごとにキャッシュ)
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
    }
}
