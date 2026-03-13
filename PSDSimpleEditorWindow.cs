using System;
using System.IO;
using UnityEngine;
using UnityEditor;

namespace PSDSimpleEditor
{
    /// <summary>
    /// Unity エディタ拡張のメインウィンドウ。
    /// PSD ファイルの読み込み・レイヤー操作・プレビュー・PNG 書き出しを提供する。
    /// メニュー: Window > PSD Simple Editor
    /// </summary>
    public class PSDSimpleEditorWindow : EditorWindow
    {
        [MenuItem("dennokoworks/PSD Simple Editor")]
        static void Open() => GetWindow<PSDSimpleEditorWindow>("PSD Simple Editor");

        // ── State ─────────────────────────────────────────────────────
        string           _psdPath          = "";
        PSDFile          _psdFile;
        LayerCompositor  _compositor;
        Texture2D        _compositeTexture;
        bool             _needsRecomposite;

        Vector2 _layerScroll;

        // ── Lifecycle ──────────────────────────────────────────────────

        void OnDestroy() => Cleanup();

        void Cleanup()
        {
            _compositor?.Dispose();
            _compositor = null;

            if (_compositeTexture != null)
            {
                DestroyImmediate(_compositeTexture);
                _compositeTexture = null;
            }

            if (_psdFile != null)
            {
                foreach (var layer in _psdFile.Layers)
                    if (layer.Texture != null)
                        DestroyImmediate(layer.Texture);
                _psdFile = null;
            }
        }

        // ── GUI ────────────────────────────────────────────────────────

        void OnGUI()
        {
            DrawToolbar();

            if (_psdFile == null)
            {
                EditorGUILayout.HelpBox("PSD ファイルを選択して「Load」ボタンを押してください。",
                    MessageType.Info);
                return;
            }

            // メインレイアウト: 左パネル + プレビューパネル
            float bottomBarH = 24f;
            float mainH      = position.height - EditorStyles.toolbar.fixedHeight - bottomBarH - 4f;

            EditorGUILayout.BeginHorizontal(GUILayout.Height(mainH));
            DrawLayerPanel(mainH);
            DrawPreviewPanel();
            EditorGUILayout.EndHorizontal();

            DrawBottomBar();

            // 再合成が必要なら実行
            if (_needsRecomposite && Event.current.type == EventType.Repaint)
                DoComposite();
        }

        // ── Toolbar ────────────────────────────────────────────────────

        void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            GUILayout.Label("PSD File:", GUILayout.Width(58));
            string newPath = EditorGUILayout.TextField(_psdPath, EditorStyles.toolbarTextField,
                                 GUILayout.ExpandWidth(true));
            if (newPath != _psdPath) _psdPath = newPath;

            if (GUILayout.Button("Browse...", EditorStyles.toolbarButton, GUILayout.Width(68)))
            {
                string path = EditorUtility.OpenFilePanel("PSD ファイルを開く", "", "psd");
                if (!string.IsNullOrEmpty(path)) _psdPath = path;
            }

            if (GUILayout.Button("Load", EditorStyles.toolbarButton, GUILayout.Width(44)))
                LoadPSD();

            EditorGUILayout.EndHorizontal();
        }

        // ── Layer Panel ────────────────────────────────────────────────

        void DrawLayerPanel(float panelHeight)
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(260), GUILayout.Height(panelHeight));
            GUILayout.Label("Layers", EditorStyles.boldLabel);

            _layerScroll = EditorGUILayout.BeginScrollView(_layerScroll, GUILayout.ExpandHeight(true));

            // UI では上のレイヤーが先頭に来るよう逆順に描画
            var layers = _psdFile.Layers;
            for (int i = layers.Count - 1; i >= 0; i--)
                DrawLayerItem(layers[i]);

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        void DrawLayerItem(PSDLayer layer)
        {
            EditorGUILayout.BeginVertical(GUI.skin.box);

            // ── ヘッダ行: 表示切替 / 名前 / ブレンドモード ──
            EditorGUILayout.BeginHorizontal();

            bool newVis = EditorGUILayout.Toggle(layer.UIVisible, GUILayout.Width(18));
            if (newVis != layer.UIVisible)
            {
                layer.UIVisible    = newVis;
                _needsRecomposite = true;
            }

            string label = layer.IsAdjustmentLayer ? $"[調整] {layer.Name}" : layer.Name;
            GUILayout.Label(label, GUILayout.ExpandWidth(true));

            GUIStyle blendStyle = EditorStyles.miniLabel;
            GUILayout.Label(layer.BlendMode.ToString(), blendStyle, GUILayout.Width(56));

            EditorGUILayout.EndHorizontal();

            if (!layer.UIVisible)
            {
                EditorGUILayout.EndVertical();
                GUILayout.Space(2);
                return;
            }

            // ── 不透明度スライダー (ピクセルレイヤーのみ) ──
            if (!layer.IsAdjustmentLayer)
            {
                float newOp = SliderField("Opacity", layer.UIOpacity, 0f, 1f, 50f);
                if (Math.Abs(newOp - layer.UIOpacity) > 0.001f)
                {
                    layer.UIOpacity   = newOp;
                    _needsRecomposite = true;
                }
            }

            // ── 明るさ / コントラスト ──
            if (layer.Adjustment.HasBrightnessContrast)
            {
                GUILayout.Label("Brightness / Contrast", EditorStyles.miniLabel);

                float nb = SliderField("Bright", layer.UIBrightness, -150f, 150f, 50f);
                if (Math.Abs(nb - layer.UIBrightness) > 0.1f)
                {
                    layer.UIBrightness = nb;
                    _needsRecomposite  = true;
                }

                float nc = SliderField("Contrast", layer.UIContrast, -50f, 100f, 50f);
                if (Math.Abs(nc - layer.UIContrast) > 0.1f)
                {
                    layer.UIContrast  = nc;
                    _needsRecomposite = true;
                }
            }

            // ── 色相 / 彩度 / 明度 ──
            if (layer.Adjustment.HasHueSaturation)
            {
                GUILayout.Label("Hue / Saturation / Lightness", EditorStyles.miniLabel);

                float nh = SliderField("Hue",  layer.UIHue,        -180f, 180f, 50f);
                if (Math.Abs(nh - layer.UIHue) > 0.1f)
                {
                    layer.UIHue       = nh;
                    _needsRecomposite = true;
                }

                float ns = SliderField("Sat",  layer.UISaturation, -100f, 100f, 50f);
                if (Math.Abs(ns - layer.UISaturation) > 0.1f)
                {
                    layer.UISaturation = ns;
                    _needsRecomposite  = true;
                }

                float nl = SliderField("Light", layer.UILightness, -100f, 100f, 50f);
                if (Math.Abs(nl - layer.UILightness) > 0.1f)
                {
                    layer.UILightness  = nl;
                    _needsRecomposite  = true;
                }
            }

            // ── ベタ塗り情報 ──
            if (layer.Adjustment.HasSolidColor)
                GUILayout.Label("SoCo (ベタ塗り)", EditorStyles.miniLabel);

            EditorGUILayout.EndVertical();
            GUILayout.Space(2);
        }

        // ラベル + スライダーのヘルパー
        static float SliderField(string label, float value, float min, float max, float labelWidth)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(labelWidth));
            float result = EditorGUILayout.Slider(value, min, max);
            EditorGUILayout.EndHorizontal();
            return result;
        }

        // ── Preview Panel ─────────────────────────────────────────────

        void DrawPreviewPanel()
        {
            EditorGUILayout.BeginVertical();
            GUILayout.Label("Preview", EditorStyles.boldLabel);

            Rect area = GUILayoutUtility.GetRect(
                GUIContent.none, GUIStyle.none,
                GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

            if (_compositeTexture != null)
            {
                Rect drawRect = FitRect(area,
                    (float)_compositeTexture.width / _compositeTexture.height);
                GUI.DrawTexture(drawRect, _compositeTexture, ScaleMode.StretchToFill, true);
            }
            else
            {
                GUI.Label(area, "プレビューなし", EditorStyles.centeredGreyMiniLabel);
            }

            EditorGUILayout.EndVertical();
        }

        static Rect FitRect(Rect area, float texAspect)
        {
            float areaAspect = area.width / area.height;
            if (texAspect > areaAspect)
            {
                float h = area.width / texAspect;
                return new Rect(area.x, area.y + (area.height - h) * 0.5f, area.width, h);
            }
            else
            {
                float w = area.height * texAspect;
                return new Rect(area.x + (area.width - w) * 0.5f, area.y, w, area.height);
            }
        }

        // ── Bottom Bar ────────────────────────────────────────────────

        void DrawBottomBar()
        {
            EditorGUILayout.BeginHorizontal();

            if (_psdFile != null)
                GUILayout.Label(
                    $"{_psdFile.Width} × {_psdFile.Height} px  |  {_psdFile.Layers.Count} layers",
                    EditorStyles.miniLabel);

            GUILayout.FlexibleSpace();

            if (_psdFile != null && GUILayout.Button("Export PNG...", GUILayout.Width(100)))
                ExportPNG();

            EditorGUILayout.EndHorizontal();
        }

        // ── PSD 読み込み ───────────────────────────────────────────────

        void LoadPSD()
        {
            if (string.IsNullOrEmpty(_psdPath) || !File.Exists(_psdPath))
            {
                EditorUtility.DisplayDialog("エラー", "有効な PSD ファイルを選択してください。", "OK");
                return;
            }

            try
            {
                EditorUtility.DisplayProgressBar("PSD 読み込み中", "ファイルを解析しています...", 0.3f);
                Cleanup();

                EditorUtility.DisplayProgressBar("PSD 読み込み中", "レイヤーデータを展開しています...", 0.6f);
                _psdFile = PSDParser.Parse(_psdPath);

                EditorUtility.DisplayProgressBar("PSD 読み込み中", "コンポジターを初期化しています...", 0.9f);
                _compositor       = new LayerCompositor(_psdFile.Width, _psdFile.Height);
                _needsRecomposite = true;

                Repaint();
            }
            catch (Exception e)
            {
                Debug.LogError($"[PSDSimpleEditor] PSD 読み込み失敗: {e.Message}\n{e.StackTrace}");
                EditorUtility.DisplayDialog("読み込みエラー",
                    $"PSD ファイルの読み込みに失敗しました:\n{e.Message}", "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        // ── 合成実行 ───────────────────────────────────────────────────

        void DoComposite()
        {
            _needsRecomposite = false;
            if (_compositor == null || !_compositor.IsValid || _psdFile == null) return;

            if (_compositeTexture != null)
            {
                DestroyImmediate(_compositeTexture);
                _compositeTexture = null;
            }

            _compositeTexture = _compositor.Composite(_psdFile.Layers);
            Repaint();
        }

        // ── PNG エクスポート ───────────────────────────────────────────

        void ExportPNG()
        {
            if (_compositeTexture == null)
            {
                EditorUtility.DisplayDialog("エラー", "プレビューが存在しません。先に PSD を読み込んでください。", "OK");
                return;
            }

            string defaultName = Path.GetFileNameWithoutExtension(_psdPath) + "_export";
            string dir         = File.Exists(_psdPath) ? Path.GetDirectoryName(_psdPath) : "";
            string savePath    = EditorUtility.SaveFilePanel("PNG として保存", dir, defaultName, "png");
            if (string.IsNullOrEmpty(savePath)) return;

            try
            {
                byte[] png = _compositeTexture.EncodeToPNG();
                File.WriteAllBytes(savePath, png);
                Debug.Log($"[PSDSimpleEditor] PNG を保存しました: {savePath}");
                EditorUtility.RevealInFinder(savePath);
            }
            catch (Exception e)
            {
                Debug.LogError($"[PSDSimpleEditor] PNG 保存失敗: {e.Message}");
                EditorUtility.DisplayDialog("エクスポートエラー",
                    $"PNG の保存に失敗しました:\n{e.Message}", "OK");
            }
        }
    }
}
