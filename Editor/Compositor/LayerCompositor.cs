using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace PSDSimpleEditor
{
    /// <summary>
    /// レイヤーツリーを LayerBlend.shader で GPU 合成し、最終結果の Texture2D を返す。
    ///
    /// RT の流れ:
    ///   ・トップレベルはピンポン RT (_rtA/_rtB)。cur = 現在の合成結果、next = 次の書き込み先。
    ///     1 レイヤー合成するごとに Graphics.Blit(cur, next, _mat) → Swap(cur, next)。
    ///   ・分離グループ (PassThrough 以外) は RT プールから 2 枚借りて透明クリア後に
    ///     子を再帰合成し、平坦化結果を「1 枚のレイヤー」として親バッファへ合成する。
    ///   ・クリッピングは、ベース層を透明 RT へ単独描画 (Normal ブレンド) して実効 α を
    ///     得た RT を _ClipMaskTex としてクリッピング層へ渡す。
    ///
    /// 色空間: RT は全て RenderTextureReadWrite.Linear、戻り Texture2D も linear:true。
    /// 合成中は GL.sRGBWrite = false にして sRGB 変換が混入しないようにする
    /// (Linear カラースペースプロジェクトでの二重変換対策。REWRITE_SPEC.md §3 色空間)。
    ///
    /// 実装は uniform 設定 (LayerCompositor.Params.cs) / 書き出し用レンダリング
    /// (LayerCompositor.Export.cs) / RenderTexture プール管理 (LayerCompositor.RT.cs) へ
    /// partial class として分割されている。このファイルは合成パイプラインの本体を担う。
    /// </summary>
    public partial class LayerCompositor
    {
        const string ShaderPath = "Assets/dennokoworks/DennokoPSDEditor/Shader/LayerBlend.shader";

        readonly int _canvasW;
        readonly int _canvasH;

        Material _mat;

        // トップレベルのピンポン RT
        RenderTexture _rtA, _rtB;

        // グループ合成・クリップマスク用の RT プール (全てキャンバスサイズ)
        readonly Stack<RenderTexture> _pool = new Stack<RenderTexture>();

        // SoCo / Color Overlay 用 1×1 ソリッドテクスチャのキャッシュ (色ごと)
        readonly Dictionary<Color, Texture2D> _solidCache = new Dictionary<Color, Texture2D>();

        public bool IsValid => _mat != null && _rtA != null && _rtB != null;

        // ════════════════════════════════════════════════════════════════
        //  初期化 / 解放
        // ════════════════════════════════════════════════════════════════

        public LayerCompositor(int canvasW, int canvasH)
        {
            _canvasW = Mathf.Max(1, canvasW);
            _canvasH = Mathf.Max(1, canvasH);

            var shader = LoadLayerBlendShader();
            if (shader == null)
            {
                Debug.LogError("[LayerCompositor] LayerBlend.shader が見つかりません。" +
                               "PSDSimpleEditor フォルダ内の Shader/LayerBlend.shader を確認してください。");
                return;
            }

            _mat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            _rtA = CreateRT();
            _rtB = CreateRT();
        }

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

        public void Dispose()
        {
            if (_mat != null) { Object.DestroyImmediate(_mat); _mat = null; }
            ReleaseRT(ref _rtA);
            ReleaseRT(ref _rtB);

            while (_pool.Count > 0)
            {
                var rt = _pool.Pop();
                if (rt != null) { rt.Release(); Object.DestroyImmediate(rt); }
            }

            foreach (var tex in _solidCache.Values)
                if (tex != null) Object.DestroyImmediate(tex);
            _solidCache.Clear();
        }

        // ════════════════════════════════════════════════════════════════
        //  Public: 合成実行
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// layers (index 0 = 最下層) を昇順に合成して Texture2D (RGBA32, linear) を返す。
        /// 返却テクスチャの破棄は呼び出し側の責任。
        /// </summary>
        public Texture2D Composite(List<PSDLayer> layers)
        {
            if (!IsValid) return null;

            // sRGB 変換の混入を防ぐ (Linear RT へバイト値そのまま書き込む)
            bool prevSRGBWrite = GL.sRGBWrite;
            var  prevActive    = RenderTexture.active;
            GL.sRGBWrite = false;

            try
            {
                ClearRT(_rtA);
                RenderTexture cur = _rtA, next = _rtB;

                if (layers != null)
                    CompositeList(layers, ref cur, ref next);

                // cur が最終結果 → Texture2D (sRGB) へ転写
                var result = new Texture2D(_canvasW, _canvasH, TextureFormat.RGBA32, false, linear: false)
                { hideFlags = HideFlags.HideAndDontSave };
                RenderTexture.active = cur;
                result.ReadPixels(new Rect(0, 0, _canvasW, _canvasH), 0, 0);
                result.Apply(false);
                return result;
            }
            finally
            {
                RenderTexture.active = prevActive;
                GL.sRGBWrite = prevSRGBWrite;
            }
        }

        // ════════════════════════════════════════════════════════════════
        //  レイヤーリストの合成 (グループ再帰 + クリッピング群の検出)
        // ════════════════════════════════════════════════════════════════

        void CompositeList(List<PSDLayer> layers, ref RenderTexture cur, ref RenderTexture next)
        {
            int i = 0;
            while (i < layers.Count)
            {
                var baseLayer = layers[i];

                // 直後に連続する IsClipping==true 群を特定する。
                // baseLayer 自身がクリッピング層の場合 (リスト先頭の孤児) は群を作らず
                // 通常レイヤーとして扱う (クリップ対象なし)。
                int runEnd = i + 1;
                if (!baseLayer.IsClipping)
                    while (runEnd < layers.Count && layers[runEnd].IsClipping) runEnd++;

                // ベース層が非表示ならクリッピング群ごとスキップ
                if (!baseLayer.UIVisible) { i = runEnd; continue; }

                bool hasClipRun = runEnd > i + 1;

                if (baseLayer.Children != null)
                {
                    CompositeGroup(layers, i, runEnd, ref cur, ref next);
                }
                else
                {
                    // ── 通常レイヤー (+ クリッピング群) ──
                    RenderTexture clipMask = null;
                    if (hasClipRun)
                        clipMask = RenderLayerAlpha(baseLayer, null); // ベース層の実効 α

                    // ベース層自身は通常どおり (自身の BlendMode で) 合成
                    DrawLayer(baseLayer, null, ref cur, ref next);

                    for (int j = i + 1; j < runEnd; j++)
                    {
                        var c = layers[j];
                        if (!c.UIVisible) continue;
                        DrawClipRunMember(c, clipMask, ref cur, ref next);
                    }

                    ReleaseToPool(clipMask);
                }

                i = runEnd;
            }
        }

        // ─── グループ (+ 直後のクリッピング群) の合成 ────────────────────────

        void CompositeGroup(List<PSDLayer> layers, int groupIdx, int runEnd,
                            ref RenderTexture cur, ref RenderTexture next)
        {
            var group = layers[groupIdx];

            if (group.GroupBlendMode == BlendMode.PassThrough)
            {
                // ── パススルー: 子を現在のバッファへ直接再帰合成 ──
                // 簡略化: パススルーグループの UIOpacity (< 1) とグループ自身のマスクは無視する
                CompositeList(group.Children, ref cur, ref next);

                // 簡略化: パススルーグループをベースとするクリッピング層は
                // ベース α を確定できないためクリップなしで通常合成する
                for (int j = groupIdx + 1; j < runEnd; j++)
                {
                    var c = layers[j];
                    if (!c.UIVisible) continue;
                    DrawClipRunMember(c, null, ref cur, ref next);
                }
                return;
            }

            // ── 分離グループ: 透明 RT に子を合成 → 1 枚のレイヤーとして合成 ──
            var gCur  = AcquireRT(clearToTransparent: true);
            var gNext = AcquireRT(clearToTransparent: false);
            CompositeList(group.Children, ref gCur, ref gNext);

            // クリッピング群がある場合、グループの実効 α (平坦化結果 × 不透明度 × マスク) を先に取得
            RenderTexture clipMask = null;
            if (runEnd > groupIdx + 1)
                clipMask = RenderTextureAlpha(gCur, group, null);

            // グループ全体を 1 枚のレイヤーとして合成
            // (_LayerRect = 全面 / _BlendMode = GroupBlendMode / _Opacity = UIOpacity / グループ自身のマスク適用)
            BlitAsFullCanvasLayer(gCur, group, ToShaderBlendMode(group.GroupBlendMode), null, ref cur, ref next);

            for (int j = groupIdx + 1; j < runEnd; j++)
            {
                var c = layers[j];
                if (!c.UIVisible) continue;
                DrawClipRunMember(c, clipMask, ref cur, ref next);
            }

            ReleaseToPool(clipMask);
            ReleaseToPool(gCur);
            ReleaseToPool(gNext);
        }

        // ─── クリッピング群メンバー 1 枚の合成 ───────────────────────────────

        void DrawClipRunMember(PSDLayer layer, RenderTexture clipMask,
                               ref RenderTexture cur, ref RenderTexture next)
        {
            if (layer.Children != null)
            {
                // 簡略化: クリッピング指定されたグループは常に分離合成し、
                // 平坦化結果 1 枚にクリップマスクを適用して合成する (PassThrough は Normal 扱い)
                var gCur  = AcquireRT(clearToTransparent: true);
                var gNext = AcquireRT(clearToTransparent: false);
                CompositeList(layer.Children, ref gCur, ref gNext);
                BlitAsFullCanvasLayer(gCur, layer, ToShaderBlendMode(layer.GroupBlendMode), clipMask, ref cur, ref next);
                ReleaseToPool(gCur);
                ReleaseToPool(gNext);
                return;
            }

            DrawLayer(layer, clipMask, ref cur, ref next);
        }

        // ════════════════════════════════════════════════════════════════
        //  単層描画 (調整レイヤー / SoCo / ピクセルレイヤー / Color Overlay)
        // ════════════════════════════════════════════════════════════════

        void DrawLayer(PSDLayer layer, RenderTexture clipMask,
                       ref RenderTexture cur, ref RenderTexture next)
        {
            // ── 調整レイヤー (ピクセルなし・SoCo なし) ──
            if (layer.IsAdjustmentLayer && !layer.Adjustment.HasSolidColor)
            {
                bool hasAdj = layer.Adjustment.HasBrightnessContrast
                           || layer.Adjustment.HasHueSaturation
                           || layer.UIColorize
                           || layer.UIInvert
                           || layer.UIThresholdEnabled
                           || layer.UIPosterizeEnabled
                           || layer.Adjustment.HasLevels
                           || layer.UICurveEnabled
                           || layer.UIColorBalanceEnabled
                           || (layer.UIGradientMapEnabled && layer._gradientLut != null);
                // 補正項目を持たない調整レイヤーは素通し (バッファは変化しないため描画自体を省略)
                if (!hasAdj) return;

                var ap = NewParams();
                ap.IsAdjustment = true;
                ap.Opacity      = layer.UIOpacity;
                SetMaskFrom(ref ap, layer);
                ap.ClipMaskTex  = clipMask;
                SetAdjustmentsFrom(ref ap, layer);
                ApplyParams(ap);
                Graphics.Blit(cur, next, _mat);
                Swap(ref cur, ref next);
                return;
            }

            // ── SoCo ベタ塗りレイヤー: 1×1 ソリッドを全面レイヤーとして合成 ──
            if (layer.Adjustment.HasSolidColor)
            {
                var sp = NewParams();
                sp.LayerTex  = GetSolidTexture(layer.Adjustment.SolidColor);
                sp.LayerRect = FullCanvasRect;
                sp.Opacity   = layer.UIOpacity;
                sp.BlendMode = ToShaderBlendMode(layer.BlendMode);
                SetMaskFrom(ref sp, layer);
                sp.ClipMaskTex = clipMask;
                SetAdjustmentsFrom(ref sp, layer);
                ApplyParams(sp);
                Graphics.Blit(cur, next, _mat);
                Swap(ref cur, ref next);
                return;
            }

            // ── ピクセルレイヤー ──
            if (layer.Texture == null) return; // 描画できるものがない

            var p = NewParams();
            p.LayerTex  = layer.Texture;
            p.LayerRect = LayerRectOf(layer);
            p.Opacity   = layer.UIOpacity;
            p.BlendMode = ToShaderBlendMode(layer.BlendMode);
            SetMaskFrom(ref p, layer);
            p.ClipMaskTex = clipMask;
            SetAdjustmentsFrom(ref p, layer);

            // ── Color Overlay (best effort) ──
            if (layer.Effects != null && layer.Effects.HasColorOverlay)
            {
                // 1) レイヤー本体を temp へ合成
                var temp = AcquireRT(clearToTransparent: false);
                ApplyParams(p);
                Graphics.Blit(cur, temp, _mat);

                // 2) レイヤーの実効 α 形状 (マスク・不透明度・クリップ込み) を取得
                var shape = RenderLayerAlpha(layer, clipMask);

                // 3) オーバーレイ色を実効 α でクリップして temp → next へ合成
                var op = NewParams();
                op.LayerTex    = GetSolidTexture(layer.Effects.OverlayColor);
                op.LayerRect   = FullCanvasRect;
                op.Opacity     = layer.Effects.OverlayOpacity;
                op.BlendMode   = ToShaderBlendMode(layer.Effects.OverlayBlendMode);
                op.ClipMaskTex = shape;
                ApplyParams(op);
                Graphics.Blit(temp, next, _mat);
                Swap(ref cur, ref next);

                ReleaseToPool(temp);
                ReleaseToPool(shape);
                return;
            }

            // ── 画像クリップ合成 (任意画像をレイヤーα形状へクリップ・タイリング・ブレンド) ──
            if (layer.UIImageClipEnabled && layer.UIImageClipTex != null)
            {
                // 1) レイヤー本体 (補正込み) を temp へ合成
                var temp = AcquireRT(clearToTransparent: false);
                ApplyParams(p);
                Graphics.Blit(cur, temp, _mat);

                // 2) レイヤーの実効 α 形状 (マスク・不透明度・クリップ込み) を取得
                var shape = RenderLayerAlpha(layer, clipMask);

                // 3) クリップ画像をレイヤー矩形基準でタイリングし、実効 α でクリップして合成
                var op = NewParams();
                op.LayerTex    = layer.UIImageClipTex;
                op.LayerRect   = LayerRectOf(layer); // 保存レイヤー (同矩形) とプレビューを一致させる
                op.LayerWrap   = true;
                op.LayerTile   = layer.UIImageClipTile;
                op.Opacity     = layer.UIImageClipOpacity;
                op.BlendMode   = ToShaderBlendMode(layer.UIImageClipBlend);
                op.ClipMaskTex = shape;
                ApplyParams(op);
                Graphics.Blit(temp, next, _mat);
                Swap(ref cur, ref next);

                ReleaseToPool(temp);
                ReleaseToPool(shape);
                return;
            }

            ApplyParams(p);
            Graphics.Blit(cur, next, _mat);
            Swap(ref cur, ref next);
        }
    }
}
