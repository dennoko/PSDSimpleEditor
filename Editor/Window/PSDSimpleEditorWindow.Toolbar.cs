using System;
using System.IO;
using UnityEngine;
using UnityEditor;

namespace PSDSimpleEditor
{
    // ── ツールバー (PSD パス入力 / Export Dir 入力 / 履歴) ──────────────────
    public partial class PSDSimpleEditorWindow
    {
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

            if (GUILayout.Button("履歴 ▾", EditorStyles.toolbarDropDown, GUILayout.Width(60)))
                ShowHistoryMenu();

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

        void DrawPreviewBar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            GUILayout.Label("Preview Mat:", GUILayout.Width(76));
            
            // マテリアル選択フィールド
            Material prevMat = (Material)EditorGUILayout.ObjectField(
                GUIContent.none, _previewMaterial, typeof(Material), true, GUILayout.Width(150));
            
            if (prevMat != _previewMaterial)
            {
                RevertRealtimePreview();
                _previewMaterial = prevMat;
                _needsRecomposite = true;
            }

            GUILayout.Label("Slot:", GUILayout.Width(32));
            string prevSlot = EditorGUILayout.TextField(_previewSlotName, EditorStyles.toolbarTextField, GUILayout.Width(100));
            if (prevSlot != _previewSlotName)
            {
                RevertRealtimePreview();
                _previewSlotName = prevSlot;
                _needsRecomposite = true;
            }

            GUILayout.Space(8);

            // プレビューの有効化トグル
            bool prevEnabled = GUILayout.Toggle(_isRealtimePreviewEnabled, "3Dプレビュー反映",
                                                EditorStyles.toolbarButton, GUILayout.Width(110));
            
            if (prevEnabled != _isRealtimePreviewEnabled)
            {
                _isRealtimePreviewEnabled = prevEnabled;
                if (_isRealtimePreviewEnabled)
                {
                    ApplyRealtimePreview();
                }
                else
                {
                    RevertRealtimePreview();
                }
                _needsRecomposite = true;
            }

            GUILayout.FlexibleSpace();

            EditorGUILayout.EndHorizontal();
        }


        // ── 履歴ドロップダウン ───────────────────────────────────────────────

        /// <summary>履歴ドロップダウンを表示する。項目選択で即ロードする。</summary>
        void ShowHistoryMenu()
        {
            var list = _history.Items;
            var menu = new GenericMenu();

            if (list.Count == 0)
            {
                menu.AddDisabledItem(new GUIContent("(履歴なし)"));
            }
            else
            {
                for (int i = 0; i < list.Count; i++)
                {
                    string path = list[i];
                    bool exists = File.Exists(path);
                    // GenericMenu は '/' をサブメニュー区切りとして解釈するため除算記号へ置換
                    string display = $"{i + 1}: {path}".Replace('/', '∕');
                    if (!exists) display += "  (見つかりません)";

                    if (exists)
                        menu.AddItem(new GUIContent(display), false, () => LoadFromHistory(path));
                    else
                        menu.AddDisabledItem(new GUIContent(display));
                }
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("履歴をクリア"), false, ClearHistory);
            }

            menu.ShowAsContext();
        }

        /// <summary>履歴から選んだパスを入力欄へ反映してロードする。</summary>
        void LoadFromHistory(string path)
        {
            _psdPath = path;
            GUI.FocusControl(null);  // テキストフィールドの古い表示を解除
            LoadPSD();
        }

        /// <summary>このプロジェクトの履歴を全消去する。</summary>
        void ClearHistory()
        {
            _history.Clear();
        }
    }
}
