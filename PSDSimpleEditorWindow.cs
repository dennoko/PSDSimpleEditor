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
    /// </summary>
    public class PSDSimpleEditorWindow : EditorWindow
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

        // ── ツールバー ─────────────────────────────────────────────────────

        void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            GUILayout.Label("PSD:", GUILayout.Width(32));
            _psdPath = EditorGUILayout.TextField(_psdPath, EditorStyles.toolbarTextField,
                                                 GUILayout.ExpandWidth(true));

            if (GUILayout.Button("Browse...", EditorStyles.toolbarButton, GUILayout.Width(68)))
            {
                string dir = "";
                try
                {
                    string resolved = ResolvePSDPath();
                    if (File.Exists(resolved)) dir = Path.GetDirectoryName(resolved);
                }
                catch { }
                string picked = EditorUtility.OpenFilePanel("PSD ファイルを開く", dir, "psd");
                if (!string.IsNullOrEmpty(picked))
                {
                    _psdPath = picked;
                    GUI.FocusControl(null);  // テキストフィールドの古い表示を解除
                }
            }

            if (GUILayout.Button("Load", EditorStyles.toolbarButton, GUILayout.Width(44)))
                LoadPSD();

            GUILayout.Space(8);

            _showMergedRef = GUILayout.Toggle(_showMergedRef, "マージ参照",
                                              EditorStyles.toolbarButton, GUILayout.Width(76));

            EditorGUILayout.EndHorizontal();
        }

        void DrawExportBar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            GUILayout.Label("Export Dir:", GUILayout.Width(72));
            _exportDir = EditorGUILayout.TextField(_exportDir, EditorStyles.toolbarTextField,
                                                   GUILayout.ExpandWidth(true));

            if (GUILayout.Button("Browse...", EditorStyles.toolbarButton, GUILayout.Width(68)))
            {
                string picked = EditorUtility.OpenFolderPanel("PNG出力先フォルダを選択", _exportDir, "");
                if (!string.IsNullOrEmpty(picked))
                {
                    string projectPath = Path.GetFullPath(Application.dataPath);
                    string normalizedPicked = Path.GetFullPath(picked);
                    if (normalizedPicked.StartsWith(projectPath, StringComparison.OrdinalIgnoreCase))
                    {
                        string relative = "Assets" + normalizedPicked.Substring(projectPath.Length);
                        _exportDir = relative.Replace('\\', '/');
                    }
                    else
                    {
                        _exportDir = normalizedPicked.Replace('\\', '/');
                    }
                    GUI.FocusControl(null);
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        // ── レイヤーパネル (左) ─────────────────────────────────────────────

        void DrawLayerPanel(float panelHeight)
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(_layerPanelWidth),
                                          GUILayout.Height(panelHeight));
            GUILayout.Label("レイヤー", EditorStyles.boldLabel);

            _layerScroll = EditorGUILayout.BeginScrollView(_layerScroll,
                                                           GUILayout.ExpandHeight(true));
            DrawLayerListTopDown(_psdFile.Layers, 0);
            EditorGUILayout.EndScrollView();

            EditorGUILayout.EndVertical();
        }

        /// <summary>レイヤーパネルとプレビューパネルの境界をドラッグで調整するためのスプリッターを描画する。</summary>
        void DrawSplitter(float height)
        {
            // 8px 幅のホバー/インタラクション領域を確保
            Rect rect = GUILayoutUtility.GetRect(8f, height, GUILayout.Width(8f), GUILayout.Height(height));

            // ホバー時のカーソル形状を左右矢印に変更
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.ResizeHorizontal);

            // ホバー状態の更新
            Event currentEvent = Event.current;
            bool isHovered = rect.Contains(currentEvent.mousePosition);
            if (isHovered != _isSplitterHovered)
            {
                _isSplitterHovered = isHovered;
                Repaint();
            }

            // スプリッターの背景/線の描画色を決定
            Color dividerColor;
            if (_isResizing)
            {
                dividerColor = new Color(0.24f, 0.48f, 0.9f); // ドラッグ中のアクセントブルー
            }
            else if (_isSplitterHovered)
            {
                dividerColor = EditorGUIUtility.isProSkin ? new Color(0.4f, 0.4f, 0.4f) : new Color(0.7f, 0.7f, 0.7f); // ホバー時
            }
            else
            {
                dividerColor = EditorGUIUtility.isProSkin ? new Color(0.18f, 0.18f, 0.18f) : new Color(0.65f, 0.65f, 0.65f); // 通常時
            }

            // 視覚的に美しい 2px 幅の中央線として描画
            Rect visualRect = new Rect(rect.x + (rect.width - 2f) / 2f, rect.y, 2f, rect.height);
            EditorGUI.DrawRect(visualRect, dividerColor);

            // マウス入力によるリサイズ処理
            switch (currentEvent.type)
            {
                case EventType.MouseDown:
                    if (rect.Contains(currentEvent.mousePosition) && currentEvent.button == 0)
                    {
                        _isResizing = true;
                        currentEvent.Use();
                    }
                    break;

                case EventType.MouseDrag:
                    if (_isResizing)
                    {
                        _layerPanelWidth = currentEvent.mousePosition.x;
                        currentEvent.Use();
                        Repaint();
                    }
                    break;

                case EventType.MouseUp:
                    if (_isResizing)
                    {
                        _isResizing = false;
                        currentEvent.Use();
                    }
                    break;
            }
        }

        /// <summary>
        /// レイヤーリストを「上が最上層」になるよう逆順に描画する
        /// (PSDFile.Layers は index 0 = 最下層)。
        /// </summary>
        void DrawLayerListTopDown(List<PSDLayer> layers, int indent)
        {
            if (layers == null) return;
            for (int i = layers.Count - 1; i >= 0; i--)
                DrawLayerNode(layers[i], indent);
        }

        void DrawLayerNode(PSDLayer layer, int indent)
        {
            bool isGroup = layer.Children != null;

            EditorGUILayout.BeginVertical(GUI.skin.box);

            // ── ヘッダ行: [折りたたみ] [表示] 名前 ... ブレンド ──
            EditorGUILayout.BeginHorizontal();

            if (indent > 0)
                GUILayout.Space(indent * IndentWidth);

            if (isGroup)
            {
                // Foldout は ExpandWidth しないよう固定幅で配置
                layer.IsExpanded = EditorGUILayout.Foldout(layer.IsExpanded, GUIContent.none, true);
            }
            else
            {
                GUILayout.Space(14f);
            }

            // 表示トグル → 再合成
            bool newVisible = EditorGUILayout.Toggle(layer.UIVisible, GUILayout.Width(16));
            if (newVisible != layer.UIVisible)
            {
                layer.UIVisible   = newVisible;
                _needsRecomposite = true;
            }

            // 名前 + 種別プレフィックス
            string label = BuildLayerLabel(layer, isGroup);
            GUILayout.Label(new GUIContent(label, label), GUILayout.ExpandWidth(true));

            // ブレンドモード (編集可能な Popup)
            DrawBlendModePopup(layer, isGroup);

            EditorGUILayout.EndHorizontal();

            // ── 詳細 (表示中レイヤーのみ) ──
            if (layer.UIVisible)
            {
                if (isGroup)
                {
                    // グループ自身の不透明度 (PassThrough 以外で合成に効く)
                    DrawOpacitySlider(layer, indent);

                    if (layer.IsExpanded)
                        DrawLayerListTopDown(layer.Children, indent + 1);
                }
                else
                {
                    DrawLayerControls(layer, indent);
                }
            }

            EditorGUILayout.EndVertical();
            GUILayout.Space(1);
        }

        /// <summary>種別プレフィックス付きのレイヤー名を組み立てる。</summary>
        static string BuildLayerLabel(PSDLayer layer, bool isGroup)
        {
            string prefix = "";

            if (isGroup)
                prefix += "[フォルダ] ";
            if (layer.IsClipping)
                prefix += "[クリップ] ";
            if (layer.HasMask)
                prefix += layer.MaskIsDisabled ? "[マスク無効] " : "[マスク] ";

            if (layer.Adjustment != null && layer.Adjustment.HasSolidColor)
                prefix += "[SoCo] ";
            else if (!isGroup && layer.IsAdjustmentLayer &&
                     layer.Adjustment != null &&
                     (layer.Adjustment.HasBrightnessContrast || layer.Adjustment.HasHueSaturation))
                prefix += "[調整] ";

            if (layer.Effects != null && layer.Effects.HasColorOverlay)
                prefix += "[CO] ";

            return prefix + layer.Name;
        }

        /// <summary>非グループレイヤーの詳細コントロール。</summary>
        void DrawLayerControls(PSDLayer layer, int indent)
        {
            // 不透明度
            DrawOpacitySlider(layer, indent);

            // 明るさ・コントラスト (brit / CgEd)
            if (layer.Adjustment != null && layer.Adjustment.HasBrightnessContrast)
            {
                float nb = IndentedSlider("明るさ", layer.UIBrightness, -150f, 150f, indent);
                float nc = IndentedSlider("ｺﾝﾄﾗｽﾄ", layer.UIContrast,    -50f, 100f, indent);
                if (!Mathf.Approximately(nb, layer.UIBrightness) ||
                    !Mathf.Approximately(nc, layer.UIContrast))
                {
                    layer.UIBrightness = nb;
                    layer.UIContrast   = nc;
                    _needsRecomposite  = true;
                }
            }

            // 色相・彩度・明度 (hue2)
            if (layer.Adjustment != null && layer.Adjustment.HasHueSaturation)
            {
                float nh = IndentedSlider("色相", layer.UIHue,        -180f, 180f, indent);
                float ns = IndentedSlider("彩度", layer.UISaturation, -100f, 100f, indent);
                float nl = IndentedSlider("明度", layer.UILightness,  -100f, 100f, indent);
                if (!Mathf.Approximately(nh, layer.UIHue) ||
                    !Mathf.Approximately(ns, layer.UISaturation) ||
                    !Mathf.Approximately(nl, layer.UILightness))
                {
                    layer.UIHue        = nh;
                    layer.UISaturation = ns;
                    layer.UILightness  = nl;
                    _needsRecomposite  = true;
                }
            }

            // ベタ塗りカラー (SoCo)
            if (layer.Adjustment != null && layer.Adjustment.HasSolidColor)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(indent * IndentWidth + 18f);
                GUILayout.Label("塗り色", EditorStyles.miniLabel, GUILayout.Width(44));
                Color nc = EditorGUILayout.ColorField(layer.Adjustment.SolidColor);
                EditorGUILayout.EndHorizontal();
                if (nc != layer.Adjustment.SolidColor)
                {
                    layer.Adjustment.SolidColor = nc;
                    _needsRecomposite = true;
                }
            }

            // 全ピクセルレイヤー向け: 色調補正 + グラデーションマップ (非破壊)
            // (parse 済み調整レイヤー / SoCo は上の専用 UI が担当するため除外)
            if (!layer.IsAdjustmentLayer)
                DrawAdjustmentFoldout(layer, indent);

            // マスクの有効/無効表示
            if (layer.HasMask)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(indent * IndentWidth + 18f);
                GUILayout.Label("マスク: " + (layer.MaskIsDisabled ? "無効" : "有効"),
                                EditorStyles.miniLabel);
                EditorGUILayout.EndHorizontal();
            }
        }

        void DrawOpacitySlider(PSDLayer layer, int indent)
        {
            float newOpacity = IndentedSlider("不透明度", layer.UIOpacity, 0f, 1f, indent);
            if (!Mathf.Approximately(newOpacity, layer.UIOpacity))
            {
                layer.UIOpacity   = newOpacity;
                _needsRecomposite = true;
            }
        }

        // ── 色調補正 + グラデーションマップ (非破壊・全ピクセルレイヤー) ──────

        /// <summary>「色調補正」フォールドアウト。明るさ/コントラスト/色相/彩度/明度 + グラデーションマップ。</summary>
        void DrawAdjustmentFoldout(PSDLayer layer, int indent)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(indent * IndentWidth + 18f);
            layer.UIAdjustExpanded = EditorGUILayout.Foldout(layer.UIAdjustExpanded, "色調補正", true);
            EditorGUILayout.EndHorizontal();
            if (!layer.UIAdjustExpanded) return;

            int ci = indent + 1;

            // 明るさ・コントラスト・色相・彩度・明度 (シェーダーの正規化除数に合わせた範囲)
            float nb = IndentedSlider("明るさ",  layer.UIBrightness, -150f, 150f, ci);
            float nc = IndentedSlider("ｺﾝﾄﾗｽﾄ",  layer.UIContrast,   -50f, 100f, ci);
            float nh = IndentedSlider("色相",    layer.UIHue,        -180f, 180f, ci);
            float ns = IndentedSlider("彩度",    layer.UISaturation, -100f, 100f, ci);
            float nl = IndentedSlider("明度",    layer.UILightness,  -100f, 100f, ci);
            if (!Mathf.Approximately(nb, layer.UIBrightness) ||
                !Mathf.Approximately(nc, layer.UIContrast)   ||
                !Mathf.Approximately(nh, layer.UIHue)        ||
                !Mathf.Approximately(ns, layer.UISaturation) ||
                !Mathf.Approximately(nl, layer.UILightness))
            {
                layer.UIBrightness = nb;
                layer.UIContrast   = nc;
                layer.UIHue        = nh;
                layer.UISaturation = ns;
                layer.UILightness  = nl;
                _needsRecomposite  = true;
            }

            DrawGradientMapControls(layer, ci);
        }

        /// <summary>グラデーションマップの有効トグル・グラデーション編集・適用率。</summary>
        void DrawGradientMapControls(PSDLayer layer, int indent)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(indent * IndentWidth + 18f);
            bool en = EditorGUILayout.ToggleLeft("グラデーションマップ", layer.UIGradientMapEnabled);
            EditorGUILayout.EndHorizontal();
            if (en != layer.UIGradientMapEnabled)
            {
                layer.UIGradientMapEnabled = en;
                if (en) EnsureGradientLut(layer);   // 初回有効化時に LUT を焼く
                _needsRecomposite = true;
            }
            if (!en) return;

            if (layer.UIGradient == null) layer.UIGradient = CreateDefaultGradient();

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(indent * IndentWidth + 18f);
            GUILayout.Label("階調", EditorStyles.miniLabel, GUILayout.Width(48));
            EditorGUI.BeginChangeCheck();
            Gradient ng = EditorGUILayout.GradientField(layer.UIGradient);
            bool gradientChanged = EditorGUI.EndChangeCheck();
            EditorGUILayout.EndHorizontal();
            if (gradientChanged)
            {
                layer.UIGradient = ng;
                BakeGradientLut(layer);
                _needsRecomposite = true;
            }

            float no = IndentedSlider("適用率", layer.UIGradientMapOpacity, 0f, 1f, indent);
            if (!Mathf.Approximately(no, layer.UIGradientMapOpacity))
            {
                layer.UIGradientMapOpacity = no;
                _needsRecomposite = true;
            }
        }

        static Gradient CreateDefaultGradient()
        {
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.black, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f),          new GradientAlphaKey(1f, 1f) });
            return g;
        }

        /// <summary>グラデーション有効時に LUT が無ければ焼く。</summary>
        void EnsureGradientLut(PSDLayer layer)
        {
            if (layer.UIGradient == null) layer.UIGradient = CreateDefaultGradient();
            if (layer._gradientLut == null) BakeGradientLut(layer);
        }

        /// <summary>UIGradient を 256×1 の LUT テクスチャ (linear) に焼き込む。</summary>
        static void BakeGradientLut(PSDLayer layer)
        {
            const int N = 256;
            if (layer._gradientLut == null)
            {
                layer._gradientLut = new Texture2D(N, 1, TextureFormat.RGBA32, false, linear: true)
                {
                    hideFlags  = HideFlags.HideAndDontSave,
                    wrapMode   = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                };
            }
            var px = new Color32[N];
            for (int i = 0; i < N; i++)
                px[i] = layer.UIGradient.Evaluate(i / (float)(N - 1));
            layer._gradientLut.SetPixels32(px);
            layer._gradientLut.Apply(false);
        }

        /// <summary>インデント付きのラベル + スライダー 1 行。</summary>
        static float IndentedSlider(string label, float value, float min, float max, int indent)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(indent * IndentWidth + 18f);
            GUILayout.Label(label, EditorStyles.miniLabel, GUILayout.Width(48));
            float result = EditorGUILayout.Slider(value, min, max);
            EditorGUILayout.EndHorizontal();
            return result;
        }

        /// <summary>ブレンドモードの短縮ラベル。</summary>
        static string BlendModeShortLabel(BlendMode mode)
        {
            switch (mode)
            {
                case BlendMode.Normal:       return "Norm";
                case BlendMode.Multiply:     return "Mul";
                case BlendMode.Screen:       return "Scrn";
                case BlendMode.Overlay:      return "Over";
                case BlendMode.Dissolve:     return "Diss";
                case BlendMode.Darken:       return "Dark";
                case BlendMode.ColorBurn:    return "CBrn";
                case BlendMode.LinearBurn:   return "LBrn";
                case BlendMode.DarkerColor:  return "DkCl";
                case BlendMode.Lighten:      return "Lite";
                case BlendMode.ColorDodge:   return "CDdg";
                case BlendMode.LinearDodge:  return "Add";
                case BlendMode.LighterColor: return "LtCl";
                case BlendMode.SoftLight:    return "SLit";
                case BlendMode.HardLight:    return "HLit";
                case BlendMode.VividLight:   return "VLit";
                case BlendMode.LinearLight:  return "LLit";
                case BlendMode.PinLight:     return "PLit";
                case BlendMode.HardMix:      return "HMix";
                case BlendMode.Difference:   return "Diff";
                case BlendMode.Exclusion:    return "Excl";
                case BlendMode.Subtract:     return "Sub";
                case BlendMode.Divide:       return "Div";
                case BlendMode.Hue:          return "Hue";
                case BlendMode.Saturation:   return "Sat";
                case BlendMode.Color:        return "Colr";
                case BlendMode.Luminosity:   return "Lum";
                case BlendMode.PassThrough:  return "Pass";
                default:                     return "?";
            }
        }

        // ── ブレンドモード Popup ────────────────────────────────────────────

        // Popup の候補 (Unknown は除外)。グループのみ PassThrough を先頭に含める。
        static readonly BlendMode[] _blendModesNormal =
        {
            BlendMode.Normal,      BlendMode.Multiply,    BlendMode.Screen,
            BlendMode.Overlay,     BlendMode.Dissolve,    BlendMode.Darken,
            BlendMode.ColorBurn,   BlendMode.LinearBurn,  BlendMode.DarkerColor,
            BlendMode.Lighten,     BlendMode.ColorDodge,  BlendMode.LinearDodge,
            BlendMode.LighterColor,BlendMode.SoftLight,   BlendMode.HardLight,
            BlendMode.VividLight,  BlendMode.LinearLight, BlendMode.PinLight,
            BlendMode.HardMix,     BlendMode.Difference,  BlendMode.Exclusion,
            BlendMode.Subtract,    BlendMode.Divide,      BlendMode.Hue,
            BlendMode.Saturation,  BlendMode.Color,       BlendMode.Luminosity,
        };
        static readonly BlendMode[] _blendModesGroup = BuildGroupBlendModes();
        static string[] _blendLabelsNormal;
        static string[] _blendLabelsGroup;

        static BlendMode[] BuildGroupBlendModes()
        {
            var list = new List<BlendMode> { BlendMode.PassThrough };
            list.AddRange(_blendModesNormal);
            return list.ToArray();
        }

        /// <summary>レイヤー/グループのブレンドモードを Popup で編集する。変更時に再合成を要求。</summary>
        void DrawBlendModePopup(PSDLayer layer, bool isGroup)
        {
            BlendMode[] modes  = isGroup ? _blendModesGroup : _blendModesNormal;
            string[]    labels = isGroup
                ? (_blendLabelsGroup  ?? (_blendLabelsGroup  = BuildBlendLabels(_blendModesGroup)))
                : (_blendLabelsNormal ?? (_blendLabelsNormal = BuildBlendLabels(_blendModesNormal)));

            BlendMode cur = isGroup ? layer.GroupBlendMode : layer.BlendMode;
            // Unknown 等で候補に無い場合は index 0 を仮表示 (ユーザー操作があるまで書き換えない)
            int curIndex = Mathf.Max(0, Array.IndexOf(modes, cur));

            int newIndex = EditorGUILayout.Popup(curIndex, labels, GUILayout.Width(74));
            if (newIndex != curIndex)
            {
                if (isGroup) layer.GroupBlendMode = modes[newIndex];
                else         layer.BlendMode      = modes[newIndex];
                _needsRecomposite = true;
            }
        }

        static string[] BuildBlendLabels(BlendMode[] modes)
        {
            var labels = new string[modes.Length];
            for (int i = 0; i < modes.Length; i++)
                labels[i] = BlendModeShortLabel(modes[i]);
            return labels;
        }

        // ── プレビューパネル (右) ───────────────────────────────────────────

        void DrawPreviewPanel()
        {
            EditorGUILayout.BeginVertical();
            GUILayout.Label("プレビュー", EditorStyles.boldLabel);

            Rect area = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none,
                                                 GUILayout.ExpandWidth(true),
                                                 GUILayout.ExpandHeight(true));

            if (Event.current.type == EventType.Repaint)
            {
                if (_compositeTexture != null && area.width > 8f && area.height > 8f)
                {
                    float aspect   = (float)_compositeTexture.width / _compositeTexture.height;
                    Rect  drawRect = FitRectKeepAspect(area, aspect);

                    // 透明部可視化のチェッカー背景 → その上にアルファ合成で描画
                    DrawCheckerBackground(drawRect);
                    GUI.DrawTexture(drawRect, _compositeTexture, ScaleMode.StretchToFill, true);

                    // マージ済み画像の参照小窓 (右下)
                    if (_showMergedRef && _psdFile != null && _psdFile.MergedComposite != null)
                        DrawMergedOverlay(area);
                }
                else
                {
                    GUI.Label(area, "プレビューなし", EditorStyles.centeredGreyMiniLabel);
                }
            }

            EditorGUILayout.EndVertical();
        }

        /// <summary>マージ済み画像を右下に小窓で重ね描きする。</summary>
        void DrawMergedOverlay(Rect area)
        {
            Texture2D merged = _psdFile.MergedComposite;
            float maxSize = Mathf.Min(area.width, area.height) * 0.3f;
            if (maxSize < 32f) return;

            var box = new Rect(area.xMax - maxSize - 6f, area.yMax - maxSize - 6f,
                               maxSize, maxSize);
            Rect fit = FitRectKeepAspect(box, (float)merged.width / merged.height);

            // 枠 + チェッカー + 画像 + ラベル
            EditorGUI.DrawRect(new Rect(fit.x - 1, fit.y - 1, fit.width + 2, fit.height + 2),
                               new Color(0f, 0f, 0f, 0.8f));
            DrawCheckerBackground(fit);
            GUI.DrawTexture(fit, merged, ScaleMode.StretchToFill, true);
            GUI.Label(new Rect(fit.x, fit.y - 15f, fit.width, 14f),
                      "マージ参照", EditorStyles.centeredGreyMiniLabel);
        }

        /// <summary>指定矩形にチェッカーパターンをタイル描画する。</summary>
        void DrawCheckerBackground(Rect rect)
        {
            EnsureCheckerTexture();
            if (_checkerTexture == null) return;

            // 2×2 テクスチャの 1 テクセルを CheckerCellPx px で繰り返す
            var coords = new Rect(0f, 0f,
                                  rect.width  / (CheckerCellPx * 2f),
                                  rect.height / (CheckerCellPx * 2f));
            GUI.DrawTextureWithTexCoords(rect, _checkerTexture, coords, false);
        }

        /// <summary>市松テクスチャを (再) 生成する。ドメインリロード後にも対応。</summary>
        void EnsureCheckerTexture()
        {
            if (_checkerTexture != null) return;

            _checkerTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
            {
                hideFlags  = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Point,
                wrapMode   = TextureWrapMode.Repeat,
            };
            var bright = new Color32(190, 190, 190, 255);
            var dark   = new Color32(127, 127, 127, 255);
            _checkerTexture.SetPixels32(new[] { bright, dark, dark, bright });
            _checkerTexture.Apply(false);
        }

        /// <summary>アスペクト比を維持して領域内に収め、中央配置した矩形を返す。</summary>
        static Rect FitRectKeepAspect(Rect area, float texAspect)
        {
            float areaAspect = area.width / Mathf.Max(area.height, 1f);
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

        // ── 下部バー ───────────────────────────────────────────────────────

        void DrawBottomBar()
        {
            EditorGUILayout.BeginHorizontal(GUILayout.Height(BottomBarHeight));

            GUILayout.Label(
                $"{_psdFile.Width} × {_psdFile.Height} px  |  " +
                $"レイヤー数: {CountLayersRecursive(_psdFile.Layers)}  |  " +
                $"{_psdFile.BitDepth}bit  |  " +
                ColorModeName(_psdFile.ColorMode),
                EditorStyles.miniLabel);

            GUILayout.FlexibleSpace();

            using (new EditorGUI.DisabledScope(_compositeTexture == null))
            {
                if (GUILayout.Button("Export PNG", GUILayout.Width(100)))
                    ExportPNG();
            }

            using (new EditorGUI.DisabledScope(_psdFile == null))
            {
                if (GUILayout.Button("Export PSD...", GUILayout.Width(100)))
                    ExportPSD();
            }

            EditorGUILayout.EndHorizontal();
        }

        /// <summary>グループを含む全ノード数を再帰的に数える。</summary>
        static int CountLayersRecursive(List<PSDLayer> layers)
        {
            if (layers == null) return 0;
            int count = layers.Count;
            foreach (var layer in layers)
                count += CountLayersRecursive(layer.Children);
            return count;
        }

        static string ColorModeName(ushort mode)
        {
            switch (mode)
            {
                case 0:  return "Bitmap";
                case 1:  return "Grayscale";
                case 2:  return "Indexed";
                case 3:  return "RGB";
                case 4:  return "CMYK";
                case 7:  return "Multichannel";
                case 8:  return "Duotone";
                case 9:  return "Lab";
                default: return $"Mode {mode}";
            }
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

        // ── PNG 書き出し ───────────────────────────────────────────────────

        void ExportPNG()
        {
            if (_compositeTexture == null)
            {
                EditorUtility.DisplayDialog("エラー",
                    "合成結果がありません。先に PSD を読み込んでください。", "OK");
                return;
            }

            if (string.IsNullOrEmpty(_exportDir))
            {
                EditorUtility.DisplayDialog("エラー",
                    "出力先フォルダが指定されていません。", "OK");
                return;
            }

            try
            {
                string baseName = "composite";
                if (!string.IsNullOrEmpty(_psdPath))
                {
                    baseName = Path.GetFileNameWithoutExtension(_psdPath);
                }

                string savePath = GetUniqueExportPath(_exportDir, baseName, ".png");

                byte[] png = _compositeTexture.EncodeToPNG();
                File.WriteAllBytes(savePath, png);
                Debug.Log($"[PSDSimpleEditor] PNG を保存しました: {savePath}");

                // プロジェクト内の出力ならAssetDatabaseをリフレッシュしてUnityエディタ上で見えるようにする
                string normalizedSavePath = savePath.Replace('\\', '/');
                int assetsIndex = normalizedSavePath.IndexOf("/Assets/", StringComparison.OrdinalIgnoreCase);
                if (assetsIndex != -1)
                {
                    string assetPath = normalizedSavePath.Substring(assetsIndex + 1);
                    AssetDatabase.Refresh();

                    // プロジェクトビューで該当ファイルを選択してハイライト（Ping）する
                    var obj = AssetDatabase.LoadMainAssetAtPath(assetPath);
                    if (obj != null)
                    {
                        Selection.activeObject = obj;
                        EditorGUIUtility.PingObject(obj);
                    }
                }
                else
                {
                    EditorUtility.RevealInFinder(savePath);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[PSDSimpleEditor] PNG 保存失敗: {e}");
                EditorUtility.DisplayDialog("書き出しエラー",
                    $"PNG の保存に失敗しました:\n{e.Message}", "OK");
            }
        }

        /// <summary>指定されたディレクトリ、ファイル名で衝突しない一意なパスを取得する。</summary>
        string GetUniqueExportPath(string dir, string baseNameWithoutExt, string ext)
        {
            string targetDir = dir;
            // "Assets" で始まる相対パスをプロジェクトルートからの絶対パスに変換
            if (dir.StartsWith("Assets", StringComparison.OrdinalIgnoreCase))
            {
                string subPath = dir.Substring(6).TrimStart('/', '\\');
                targetDir = Path.Combine(Application.dataPath, subPath);
            }

            targetDir = Path.GetFullPath(targetDir);

            // ディレクトリが存在しない場合は作成
            if (!Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            string targetPath = Path.Combine(targetDir, baseNameWithoutExt + ext);
            int counter = 1;

            while (File.Exists(targetPath))
            {
                targetPath = Path.Combine(targetDir, $"{baseNameWithoutExt} {counter}{ext}");
                counter++;
            }

            return targetPath;
        }

        // ── PSD 書き出し (レイヤー構造保持) ─────────────────────────────────

        void ExportPSD()
        {
            if (_psdFile == null)
            {
                EditorUtility.DisplayDialog("エラー", "先に PSD を読み込んでください。", "OK");
                return;
            }

            ResolvePSDPath();
            string defaultName = Path.GetFileNameWithoutExtension(_psdPath) + "_export";
            string dir = "";
            try
            {
                if (File.Exists(_psdPath)) dir = Path.GetDirectoryName(_psdPath);
            }
            catch { }

            string savePath = EditorUtility.SaveFilePanel("PSD として保存", dir, defaultName, "psd");
            if (string.IsNullOrEmpty(savePath)) return;

            try
            {
                EditorUtility.DisplayProgressBar("PSD 書き出し中", "合成結果を更新しています...", 0.2f);

                // マージ画像を最新の編集状態に同期 (Repaint 待ちに依存しない)
                if (_compositor != null && _compositor.IsValid)
                {
                    SafeDestroy(ref _compositeTexture);
                    _compositeTexture = _compositor.Composite(_psdFile.Layers);
                    _needsRecomposite = false;
                }

                EditorUtility.DisplayProgressBar("PSD 書き出し中", "PSD を書き出しています...", 0.6f);
                PSDWriter.Save(_psdFile, _psdFile.Layers, _compositor, _compositeTexture, savePath);

                Debug.Log($"[PSDSimpleEditor] PSD を保存しました: {savePath}");

                string normalizedSavePath = savePath.Replace('\\', '/');
                int assetsIndex = normalizedSavePath.IndexOf("/Assets/", StringComparison.OrdinalIgnoreCase);
                if (assetsIndex != -1)
                {
                    string assetPath = normalizedSavePath.Substring(assetsIndex + 1);
                    AssetDatabase.Refresh();

                    // プロジェクトビューで該当ファイルを選択してハイライト（Ping）する
                    var obj = AssetDatabase.LoadMainAssetAtPath(assetPath);
                    if (obj != null)
                    {
                        Selection.activeObject = obj;
                        EditorGUIUtility.PingObject(obj);
                    }
                }
                else
                {
                    EditorUtility.RevealInFinder(savePath);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[PSDSimpleEditor] PSD 保存失敗: {e}");
                EditorUtility.DisplayDialog("書き出しエラー",
                    $"PSD の保存に失敗しました:\n{e.Message}", "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
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
    }
}
