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
    ///   ・クリッピングは Photoshop 既定 (clbl=true) ではベース+クリップ群を透明バッファで
    ///     先に合成し、結果 1 枚をベースのブレンドモード・不透明度で背景へ合成する
    ///     (CompositeClipGroup)。clbl=false のレイヤーはベース α を _ClipMaskTex として
    ///     メンバーを背景バッファへ直接ブレンドする旧方式。
    ///
    /// 色空間: RT は全て RenderTextureReadWrite.Linear、戻り Texture2D も linear:true。
    /// 合成中は GL.sRGBWrite = false にして sRGB 変換が混入しないようにする
    /// (Linear カラースペースプロジェクトでの二重変換対策。REWRITE_SPEC.md §3 色空間)。
    ///
    /// 実装は uniform 設定 (LayerCompositor.Params.cs) / 書き出し用レンダリング
    /// (LayerCompositor.Export.cs) / RenderTexture プール管理 (LayerCompositor.RT.cs) へ
    /// partial class として分割されている。このファイルは合成パイプラインの本体を担う。
    /// </summary>
    // ─── partial 見取り図 ───────────────────────────────────────────
    // 責務   : レイヤーツリーを GPU 合成し最終結果テクスチャを生成する合成パイプライン本体
    // 宣言   : ShaderPath, _canvasW, _canvasH, _mat, _rtA, _rtB, _pool, _solidCache
    // 参照   : なし (本体ファイル)
    // 依存   : ApplyParams (Params.cs), RenderLayerAlpha (Export.cs), AcquireRT (RT.cs) 等
    // ────────────────────────────────────────────────────────────────
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

            BlendModeMappingValidator.ValidateOnce(shader);

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
                if (!baseLayer.UI.Visible) { i = runEnd; continue; }

                bool hasClipRun = runEnd > i + 1;

                if (baseLayer.Children != null)
                {
                    CompositeGroup(layers, i, runEnd, ref cur, ref next);
                }
                else if (hasClipRun && baseLayer.BlendClippedAsGroup)
                {
                    // ── Photoshop 既定 (clbl=true): ベース+クリップ群を先にグループ合成 ──
                    CompositeClipGroup(layers, i, runEnd, ref cur, ref next);
                }
                else
                {
                    // ── 通常レイヤー (+ clbl=false のクリッピング群) ──
                    // clbl=false: メンバーは背景合成後のバッファへ直接ブレンドされる
                    RenderTexture clipMask = null;
                    try
                    {
                        if (hasClipRun)
                            clipMask = RenderLayerAlpha(baseLayer, null); // ベース層の実効 α

                        // ベース層自身は通常どおり (自身の BlendMode で) 合成
                        DrawLayer(baseLayer, null, ref cur, ref next);

                        DrawClipRunMembers(layers, i + 1, runEnd, clipMask, ref cur, ref next);
                    }
                    finally { ReleaseToPool(clipMask); }
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
                // 簡略化: パススルーグループの UI.Opacity (< 1) とグループ自身のマスクは無視する
                CompositeList(group.Children, ref cur, ref next);

                // 簡略化: パススルーグループをベースとするクリッピング層は
                // ベース α を確定できないためクリップなしで通常合成する
                DrawClipRunMembers(layers, groupIdx + 1, runEnd, null, ref cur, ref next);
                return;
            }

            // ── 分離グループ: 透明 RT に子を合成 → 1 枚のレイヤーとして合成 ──
            // gCur/gNext はプールからの借用。ref で渡され Swap されるため using ではなく
            // try/finally で確実に返却する (再帰合成中に例外が出ても RT をリークさせない)。
            var gCur  = AcquireRT(clearToTransparent: true);
            var gNext = AcquireRT(clearToTransparent: false);
            try
            {
                CompositeList(group.Children, ref gCur, ref gNext);

                bool hasClipRun = runEnd > groupIdx + 1;

                if (hasClipRun && group.BlendClippedAsGroup)
                {
                    // ── Photoshop 既定 (clbl=true): クリップ群をグループ内容と先に合成 ──
                    // グループ内容の α をコピーで保持 (メンバー描画で gCur が変化するため)
                    var clipMask = AcquireRT(clearToTransparent: false);
                    try
                    {
                        Graphics.Blit(gCur, clipMask);
                        DrawClipRunMembers(layers, groupIdx + 1, runEnd, clipMask, ref gCur, ref gNext);
                    }
                    finally { ReleaseToPool(clipMask); }

                    // メンバー込みの平坦化結果を 1 枚のレイヤーとして合成
                    // (グループの不透明度・マスクは群全体へかかる = Photoshop と同じ)
                    BlitAsFullCanvasLayer(gCur, group, ToShaderBlendMode(group.GroupBlendMode), null, ref cur, ref next);
                }
                else
                {
                    // ── clbl=false: グループを先に合成し、メンバーは背景バッファへ直接ブレンド ──
                    RenderTexture clipMask = null;
                    try
                    {
                        if (hasClipRun)
                            clipMask = RenderTextureAlpha(gCur, group, null); // 実効 α (× 不透明度 × マスク)

                        BlitAsFullCanvasLayer(gCur, group, ToShaderBlendMode(group.GroupBlendMode), null, ref cur, ref next);

                        DrawClipRunMembers(layers, groupIdx + 1, runEnd, clipMask, ref cur, ref next);
                    }
                    finally { ReleaseToPool(clipMask); }
                }
            }
            finally
            {
                ReleaseToPool(gCur);
                ReleaseToPool(gNext);
            }
        }

        // ─── 通常レイヤーのクリッピング群 (clbl=true) の合成 ─────────────────
        //
        // Photoshop の既定動作「クリップされたレイヤーをグループとしてブレンド」:
        //   1. ベース層を不透明度 1 で透明バッファへ単独描画 (マスク・色調補正込み)
        //   2. その α をクリップマスクとして保持
        //   3. クリッピング層を同バッファへ各自のブレンドモードで合成
        //   4. 平坦化結果 1 枚をベース層のブレンドモード・不透明度で背景へ合成
        // これによりベースが乗算等でもメンバーはベースの画素とだけ先に混ざり、
        // 背景とはベースのモードで一度だけブレンドされる。

        void CompositeClipGroup(List<PSDLayer> layers, int baseIdx, int runEnd,
                                ref RenderTexture cur, ref RenderTexture next)
        {
            var baseLayer = layers[baseIdx];

            // gCur/gNext はプールからの借用。ref/Swap されるため try/finally で確実に返却する。
            var gCur  = AcquireRT(clearToTransparent: true);
            var gNext = AcquireRT(clearToTransparent: false);
            try
            {
                // ベース層を不透明度 1 で単独描画 (不透明度は最後に群全体へかける)
                float savedOpacity = baseLayer.UI.Opacity;
                baseLayer.UI.Opacity = 1f;
                try     { DrawLayer(baseLayer, null, ref gCur, ref gNext); }
                finally { baseLayer.UI.Opacity = savedOpacity; }

                // ベース層の実効 α をコピーで保持 (メンバー描画で gCur が変化するため)
                var clipMask = AcquireRT(clearToTransparent: false);
                try
                {
                    Graphics.Blit(gCur, clipMask);
                    DrawClipRunMembers(layers, baseIdx + 1, runEnd, clipMask, ref gCur, ref gNext);
                }
                finally { ReleaseToPool(clipMask); }

                // 平坦化結果をベース層のブレンドモード・不透明度で背景へ合成
                // (マスク・色調補正はベース層の描画時に適用済みのため、ここでは適用しない)
                var p = NewParams();
                p.LayerTex  = gCur;
                p.LayerRect = FullCanvasRect;
                p.Opacity   = savedOpacity;
                p.BlendMode = ToShaderBlendMode(baseLayer.BlendMode);
                ApplyParams(p);
                Graphics.Blit(cur, next, _mat);
                Swap(ref cur, ref next);
            }
            finally
            {
                ReleaseToPool(gCur);
                ReleaseToPool(gNext);
            }
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
                try
                {
                    CompositeList(layer.Children, ref gCur, ref gNext);
                    BlitAsFullCanvasLayer(gCur, layer, ToShaderBlendMode(layer.GroupBlendMode), clipMask, ref cur, ref next);
                }
                finally
                {
                    ReleaseToPool(gCur);
                    ReleaseToPool(gNext);
                }
                return;
            }

            DrawLayer(layer, clipMask, ref cur, ref next);
        }

        // ─── クリッピング群メンバー列の合成 (共通ループ) ─────────────────────
        // ベース (グループ) 直後の [firstMemberIdx, runEnd) にある可視メンバーを、
        // clipMask (null 可) でクリップしつつ順に合成する。CompositeList /
        // CompositeGroup / CompositeClipGroup が共有する。
        void DrawClipRunMembers(List<PSDLayer> layers, int firstMemberIdx, int runEnd,
                                RenderTexture clipMask, ref RenderTexture cur, ref RenderTexture next)
        {
            for (int j = firstMemberIdx; j < runEnd; j++)
            {
                var c = layers[j];
                if (!c.UI.Visible) continue;
                DrawClipRunMember(c, clipMask, ref cur, ref next);
            }
        }

        // ════════════════════════════════════════════════════════════════
        //  単層描画 (調整レイヤー / SoCo / ピクセルレイヤー / Color Overlay)
        // ════════════════════════════════════════════════════════════════

        void DrawLayer(PSDLayer layer, RenderTexture clipMask,
                       ref RenderTexture cur, ref RenderTexture next)
        {
            // ── 調整レイヤー (ピクセルなし・SoCo/GdFl なし) ──
            if (layer.IsAdjustmentLayer && !layer.Adjustment.HasSolidColor && !layer.Adjustment.HasGradientFill)
            {
                bool hasAdj = layer.Adjustment.HasBrightnessContrast
                           || layer.Adjustment.HasHueSaturation
                           || layer.UI.Colorize
                           || layer.UI.Invert
                           || layer.UI.ThresholdEnabled
                           || layer.UI.PosterizeEnabled
                           || layer.Adjustment.HasLevels
                           || layer.UI.CurveEnabled
                           || layer.UI.ColorBalanceEnabled
                           || (layer.UI.GradientMapEnabled && layer.Runtime.GradientLut != null);
                // 補正項目を持たない調整レイヤーは素通し (バッファは変化しないため描画自体を省略)
                if (!hasAdj) return;

                var ap = NewParams();
                ap.IsAdjustment = true;
                ap.Opacity      = layer.UI.Opacity;
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
                sp.Opacity   = layer.UI.Opacity;
                sp.BlendMode = ToShaderBlendMode(layer.BlendMode);
                SetMaskFrom(ref sp, layer);
                sp.ClipMaskTex = clipMask;
                SetAdjustmentsFrom(ref sp, layer);
                ApplyParams(sp);
                Graphics.Blit(cur, next, _mat);
                Swap(ref cur, ref next);
                return;
            }

            // ── GdFl グラデーション塗りつぶしレイヤー: LUT + 角度/タイプで全面レイヤーとして合成 ──
            if (layer.Adjustment.HasGradientFill && layer.Runtime.GradientFillLut != null)
            {
                var gp = NewParams();
                gp.LayerTex  = Texture2D.whiteTexture; // 色はシェーダーが _GradFillTex で上書きする
                gp.LayerRect = FullCanvasRect;
                gp.Opacity   = layer.UI.Opacity;
                gp.BlendMode = ToShaderBlendMode(layer.BlendMode);
                SetMaskFrom(ref gp, layer);
                gp.ClipMaskTex = clipMask;
                SetAdjustmentsFrom(ref gp, layer);
                SetGradientFillFrom(ref gp, layer);
                ApplyParams(gp);
                Graphics.Blit(cur, next, _mat);
                Swap(ref cur, ref next);
                return;
            }

            // ── ピクセルレイヤー ──
            if (layer.Texture == null) return; // 描画できるものがない

            var p = NewParams();
            p.LayerTex  = layer.Texture;
            p.LayerRect = LayerRectOf(layer);
            p.Opacity   = layer.UI.Opacity;
            p.BlendMode = ToShaderBlendMode(layer.BlendMode);
            SetMaskFrom(ref p, layer);
            p.ClipMaskTex = clipMask;
            SetAdjustmentsFrom(ref p, layer);

            // ── Color Overlay (best effort) ──
            if (layer.Effects != null && layer.Effects.HasColorOverlay)
            {
                // 1) レイヤー本体を temp へ合成
                var temp = AcquireRT(clearToTransparent: false);
                RenderTexture shape = null;
                try
                {
                    ApplyParams(p);
                    Graphics.Blit(cur, temp, _mat);

                    // 2) レイヤーの実効 α 形状 (マスク・不透明度・クリップ込み) を取得
                    shape = RenderLayerAlpha(layer, clipMask);

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
                }
                finally
                {
                    ReleaseToPool(temp);
                    ReleaseToPool(shape);
                }
                return;
            }

            // ── 画像クリップ合成 (任意画像をレイヤーα形状へクリップ・タイリング・ブレンド) ──
            if (layer.UI.ImageClipEnabled && layer.UI.ImageClipTex != null)
            {
                // 1) レイヤー本体 (補正込み) を temp へ合成
                var temp = AcquireRT(clearToTransparent: false);
                RenderTexture shape = null;
                try
                {
                    ApplyParams(p);
                    Graphics.Blit(cur, temp, _mat);

                    // 2) レイヤーの実効 α 形状 (マスク・不透明度・クリップ込み) を取得
                    shape = RenderLayerAlpha(layer, clipMask);

                    // 3) クリップ画像をレイヤー矩形基準でタイリングし、実効 α でクリップして合成
                    var op = NewParams();
                    op.LayerTex    = layer.UI.ImageClipTex;
                    op.LayerRect   = LayerRectOf(layer); // 保存レイヤー (同矩形) とプレビューを一致させる
                    op.LayerWrap   = true;
                    op.LayerTile   = layer.UI.ImageClipTile;
                    op.Opacity     = layer.UI.ImageClipOpacity;
                    op.BlendMode   = ToShaderBlendMode(layer.UI.ImageClipBlend);
                    op.ClipMaskTex = shape;
                    ApplyParams(op);
                    Graphics.Blit(temp, next, _mat);
                    Swap(ref cur, ref next);
                }
                finally
                {
                    ReleaseToPool(temp);
                    ReleaseToPool(shape);
                }
                return;
            }

            ApplyParams(p);
            Graphics.Blit(cur, next, _mat);
            Swap(ref cur, ref next);
        }
    }
}
