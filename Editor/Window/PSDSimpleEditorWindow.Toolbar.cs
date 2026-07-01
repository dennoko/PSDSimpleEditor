using System;
using System.IO;
using UnityEngine;
using UnityEditor;

namespace PSDSimpleEditor
{
    // ── 設定カード (PSD パス / 出力先 / 3D プレビュー反映 / 履歴) ────────────
    public partial class PSDSimpleEditorWindow
    {
        const float SettingsLabelWidth = 68f;   // 設定カード左ラベル幅
        const float SettingsButtonWidth = 60f;  // 「参照」等ボタン幅

        /// <summary>入出力設定をまとめたカード。PSD 入力 / 出力先 / 3D プレビュー反映の 3 行。</summary>
        void DrawSettingsCard()
        {
            EditorGUILayout.BeginVertical(PSDEditorTheme.CardStyle);

            DrawPsdInputRow();
            DrawSeparator();
            DrawExportDirRow();
            DrawSeparator();
            DrawRealtimePreviewRow();

            EditorGUILayout.EndVertical();
        }

        // ── 行: PSD 入力 ────────────────────────────────────────────────────
        void DrawPsdInputRow()
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("PSD", PSDEditorTheme.ControlLabelStyle, GUILayout.Width(SettingsLabelWidth));

            _psdPath = EditorGUILayout.TextField(_psdPath, GUILayout.ExpandWidth(true));

            if (GUILayout.Button("参照", PSDEditorTheme.ToolButtonStyle, GUILayout.Width(SettingsButtonWidth)))
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

            if (GUILayout.Button("読み込み", PSDEditorTheme.ToolButtonStyle, GUILayout.Width(72)))
                LoadPSD();

            if (GUILayout.Button("履歴 ▾", PSDEditorTheme.ToolButtonStyle, GUILayout.Width(SettingsButtonWidth)))
                ShowHistoryMenu();

            EditorGUILayout.EndHorizontal();
        }

        // ── 行: 出力先フォルダ ──────────────────────────────────────────────
        void DrawExportDirRow()
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("出力先", PSDEditorTheme.ControlLabelStyle, GUILayout.Width(SettingsLabelWidth));

            _exportDir = EditorGUILayout.TextField(_exportDir, GUILayout.ExpandWidth(true));

            if (GUILayout.Button("参照", PSDEditorTheme.ToolButtonStyle, GUILayout.Width(SettingsButtonWidth)))
            {
                string picked = EditorUtility.OpenFolderPanel("出力先フォルダを選択", _exportDir, "");
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

        // ── 行: 3D プレビュー反映 ───────────────────────────────────────────
        void DrawRealtimePreviewRow()
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("3D反映", PSDEditorTheme.ControlLabelStyle, GUILayout.Width(SettingsLabelWidth));

            // マテリアル選択フィールド
            Material prevMat = (Material)EditorGUILayout.ObjectField(
                GUIContent.none, _previewMaterial, typeof(Material), true, GUILayout.Width(160));

            if (prevMat != _previewMaterial)
            {
                RevertRealtimePreview();
                _previewMaterial = prevMat;
                _needsRecomposite = true;
            }

            GUILayout.Label("スロット", PSDEditorTheme.ControlLabelStyle, GUILayout.Width(48));
            string prevSlot = EditorGUILayout.TextField(_previewSlotName, GUILayout.Width(110));
            if (prevSlot != _previewSlotName)
            {
                RevertRealtimePreview();
                _previewSlotName = prevSlot;
                _needsRecomposite = true;
            }

            GUILayout.FlexibleSpace();

            // プレビューの有効化トグル
            bool prevEnabled = GUILayout.Toggle(_isRealtimePreviewEnabled, "反映",
                                                PSDEditorTheme.ToolButtonStyle, GUILayout.Width(64));

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
