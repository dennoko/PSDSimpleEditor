using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;

namespace PSDSimpleEditor
{
    /// <summary>
    /// PSD Simple Editor のメインウィンドウ (REWRITE_SPEC.md §6)。
    /// PSD の読み込み → レイヤーツリー操作 → GPU 合成プレビュー → PNG 書き出しを提供する。
    ///
    /// 実装は機能ごとに partial class として分割されている:
    ///   Toolbar (ツールバー/履歴) / LayerPanel (レイヤーツリー UI) /
    ///   Adjustments (色調補正・グラデーションマップ・画像クリップ) /
    ///   Preview (プレビュー描画) / ColorRangeMask (色域選択マスク) /
    ///   Export (下部バー・PNG/PSD 書き出し)。
    /// このファイルはフィールド定義・ライフサイクル・OnGUI・PSD 読み込み・合成実行を担う。
    /// </summary>
    public partial class PSDSimpleEditorWindow : EditorWindow
    {
        [MenuItem("dennokoworks/PSD Simple Editor")]
        static void Open()
        {
            var window = GetWindow<PSDSimpleEditorWindow>("PSD Simple Editor");
            window.minSize = new Vector2(640f, 360f);
        }

        // ── 定数 ───────────────────────────────────────────────────────────
        const float BottomBarHeight = 22f;    // 下部バー高さ
        const float IndentWidth     = 14f;    // ツリー 1 段あたりのインデント
        const float CheckerCellPx   = 8f;     // チェッカー 1 マスの画面ピクセル数

        // ── 状態 ───────────────────────────────────────────────────────────
        [SerializeField] float _layerPanelWidth = 270f; // 左パネル幅
        [SerializeField] string _exportDir = "Assets/PSDSE_exported"; // PNG出力先フォルダ
        [SerializeField] ExportFormat _exportFormat = ExportFormat.PNG; // エクスポートフォーマット
        string _psdPath = "";                 // 入力中の PSD パス (リロード後も保持)
        bool   _showMergedRef;                // マージ済み画像の参照表示


        [NonSerialized] PSDFile         _psdFile;            // 読み込み結果
        [NonSerialized] LayerCompositor _compositor;         // GPU 合成器
        [NonSerialized] Texture2D       _compositeTexture;   // 最新の合成結果
        [NonSerialized] Texture2D       _checkerTexture;     // 透明部可視化用の市松テクスチャ
        [NonSerialized] bool            _needsRecomposite;   // 変更フラグ → Repaint 時に合成

        Vector2 _layerScroll;
        [NonSerialized] bool _isResizing;             // リサイズ中フラグ
        [NonSerialized] bool _isSplitterHovered;      // スプリッターホバーフラグ
        [NonSerialized] PSDLayer _eyedropperTarget;   // 非 null = 色域選択スポイト待機中の対象レイヤー

        // 色域選択ハイライトプレビュー
        [NonSerialized] PSDLayer  _colorRangePreviewLayer; // 非 null = ハイライト表示中の対象レイヤー
        [NonSerialized] Texture2D _colorRangePreviewTex;   // ハイライト用テクスチャ (レイヤーサイズ)
        [NonSerialized] bool      _colorRangePreviewDirty; // パラメータ変更でハイライト再生成が必要
        static readonly Color ColorRangeHighlightColor = new Color(1f, 0.1f, 0.1f, 0.6f); // 選択範囲の表示色

        // PSD 読み込み履歴
        readonly PSDPathHistory _history = new PSDPathHistory();

        // ── ライフサイクル ─────────────────────────────────────────────────

        void OnEnable()
        {
            wantsMouseMove = true;
        }

        void OnDestroy() => Cleanup();

        /// <summary>全リソースを破棄する (再ロード前・ウィンドウ破棄時)。</summary>
        void Cleanup()
        {
            _compositor?.Dispose();
            _compositor = null;

            SafeDestroy(ref _compositeTexture);
            SafeDestroy(ref _checkerTexture);
            SafeDestroy(ref _colorRangePreviewTex);
            _colorRangePreviewLayer = null;
            _colorRangePreviewDirty = false;
            _eyedropperTarget       = null;

            if (_psdFile != null)
            {
                DestroyLayerTexturesRecursive(_psdFile.Layers);
                SafeDestroy(ref _psdFile.MergedComposite);
                _psdFile = null;
            }

            _needsRecomposite = false;
        }

        /// <summary>レイヤーツリーの Texture / MaskTexture を再帰的に破棄する。</summary>
        static void DestroyLayerTexturesRecursive(List<PSDLayer> layers)
        {
            if (layers == null) return;
            foreach (var layer in layers)
            {
                if (layer.Texture     != null) { DestroyImmediate(layer.Texture);     layer.Texture     = null; }
                if (layer.MaskTexture != null) { DestroyImmediate(layer.MaskTexture); layer.MaskTexture = null; }
                if (layer._gradientLut != null) { DestroyImmediate(layer._gradientLut); layer._gradientLut = null; }
                if (layer._curveLut    != null) { DestroyImmediate(layer._curveLut);    layer._curveLut    = null; }
                DestroyLayerTexturesRecursive(layer.Children);
            }
        }

        static void SafeDestroy<T>(ref T obj) where T : UnityEngine.Object
        {
            if (obj != null) { DestroyImmediate(obj); obj = null; }
        }

        // ── OnGUI ──────────────────────────────────────────────────────────

        void OnGUI()
        {
            DrawToolbar();
            DrawExportBar();

            if (_psdFile == null)
            {
                EditorGUILayout.HelpBox("PSD ファイルを選択して「Load」を押してください。", MessageType.Info);
            }
            else
            {
                float mainHeight = position.height
                                 - EditorStyles.toolbar.fixedHeight * 2f
                                 - BottomBarHeight - 8f;

                // レイヤーパネルの幅をウィンドウサイズに応じて制限
                float minWidth = 150f;
                float maxWidth = Mathf.Max(minWidth, position.width - 150f);
                _layerPanelWidth = Mathf.Clamp(_layerPanelWidth, minWidth, maxWidth);

                EditorGUILayout.BeginHorizontal(GUILayout.Height(mainHeight));
                DrawLayerPanel(mainHeight);
                DrawSplitter(mainHeight);
                DrawPreviewPanel();
                EditorGUILayout.EndHorizontal();

                DrawBottomBar();
            }

            // 変更があれば Repaint イベント中に再合成する (レイアウト中の構造変更を避ける)
            if (_needsRecomposite && Event.current.type == EventType.Repaint)
                DoComposite();
        }

        // ── PSD 読み込み ───────────────────────────────────────────────────

        void LoadPSD()
        {
            string resolved = ResolvePSDPath();
            if (string.IsNullOrEmpty(resolved) || !File.Exists(resolved))
            {
                EditorUtility.DisplayDialog("エラー", "有効な PSD ファイルを指定してください。", "OK");
                return;
            }

            try
            {
                EditorUtility.DisplayProgressBar("PSD 読み込み中", "旧データを破棄しています...", 0.1f);
                Cleanup();  // 旧リソースを完全破棄してから読み込む

                EditorUtility.DisplayProgressBar("PSD 読み込み中", "ファイルを解析しています...", 0.4f);
                _psdFile = PSDParser.Parse(_psdPath);

                // 読み込みに成功したパスを履歴へ記録
                _history.Add(resolved);

                EditorUtility.DisplayProgressBar("PSD 読み込み中", "コンポジターを初期化しています...", 0.85f);
                _compositor = new LayerCompositor(_psdFile.Width, _psdFile.Height);
                if (!_compositor.IsValid)
                    Debug.LogWarning("[PSDSimpleEditor] コンポジターの初期化に失敗しました。" +
                                     "LayerBlend.shader を確認してください。");

                _needsRecomposite = true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[PSDSimpleEditor] PSD 読み込み失敗: {e}");
                Cleanup();  // 中途半端な状態を残さない
                EditorUtility.DisplayDialog("読み込みエラー",
                    $"PSD ファイルの読み込みに失敗しました:\n{e.Message}", "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            Repaint();
        }

        // ── 合成 ───────────────────────────────────────────────────────────

        /// <summary>Repaint イベント中に呼び出す。古い結果を破棄して再合成する。</summary>
        void DoComposite()
        {
            _needsRecomposite = false;
            if (_psdFile == null || _compositor == null || !_compositor.IsValid) return;

            SafeDestroy(ref _compositeTexture);
            try
            {
                _compositeTexture = _compositor.Composite(_psdFile.Layers);
            }
            catch (Exception e)
            {
                Debug.LogError($"[PSDSimpleEditor] 合成失敗: {e}");
            }
            Repaint();  // 新しい結果を次フレームで表示
        }

        /// <summary>
        /// パスにダブルクォーテーションが含まれており、そのままではファイルが存在しない場合、
        /// ダブルクォーテーションを除去してファイル存在チェックを試みる。
        /// 存在すればクリーンアップされたパスを返し、かつ _psdPath 自体を更新する。
        /// </summary>
        string ResolvePSDPath()
        {
            if (string.IsNullOrEmpty(_psdPath)) return _psdPath;
            if (File.Exists(_psdPath)) return _psdPath;

            if (_psdPath.StartsWith("\"") || _psdPath.EndsWith("\""))
            {
                string trimmed = _psdPath.Trim('"');
                if (File.Exists(trimmed))
                {
                    _psdPath = trimmed;
                    return trimmed;
                }
            }
            return _psdPath;
        }

        public enum ExportFormat
        {
            PNG,
            PSD,
            TGA
        }
    }
}

