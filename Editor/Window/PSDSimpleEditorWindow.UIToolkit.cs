using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using UnityEditor.UIElements;

namespace PSDSimpleEditor
{
    // ─── partial 見取り図 ───────────────────────────────────────────
    // 責務   : UI Toolkit を使用したウィンドウ全体のレイアウト構築、データバインディング、表示の動的更新
    // 宣言   : _rootContainer などの UI 要素フィールド
    // 参照   : _psdPath (RW), _exportDir (RW), _previewMaterial (RW), _previewSlotName (RW),
    //          _isRealtimePreviewEnabled (RW), _exportFormat (RW), _statusMessage (R), _needsRecomposite (RW)
    // 依存   : LoadPSD (本体), DoComposite (本体), RevertRealtimePreview (本体), ApplyRealtimePreview (本体)
    // ────────────────────────────────────────────────────────────────
    public partial class PSDSimpleEditorWindow
    {
        // ── UI Toolkit Elements ───────────────────────────────────────────
        private VisualElement _rootContainer;
        private TextField _psdPathField;
        private TextField _exportDirField;
        private ObjectField _previewMaterialField;
        private TextField _previewSlotField;
        private Button _realtimePreviewButton;
        private Button _mergedRefButton;
        private EnumField _exportFormatField;
        private Label _bottomInfoLabel;
        private Button _exportButton;
        private Label _statusBarLabel;
        private VisualElement _mainAreaContainer;
        private ScrollView _layerTreeScrollView;
        private VisualElement _layerTreeContainer;
        private VisualElement _bottomBarContainer;

        void CreateGUI()
        {
            // Load language settings
            bool useEnglish = EditorPrefs.GetBool("DennokoPSDEditor_UseEnglish", false);
            PSDTranslation.LoadLanguage(useEnglish ? "en" : "ja");

            // Ensure textures/styles are initialized
            PSDEditorTheme.Initialize();

            // Load USS stylesheet
            var ussGuid = AssetDatabase.FindAssets("PSDEditorTheme t:StyleSheet");
            if (ussGuid != null && ussGuid.Length > 0)
            {
                var ussAsset = AssetDatabase.LoadAssetAtPath<StyleSheet>(AssetDatabase.GUIDToAssetPath(ussGuid[0]));
                if (ussAsset != null)
                {
                    rootVisualElement.styleSheets.Add(ussAsset);
                }
            }

            // Create root container
            _rootContainer = new VisualElement();
            _rootContainer.AddToClassList("root");
            rootVisualElement.Add(_rootContainer);

            // Build layout
            BuildHeader();
            BuildSettingsCard();
            BuildMainArea();
            BuildStatusBar();

            // Refresh dynamic states
            UpdateMainArea();
            UpdateBottomBar();
            UpdateStatusBar();
            UpdateSettingsFields();
        }

        void RebuildUI()
        {
            _blendLabelsNormal = null;
            _blendLabelsGroup = null;
            if (rootVisualElement != null)
            {
                rootVisualElement.Clear();
                CreateGUI();
            }
        }

        void BuildHeader()
        {
            var header = new VisualElement();
            header.AddToClassList("header");

            var titleLabel = new Label(PSDTranslation.Get("WindowTitle", "Dennoko PSD Editor"));
            titleLabel.AddToClassList("title");
            header.Add(titleLabel);

            // Spacer to push the toggle to the right
            var spacer = new VisualElement();
            spacer.AddToClassList("grow");
            header.Add(spacer);

            // Language Toggle
            var langToggle = new Toggle("English");
            langToggle.value = EditorPrefs.GetBool("DennokoPSDEditor_UseEnglish", false);
            langToggle.AddToClassList("settings-toggle");
            langToggle.RegisterValueChangedCallback(evt => {
                EditorPrefs.SetBool("DennokoPSDEditor_UseEnglish", evt.newValue);
                PSDTranslation.LoadLanguage(evt.newValue ? "en" : "ja");
                RebuildUI();
            });
            header.Add(langToggle);

            _rootContainer.Add(header);

            // Separator
            var sep = new VisualElement();
            sep.AddToClassList("separator");
            _rootContainer.Add(sep);
        }

        void BuildSettingsCard()
        {
            var card = new VisualElement();
            card.AddToClassList("card");

            // Row 1: PSD Input
            var row1 = new VisualElement();
            row1.AddToClassList("settings-row");

            var psdLabel = new Label("PSD");
            psdLabel.AddToClassList("control-label");
            psdLabel.AddToClassList("settings-label");
            row1.Add(psdLabel);

            _psdPathField = new TextField();
            _psdPathField.value = _psdPath;
            _psdPathField.AddToClassList("settings-input");
            _psdPathField.RegisterValueChangedCallback(evt => {
                _psdPath = evt.newValue;
            });
            row1.Add(_psdPathField);

            var browsePsdBtn = new Button(() => {
                string dir = "";
                try
                {
                    string resolved = ResolvePSDPath();
                    if (File.Exists(resolved)) dir = Path.GetDirectoryName(resolved);
                }
                catch { }
                string picked = EditorUtility.OpenFilePanel(PSDTranslation.Get("PsdOpenTitle", "PSD ファイルを開く"), dir, "psd");
                if (!string.IsNullOrEmpty(picked))
                {
                    _psdPath = picked;
                    _psdPathField.value = picked;
                }
            }) { text = PSDTranslation.Get("Browse", "参照") };
            browsePsdBtn.AddToClassList("button-tool");
            browsePsdBtn.AddToClassList("settings-button");
            row1.Add(browsePsdBtn);

            var loadPsdBtn = new Button(LoadPSD) { text = PSDTranslation.Get("Load", "読み込み") };
            loadPsdBtn.AddToClassList("button-tool");
            loadPsdBtn.AddToClassList("settings-button-wide");
            row1.Add(loadPsdBtn);

            var historyBtn = new Button(ShowHistoryMenu) { text = PSDTranslation.Get("History", "履歴 ▾") };
            historyBtn.AddToClassList("button-tool");
            historyBtn.AddToClassList("settings-button");
            row1.Add(historyBtn);

            card.Add(row1);

            // Separator
            var sep1 = new VisualElement();
            sep1.AddToClassList("separator");
            card.Add(sep1);

            // Row 2: Export Directory
            var row2 = new VisualElement();
            row2.AddToClassList("settings-row");

            var exportLabel = new Label(PSDTranslation.Get("ExportDir", "出力先"));
            exportLabel.AddToClassList("control-label");
            exportLabel.AddToClassList("settings-label");
            row2.Add(exportLabel);

            _exportDirField = new TextField();
            _exportDirField.value = _exportDir;
            _exportDirField.AddToClassList("settings-input");
            _exportDirField.RegisterValueChangedCallback(evt => {
                _exportDir = evt.newValue;
            });
            row2.Add(_exportDirField);

            var browseExportBtn = new Button(() => {
                string picked = EditorUtility.OpenFolderPanel(PSDTranslation.Get("ExportDirSelect", "出力先フォルダを選択"), _exportDir, "");
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
                    _exportDirField.value = _exportDir;
                }
            }) { text = PSDTranslation.Get("Browse", "参照") };
            browseExportBtn.AddToClassList("button-tool");
            browseExportBtn.AddToClassList("settings-button");
            row2.Add(browseExportBtn);

            card.Add(row2);

            // Separator
            var sep2 = new VisualElement();
            sep2.AddToClassList("separator");
            card.Add(sep2);

            // Row 3: Material Preview (3D プレビュー反映)
            var row3 = new VisualElement();
            row3.AddToClassList("settings-row");

            var previewLabel = new Label(PSDTranslation.Get("MaterialPreview", "マテリアルプレビュー"));
            previewLabel.AddToClassList("control-label");
            previewLabel.AddToClassList("settings-label-wide");
            previewLabel.tooltip = PSDTranslation.Get("MaterialPreviewTooltip", "指定したマテリアルのテクスチャを合成結果に一時的に差し替えて、3Dビュー上で見た目を確認できます。");
            row3.Add(previewLabel);

            _previewMaterialField = new ObjectField { objectType = typeof(Material), value = _previewMaterial, allowSceneObjects = true };
            _previewMaterialField.AddToClassList("settings-object-input");
            _previewMaterialField.tooltip = PSDTranslation.Get("PreviewMatTooltip", "プレビュー先のマテリアルを指定します。設定すると自動的にプレビューが有効になります。");
            _previewMaterialField.RegisterValueChangedCallback(evt => {
                var prevMat = (Material)evt.newValue;
                if (prevMat != _previewMaterial)
                {
                    RevertRealtimePreview();
                    _previewMaterial = prevMat;
                    MarkDirty();

                    // マテリアルをセットしたら自動的にプレビューを有効化する（未設定に戻したら無効化）
                    _isRealtimePreviewEnabled = _previewMaterial != null;
                    UpdateRealtimePreviewButtonState();
                }
            });
            row3.Add(_previewMaterialField);

            var slotLabel = new Label(PSDTranslation.Get("Texture", "テクスチャ"));
            slotLabel.AddToClassList("control-label");
            slotLabel.AddToClassList("settings-slot-label");
            row3.Add(slotLabel);

            _previewSlotField = new TextField();
            _previewSlotField.value = _previewSlotName;
            _previewSlotField.AddToClassList("settings-slot-input");
            _previewSlotField.tooltip = PSDTranslation.Get("TextureSlotTooltip", "合成結果を差し替えるマテリアルのテクスチャプロパティ名です。右の「▾」からシェーダーのテクスチャ項目を選択できます。");
            _previewSlotField.RegisterValueChangedCallback(evt => {
                var prevSlot = evt.newValue;
                if (prevSlot != _previewSlotName)
                {
                    RevertRealtimePreview();
                    _previewSlotName = prevSlot;
                    MarkDirty();
                }
            });
            row3.Add(_previewSlotField);

            var slotPickerBtn = new Button(ShowTexturePropertyMenu) { text = "▾" };
            slotPickerBtn.AddToClassList("button-tool");
            slotPickerBtn.AddToClassList("settings-button-icon");
            slotPickerBtn.tooltip = PSDTranslation.Get("TexturePickerTooltip", "マテリアルのシェーダーからテクスチャ項目を選んで指定します。");
            row3.Add(slotPickerBtn);

            _realtimePreviewButton = new Button(ToggleRealtimePreview);
            _realtimePreviewButton.AddToClassList("button-tool");
            _realtimePreviewButton.AddToClassList("settings-button-wide");
            UpdateRealtimePreviewButtonState();
            row3.Add(_realtimePreviewButton);

            card.Add(row3);

            _rootContainer.Add(card);
        }

        void BuildMainArea()
        {
            _mainAreaContainer = new VisualElement();
            _mainAreaContainer.AddToClassList("main-area");
            _rootContainer.Add(_mainAreaContainer);
        }

        void UpdateMainArea()
        {
            if (_mainAreaContainer == null) return;
            _mainAreaContainer.Clear();

            if (_psdFile == null)
            {
                // Empty state
                var emptyState = new VisualElement();
                emptyState.AddToClassList("card");
                emptyState.AddToClassList("empty-state");

                var emptyHeader = new Label(PSDTranslation.Get("PsdNotLoaded", "PSD が読み込まれていません"));
                emptyHeader.AddToClassList("title");
                emptyState.Add(emptyHeader);

                var emptySep = new VisualElement();
                emptySep.AddToClassList("separator");
                emptySep.AddToClassList("empty-state-separator");
                emptyState.Add(emptySep);

                var emptyText = new Label(PSDTranslation.Get("PsdLoadPrompt", "上部の「PSD」欄でファイルを指定し、「読み込み」を押してください。\n履歴からの再読み込みも可能です。"));
                emptyText.AddToClassList("centered-caption");
                emptyState.Add(emptyText);

                _mainAreaContainer.Add(emptyState);
            }
            else
            {
                // Split View with Layer panel and Preview panel
                var splitView = new TwoPaneSplitView(0, _layerPanelWidth, TwoPaneSplitViewOrientation.Horizontal);
                splitView.AddToClassList("split-view");

                // Left Pane: Layer Panel
                var layerPanel = new VisualElement();
                layerPanel.AddToClassList("panel");
                layerPanel.AddToClassList("layer-panel");

                var layerHeader = new VisualElement();
                layerHeader.AddToClassList("toolbar-style");
                
                var layerTitle = new Label(PSDTranslation.Get("Layers", "レイヤー"));
                layerTitle.AddToClassList("section-header");
                layerTitle.tooltip = PSDTranslation.Get("LayersTooltip", "PSD内のレイヤー構造を表示します。\n・左の目のトグル: 表示/非表示\n・右のドロップダウン: ブレンドモード\n・フォルダやレイヤーを展開すると詳細パラメータが表示されます。");
                layerHeader.Add(layerTitle);
                layerPanel.Add(layerHeader);

                _layerTreeScrollView = new ScrollView();
                _layerTreeScrollView.AddToClassList("layer-tree-scroll");
                _layerTreeContainer = new VisualElement();
                _layerTreeContainer.AddToClassList("layer-tree-container");
                _layerTreeScrollView.Add(_layerTreeContainer);
                layerPanel.Add(_layerTreeScrollView);

                // Right Pane: Preview Panel
                var previewPanel = new VisualElement();
                previewPanel.AddToClassList("panel");
                previewPanel.AddToClassList("preview-panel");

                var previewHeader = new VisualElement();
                previewHeader.AddToClassList("toolbar-style");

                var previewTitle = new Label(PSDTranslation.Get("Preview", "プレビュー"));
                previewTitle.AddToClassList("section-header");
                previewTitle.AddToClassList("grow");
                previewHeader.Add(previewTitle);

                _mergedRefButton = new Button(ToggleMergedRef);
                _mergedRefButton.AddToClassList("button-tool");
                _mergedRefButton.AddToClassList("settings-button-wide");
                UpdateMergedRefButtonState();
                previewHeader.Add(_mergedRefButton);
                previewPanel.Add(previewHeader);

                var imguiPreview = new IMGUIContainer(() => {
                    // DrawPreviewPanel inner part (excluding border and header which are now in UI Toolkit)
                    PSDEditorTheme.PushEditorTheme();
                    try
                    {
                        if (_needsRecomposite)
                        {
                            DoComposite();
                        }
                        DrawPreviewPanelContentOnly();
                    }
                    finally
                    {
                        PSDEditorTheme.PopEditorTheme();
                    }
                });
                imguiPreview.AddToClassList("grow");
                previewPanel.Add(imguiPreview);

                splitView.Add(layerPanel);
                splitView.Add(previewPanel);

                // Resize handle callback to update _layerPanelWidth
                splitView.RegisterCallback<GeometryChangedEvent>(evt => {
                    if (splitView.childCount > 0)
                    {
                        _layerPanelWidth = splitView[0].layout.width;
                    }
                });

                _mainAreaContainer.Add(splitView);

                // Build the tree nodes initially
                RebuildLayerTree();
            }

            BuildBottomBar();
        }

        void BuildBottomBar()
        {
            if (_rootContainer == null) return;
            // RemoveFromHierarchy は現在の実際の親から外すため、既に外れている場合でも安全 (Remove は非子要素で例外を投げる)
            _bottomBarContainer?.RemoveFromHierarchy();

            if (_psdFile == null) return;

            _bottomBarContainer = new VisualElement();
            _bottomBarContainer.AddToClassList("card");
            _bottomBarContainer.AddToClassList("bottom-bar");

            // Info Label
            _bottomInfoLabel = new Label();
            _bottomInfoLabel.AddToClassList("caption");
            _bottomBarContainer.Add(_bottomInfoLabel);

            // Spacer
            var spacer = new VisualElement();
            spacer.AddToClassList("grow");
            _bottomBarContainer.Add(spacer);

            // Format Selector Label
            var formatLabel = new Label(PSDTranslation.Get("Format", "形式"));
            formatLabel.AddToClassList("control-label");
            formatLabel.AddToClassList("format-label");
            formatLabel.tooltip = PSDTranslation.Get("FormatTooltip", "書き出す画像のファイルフォーマットを指定します。\n・PNG: 合成結果をアルファ付きPNGとして書き出します。\n・PSD: 現在の編集パラメータを維持したままPSDとして書き出します。\n・TGA: 32bit（アルファあり）のTGA形式で書き出します。");
            _bottomBarContainer.Add(formatLabel);

            // Format Selector EnumField
            _exportFormatField = new EnumField(_exportFormat);
            _exportFormatField.AddToClassList("format-field");
            _exportFormatField.RegisterValueChangedCallback(evt => {
                _exportFormat = (ExportFormat)evt.newValue;
                UpdateBottomBar();
            });
            _bottomBarContainer.Add(_exportFormatField);

            // Export Button
            _exportButton = new Button(() => {
                switch (_exportFormat)
                {
                    case ExportFormat.PNG:
                        ExportPNG();
                        break;
                    case ExportFormat.PSD:
                        ExportPSD();
                        break;
                    case ExportFormat.TGA:
                        ExportTGA();
                        break;
                }
            }) { text = PSDTranslation.Get("Export", "書き出し") };
            _exportButton.AddToClassList("button-primary");
            _exportButton.AddToClassList("export-button");
            _bottomBarContainer.Add(_exportButton);

            // Add bottom bar before the status bar
            int index = _rootContainer.IndexOf(_statusBarLabel);
            if (index >= 0)
            {
                _rootContainer.Insert(index, _bottomBarContainer);
            }
            else
            {
                _rootContainer.Add(_bottomBarContainer);
            }
        }

        void UpdateBottomBar()
        {
            if (_bottomBarContainer == null || _psdFile == null) return;

            // Info text
            _bottomInfoLabel.text = $"{_psdFile.Width} × {_psdFile.Height} px   |   " +
                                   $"{PSDTranslation.GetFormat("LayerCountFormat", CountLayersRecursive(_psdFile.Layers))}   |   " +
                                   $"{_psdFile.BitDepth}bit   |   " +
                                   ColorModeName(_psdFile.ColorMode);

            // Export button enabled state
            bool canExport = false;
            if (_exportFormat == ExportFormat.PNG || _exportFormat == ExportFormat.TGA)
            {
                canExport = _compositeRT != null;
            }
            else if (_exportFormat == ExportFormat.PSD)
            {
                canExport = _psdFile != null;
            }

            _exportButton.SetEnabled(canExport);
        }

        void BuildStatusBar()
        {
            _statusBarLabel = new Label();
            _statusBarLabel.AddToClassList("status-bar");
            _statusBarLabel.AddToClassList("status-info");
            _rootContainer.Add(_statusBarLabel);
        }

        void UpdateStatusBar()
        {
            if (_statusBarLabel == null) return;
            _statusBarLabel.text = _statusMessage;
            _statusBarLabel.ClearClassList();
            _statusBarLabel.AddToClassList("status-bar");
            switch (_statusType)
            {
                case StatusType.Success:
                    _statusBarLabel.AddToClassList("status-success");
                    break;
                case StatusType.Error:
                    _statusBarLabel.AddToClassList("status-error");
                    break;
                default:
                    _statusBarLabel.AddToClassList("status-info");
                    break;
            }
        }

        void UpdateSettingsFields()
        {
            if (_psdPathField != null) _psdPathField.SetValueWithoutNotify(_psdPath);
            if (_exportDirField != null) _exportDirField.SetValueWithoutNotify(_exportDir);
            if (_previewMaterialField != null) _previewMaterialField.SetValueWithoutNotify(_previewMaterial);
            if (_previewSlotField != null) _previewSlotField.SetValueWithoutNotify(_previewSlotName);
            UpdateRealtimePreviewButtonState();
            UpdateMergedRefButtonState();
        }

        /// <summary>プレビューマテリアルのシェーダーからテクスチャプロパティ一覧を取得し、選択メニューを表示する。</summary>
        void ShowTexturePropertyMenu()
        {
            var menu = new GenericMenu();

            if (_previewMaterial == null || _previewMaterial.shader == null)
            {
                menu.AddDisabledItem(new GUIContent(PSDTranslation.Get("MaterialNotSet", "マテリアルが未設定です")));
                menu.ShowAsContext();
                return;
            }

            var props = MaterialEditor.GetMaterialProperties(new Material[] { _previewMaterial });
            bool any = false;
            foreach (var prop in props)
            {
                if (prop.type != MaterialProperty.PropType.Texture) continue;
                any = true;

                string propName = prop.name;
                string display = string.IsNullOrEmpty(prop.displayName) || prop.displayName == propName
                    ? propName
                    : $"{prop.displayName} ({propName})";
                display = display.Replace('/', '∕'); // GenericMenu はスラッシュをサブメニュー区切りとして解釈するため置換

                menu.AddItem(new GUIContent(display), propName == _previewSlotName, () => {
                    if (_previewSlotName != propName)
                    {
                        RevertRealtimePreview();
                        _previewSlotName = propName;
                        if (_previewSlotField != null) _previewSlotField.SetValueWithoutNotify(_previewSlotName);
                        MarkDirty();
                    }
                });
            }

            if (!any)
            {
                menu.AddDisabledItem(new GUIContent(PSDTranslation.Get("TextureNotFound", "テクスチャ項目が見つかりません")));
            }

            menu.ShowAsContext();
        }

        void ToggleMergedRef()
        {
            _showMergedRef = !_showMergedRef;
            UpdateMergedRefButtonState();
            Repaint();
        }

        void UpdateMergedRefButtonState()
        {
            if (_mergedRefButton == null) return;
            if (_showMergedRef)
            {
                _mergedRefButton.text = PSDTranslation.Get("MergedRefActive", "マージ参照中");
                _mergedRefButton.AddToClassList("button-tool-active");
            }
            else
            {
                _mergedRefButton.text = PSDTranslation.Get("MergedRef", "マージ参照");
                _mergedRefButton.RemoveFromClassList("button-tool-active");
            }
        }

        void ToggleRealtimePreview()
        {
            _isRealtimePreviewEnabled = !_isRealtimePreviewEnabled;
            if (_isRealtimePreviewEnabled)
            {
                ApplyRealtimePreview();
            }
            else
            {
                RevertRealtimePreview();
            }
            MarkDirty();
            UpdateRealtimePreviewButtonState();
        }

        void UpdateRealtimePreviewButtonState()
        {
            if (_realtimePreviewButton == null) return;
            if (_isRealtimePreviewEnabled)
            {
                _realtimePreviewButton.text = PSDTranslation.Get("PreviewActive", "プレビュー中");
                _realtimePreviewButton.AddToClassList("button-tool-active");
            }
            else
            {
                _realtimePreviewButton.text = PSDTranslation.Get("Preview", "プレビュー");
                _realtimePreviewButton.RemoveFromClassList("button-tool-active");
            }
        }

        void RebuildLayerTree()
        {
            if (_layerTreeContainer == null) return;
            _layerTreeContainer.Clear();

            if (_psdFile == null || _psdFile.Layers == null) return;

            // PSDFile.Layers is sorted index 0 = bottom layer. We draw top-down.
            BuildLayerListTopDown(_layerTreeContainer, _psdFile.Layers, 0);
        }

        void BuildLayerListTopDown(VisualElement parent, List<PSDLayer> layers, int depth)
        {
            if (layers == null) return;
            for (int i = layers.Count - 1; i >= 0; i--)
            {
                var layer = layers[i];
                var nodeEl = BuildLayerNodeElement(layer, depth);
                parent.Add(nodeEl);
            }
        }

        VisualElement BuildLayerNodeElement(PSDLayer layer, int depth)
        {
            bool isGroup = layer.Children != null;
            if (isGroup)
            {
                return BuildGroupNodeElement(layer, depth);
            }
            else
            {
                return BuildLeafNodeElement(layer, depth);
            }
        }

        VisualElement BuildGroupNodeElement(PSDLayer layer, int depth)
        {
            var container = new VisualElement();
            container.AddToClassList("layer-group-outer");

            // Header row
            var header = new VisualElement();
            header.AddToClassList("layer-group-header");

            // Foldout button
            var foldoutBtn = new Button(() => {
                layer.IsExpanded = !layer.IsExpanded;
                RebuildLayerTree();
            }) { text = layer.IsExpanded ? "▾" : "▸" };
            foldoutBtn.AddToClassList("foldout-button");
            header.Add(foldoutBtn);

            // Visibility toggle
            var visibilityToggle = new Toggle();
            visibilityToggle.value = layer.UI.Visible;
            visibilityToggle.RegisterValueChangedCallback(evt => {
                layer.UI.Visible = evt.newValue;
                MarkDirty();
                RebuildLayerTree();
            });
            header.Add(visibilityToggle);

            // Label
            string labelText = BuildLayerLabel(layer, true);
            var label = new Label(labelText);
            label.AddToClassList("layer-name");
            header.Add(label);

            // Blend mode dropdown
            var blendModeDropdown = BuildBlendModeDropdown(layer, true);
            header.Add(blendModeDropdown);

            container.Add(header);

            // Body (shown when visible)
            if (layer.UI.Visible)
            {
                var body = new VisualElement();
                body.AddToClassList("layer-group-body");

                // Opacity/Controls in IMGUI
                var imguiContainer = new IMGUIContainer(() => {
                    PSDEditorTheme.PushEditorTheme();
                    try
                    {
                        bool isPassThrough = layer.GroupBlendMode == BlendMode.PassThrough;
                        using (new EditorGUI.DisabledScope(isPassThrough))
                        {
                            DrawOpacitySlider(layer, 0);
                        }
                        if (isPassThrough)
                        {
                            EditorGUILayout.BeginHorizontal();
                            GUILayout.Space(18f);
                            GUILayout.Label(PSDTranslation.Get("PassThroughWarning", "※ パススルー時は不透明度は適用されません"), PSDEditorTheme.CaptionStyle);
                            EditorGUILayout.EndHorizontal();
                        }
                    }
                    finally
                    {
                        PSDEditorTheme.PopEditorTheme();
                    }
                });
                body.Add(imguiContainer);

                if (layer.IsExpanded)
                {
                    var childrenContainer = new VisualElement();
                    BuildLayerListTopDown(childrenContainer, layer.Children, depth + 1);
                    body.Add(childrenContainer);
                }

                container.Add(body);
            }

            return container;
        }

        VisualElement BuildLeafNodeElement(PSDLayer layer, int depth)
        {
            var container = new VisualElement();
            container.AddToClassList("layer-leaf-card");

            var header = new VisualElement();
            header.AddToClassList("layer-leaf-header");

            // Spacer for foldout alignment
            var spacer = new VisualElement();
            spacer.AddToClassList("foldout-spacer");
            header.Add(spacer);

            // Visibility toggle
            var visibilityToggle = new Toggle();
            visibilityToggle.value = layer.UI.Visible;
            visibilityToggle.RegisterValueChangedCallback(evt => {
                layer.UI.Visible = evt.newValue;
                MarkDirty();
                RebuildLayerTree();
            });
            header.Add(visibilityToggle);

            // Label
            string labelText = BuildLayerLabel(layer, false);
            var label = new Label(labelText);
            label.AddToClassList("layer-name");
            header.Add(label);

            // Blend mode dropdown
            var blendModeDropdown = BuildBlendModeDropdown(layer, false);
            header.Add(blendModeDropdown);

            container.Add(header);

            // Controls (shown when visible)
            if (layer.UI.Visible)
            {
                var imguiContainer = new IMGUIContainer(() => {
                    PSDEditorTheme.PushEditorTheme();
                    try
                    {
                        DrawLayerControls(layer, 0);
                    }
                    finally
                    {
                        PSDEditorTheme.PopEditorTheme();
                    }
                });
                imguiContainer.AddToClassList("layer-controls");
                container.Add(imguiContainer);
            }

            return container;
        }

        VisualElement BuildBlendModeDropdown(PSDLayer layer, bool isGroup)
        {
            BlendMode[] modes = isGroup ? _blendModesGroup : _blendModesNormal;
            string[] labels = isGroup
                ? (_blendLabelsGroup ?? (_blendLabelsGroup = BuildBlendLabels(_blendModesGroup)))
                : (_blendLabelsNormal ?? (_blendLabelsNormal = BuildBlendLabels(_blendModesNormal)));

            BlendMode cur = isGroup ? layer.GroupBlendMode : layer.BlendMode;
            int curIndex = Mathf.Max(0, Array.IndexOf(modes, cur));

            var choices = new List<string>(labels);
            var dropdown = new DropdownField();
            dropdown.choices = choices;
            dropdown.index = curIndex;
            dropdown.AddToClassList("blend-dropdown");

            dropdown.RegisterValueChangedCallback(evt => {
                int index = dropdown.index;
                if (index >= 0 && index < modes.Length)
                {
                    if (isGroup) layer.GroupBlendMode = modes[index];
                    else layer.BlendMode = modes[index];
                    MarkDirty();
                }
            });

            return dropdown;
        }
    }
}
