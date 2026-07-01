using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;

namespace PSDSimpleEditor
{
    // ── 下部バー / PNG・PSD 書き出し ────────────────────────────────────────
    public partial class PSDSimpleEditorWindow
    {
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

                // 新規 PSD として保存したパスを履歴へ記録
                _history.Add(savePath.Replace('\\', '/'));

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
    }
}
