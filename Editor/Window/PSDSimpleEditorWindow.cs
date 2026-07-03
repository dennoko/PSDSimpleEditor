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
    // ─── partial 見取り図 ───────────────────────────────────────────
    // 責務   : メインウィンドウの定義、ライフサイクル管理、PSD 読み込みと GPU 合成の実行
    //          ※ 本クラスは以下の 9 ファイルに分割されています:
    //             - PSDSimpleEditorWindow.cs (本体、ライフサイクルと読み込み/合成)
    //             - PSDSimpleEditorWindow.Toolbar.cs (履歴メニュー)
    //             - PSDSimpleEditorWindow.LayerPanel.cs (レイヤーパネル UI の描画)
    //             - PSDSimpleEditorWindow.Adjustments.cs (色調補正/トーンカーブ等の UI・LUTベイク)
    //             - PSDSimpleEditorWindow.Preview.cs (プレビュー描画・チェッカー背景)
    //             - PSDSimpleEditorWindow.ColorRangeMask.cs (色域選択・スポイト抽出・PNG出力)
    //             - PSDSimpleEditorWindow.Export.cs (下部バー描画・PNG/PSD書き出し)
    //             - PSDSimpleEditorWindow.AdjustmentClipboard.cs (補正パラメータのコピー&ペースト)
    //             - PSDSimpleEditorWindow.UIToolkit.cs (UI Toolkit を使用したレイアウト構築・制御)
    // 宣言   : IndentWidth, CheckerCellPx, StatusType, _statusMessage, _statusType,
    //          _statusResetTime, _layerPanelWidth, _exportDir, _exportFormat, _psdPath,
    //          _showMergedRef, _previewMaterial, _previewSlotName, _isRealtimePreviewEnabled,
    //          _originalTexture, _psdFile, _compositor, _compositeTexture, _checkerTexture,
    //          _needsRecomposite, _layerScroll, _isResizing, _isSplitterHovered, _eyedropperTarget,
    //          _colorRangePreviewLayer, _colorRangePreviewTex, _colorRangePreviewDirty,
    //          ColorRangeHighlightColor, _history
    // 参照   : なし (本体ファイル)
    // 依存   : BakeImportedLuts (.Adjustments.cs), UpdateMainArea (.UIToolkit.cs), RevertRealtimePreview (.cs) 等
    // ────────────────────────────────────────────────────────────────
    public partial class PSDSimpleEditorWindow : EditorWindow
    {
        [MenuItem("dennokoworks/Dennoko PSD Editor")]
        static void Open()
        {
            var window = GetWindow<PSDSimpleEditorWindow>("Dennoko PSD Editor");
            window.minSize = new Vector2(800f, 360f);
        }

        // ── 定数 ───────────────────────────────────────────────────────────
        const float IndentWidth     = 14f;    // コントロール行 1 段あたりのインデント
        const float CheckerCellPx   = 8f;     // チェッカー 1 マスの画面ピクセル数

        // ── ステータス ──────────────────────────────────────────────────────
        public enum StatusType { Info, Success, Error }
        string     _statusMessage   = "PSD ファイルを読み込んでください。";
        StatusType _statusType      = StatusType.Info;
        double     _statusResetTime = -1.0;

        // ── 状態 ───────────────────────────────────────────────────────────
        [SerializeField] float _layerPanelWidth = 300f; // 左パネル幅
        [SerializeField] string _exportDir = "Assets/DennokoPSDEditor_exported"; // PNG出力先フォルダ
        [SerializeField] ExportFormat _exportFormat = ExportFormat.PNG; // エクスポートフォーマット
        string _psdPath = "";                 // 入力中の PSD パス (リロード後も保持)
        bool   _showMergedRef;                // マージ済み画像の参照表示

        // ── リアルタイムプレビュー ──────────────────────────────────────────
        [SerializeField] Material _previewMaterial;      // プレビュー対象マテリアル
        [SerializeField] string _previewSlotName = "_MainTex"; // 対象スロット名
        [SerializeField] bool _isRealtimePreviewEnabled; // プレビュー有効フラグ
        [SerializeField] Texture _originalTexture;         // 元のテクスチャのバックアップ


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

            // ドメインリロード（コンパイル）後にプレビューが有効状態であれば、再度バインドとバックアップを登録する
            if (_isRealtimePreviewEnabled && _previewMaterial != null)
            {
                EditorApplication.delayCall += () =>
                {
                    if (this != null && _isRealtimePreviewEnabled)
                    {
                        // 念のため _originalTexture が残っていなければ再設定する
                        if (_originalTexture == null)
                        {
                            _originalTexture = _previewMaterial.GetTexture(_previewSlotName);
                        }
                        PSDPreviewRecovery.SaveBackup(_previewMaterial, _previewSlotName, _originalTexture);
                        ApplyRealtimePreview();
                        _needsRecomposite = true;
                        Repaint();
                    }
                };
            }
        }


        void OnDestroy() => Cleanup();

        /// <summary>全リソースを破棄する (再ロード前・ウィンドウ破棄時)。</summary>
        void Cleanup()
        {
            RevertRealtimePreview(); // プレビューを解除して元に戻す

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

            UpdateMainArea();
            UpdateBottomBar();
            UpdateSettingsFields();
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
                if (layer._gradientFillLut != null) { DestroyImmediate(layer._gradientFillLut); layer._gradientFillLut = null; }
                DestroyLayerTexturesRecursive(layer.Children);
            }
        }

        static void SafeDestroy<T>(ref T obj) where T : UnityEngine.Object
        {
            if (obj != null) { DestroyImmediate(obj); obj = null; }
        }

        void Update()
        {
            // ステータスの自動リセット (Info 以外を一定時間後に戻す)
            if (_statusResetTime > 0 && EditorApplication.timeSinceStartup > _statusResetTime)
            {
                _statusMessage   = _psdFile != null ? "Ready" : "PSD ファイルを読み込んでください。";
                _statusType      = StatusType.Info;
                _statusResetTime = -1.0;
                UpdateStatusBar();
                Repaint();
            }
        }

        // ── ヘッダー / 空状態 / ステータスバー ───────────────────────────────

        /// <summary>ステータスバーにメッセージを表示し、一定時間後に既定へ戻す。</summary>
        void SetStatus(string message, StatusType type, double autoResetSeconds = 4.0)
        {
            _statusMessage   = message;
            _statusType      = type;
            _statusResetTime = type == StatusType.Info
                ? -1.0
                : EditorApplication.timeSinceStartup + autoResetSeconds;
            UpdateStatusBar();
            Repaint();
        }

        // ── PSD 読み込み ───────────────────────────────────────────────────

        void LoadPSD()
        {
            string resolved = ResolvePSDPath();
            if (string.IsNullOrEmpty(resolved) || !File.Exists(resolved))
            {
                SetStatus("有効な PSD ファイルを指定してください。", StatusType.Error);
                EditorUtility.DisplayDialog("エラー", "有効な PSD ファイルを指定してください。", "OK");
                return;
            }

            try
            {
                EditorUtility.DisplayProgressBar("PSD 読み込み中", "旧データを破棄しています...", 0.1f);
                Cleanup();  // 旧リソースを完全破棄してから読み込む

                EditorUtility.DisplayProgressBar("PSD 読み込み中", "ファイルを解析しています...", 0.4f);
                _psdFile = PSDParser.Parse(_psdPath);

                // グラデーションマップ (grdm) / トーンカーブ (curv) の LUT を焼く (Editor 側でしか焼けないため)
                BakeImportedLuts(_psdFile.Layers);

                // 読み込みに成功したパスを履歴へ記録
                _history.Add(resolved);

                EditorUtility.DisplayProgressBar("PSD 読み込み中", "コンポジターを初期化しています...", 0.85f);
                _compositor = new LayerCompositor(_psdFile.Width, _psdFile.Height);
                if (!_compositor.IsValid)
                    Debug.LogWarning("[PSDSimpleEditor] コンポジターの初期化に失敗しました。" +
                                     "LayerBlend.shader を確認してください。");

                _needsRecomposite = true;
                SetStatus($"読み込み完了: {Path.GetFileName(resolved)}", StatusType.Success);
            }
            catch (Exception e)
            {
                Debug.LogError($"[PSDSimpleEditor] PSD 読み込み失敗: {e}");
                Cleanup();  // 中途半端な状態を残さない
                SetStatus($"読み込みに失敗しました: {e.Message}", StatusType.Error);
                EditorUtility.DisplayDialog("読み込みエラー",
                    $"PSD ファイルの読み込みに失敗しました:\n{e.Message}", "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            UpdateMainArea();
            UpdateBottomBar();
            UpdateSettingsFields();
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
                if (_isRealtimePreviewEnabled)
                {
                    ApplyRealtimePreview();
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[PSDSimpleEditor] 合成失敗: {e}");
            }
            UpdateBottomBar();
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

        // ── リアルタイムプレビュー制御 ──────────────────────────────────────

        /// <summary>編集中のテクスチャ（RenderTexture）をマテリアルへリアルタイムに適用する。</summary>
        void ApplyRealtimePreview()
        {
            if (_previewMaterial == null || string.IsNullOrEmpty(_previewSlotName) || _compositeTexture == null)
                return;

            // 最初の一回だけ元のテクスチャを退避
            if (_originalTexture == null)
            {
                _originalTexture = _previewMaterial.GetTexture(_previewSlotName);
                // 異常終了に備えてバックアップを保存
                PSDPreviewRecovery.SaveBackup(_previewMaterial, _previewSlotName, _originalTexture);
            }

            _previewMaterial.SetTexture(_previewSlotName, _compositeTexture);
        }

        /// <summary>リアルタイムプレビューを解除し、元のテクスチャを復元する。</summary>
        void RevertRealtimePreview()
        {
            if (_previewMaterial != null && !string.IsNullOrEmpty(_previewSlotName) && _originalTexture != null)
            {
                _previewMaterial.SetTexture(_previewSlotName, _originalTexture);
                _originalTexture = null;
                PSDPreviewRecovery.ClearBackup();
            }
        }

        public enum ExportFormat
        {
            PNG,
            PSD,
            TGA
        }
    }
}

