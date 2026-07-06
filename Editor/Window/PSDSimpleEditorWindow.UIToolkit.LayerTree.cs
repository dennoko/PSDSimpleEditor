using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;

namespace PSDSimpleEditor
{
    // ── UI Toolkit レイヤーツリー構築 ───────────────────────────────────────
    // ─── partial 見取り図 ───────────────────────────────────────────
    // 責務   : レイヤーツリーの VisualElement 構築 (グループ/リーフノード、ブレンドモード
    //          Dropdown、IMGUIContainer 経由のレイヤーコントロール埋め込み)
    // 宣言   : なし
    // 参照   : _layerTreeContainer (R), _psdFile (R), _blendModesGroup/Normal (R),
    //          _blendLabelsGroup/Normal (RW)
    // 依存   : DrawLayerControls / DrawOpacitySlider / BuildLayerLabel / BuildBlendLabels
    //          (.LayerPanel.cs), MarkDirty (本体)
    // ────────────────────────────────────────────────────────────────
    public partial class PSDSimpleEditorWindow
    {
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
