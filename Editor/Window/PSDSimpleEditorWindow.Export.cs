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
            // 書き出しボタン (28) の高さに各要素を合わせて縦中央そろえにする。
            // CaptionStyle の stretchHeight を廃止済みのため、行はこの内容高さ + カード余白に収まる。
            const float BarH = 28f;
            EditorGUILayout.BeginHorizontal(PSDEditorTheme.CardStyle);

            GUILayout.Label(
                $"{_psdFile.Width} × {_psdFile.Height} px   |   " +
                $"レイヤー数 {CountLayersRecursive(_psdFile.Layers)}   |   " +
                $"{_psdFile.BitDepth}bit   |   " +
                ColorModeName(_psdFile.ColorMode),
                PSDEditorTheme.CaptionStyle, GUILayout.Height(BarH));

            GUILayout.FlexibleSpace();

            // エクスポート形式の選択
            GUILayout.Label(new GUIContent("形式", "書き出す画像のファイルフォーマットを指定します。\n・PNG: 合成結果をアルファ付きPNGとして書き出します。\n・PSD: 現在の編集パラメータを維持したままPSDとして書き出します。\n・TGA: 32bit（アルファあり）のTGA形式で書き出します。"), PSDEditorTheme.ControlLabelStyle, GUILayout.Width(30), GUILayout.Height(BarH));
            float originalLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 1f;
            _exportFormat = (ExportFormat)EditorGUILayout.EnumPopup(new GUIContent("", "出力形式の選択"), _exportFormat,
                                                                    GUILayout.Width(80), GUILayout.Height(RowH));
            EditorGUIUtility.labelWidth = originalLabelWidth;

            bool canExport = false;
            if (_exportFormat == ExportFormat.PNG || _exportFormat == ExportFormat.TGA)
            {
                canExport = _compositeTexture != null;
            }
            else if (_exportFormat == ExportFormat.PSD)
            {
                canExport = _psdFile != null;
            }

            using (new EditorGUI.DisabledScope(!canExport))
            {
                if (GUILayout.Button(new GUIContent("書き出し", "指定した形式で、出力先フォルダまたは指定したパスに画像を保存します。"), PSDEditorTheme.ActionButtonStyle, GUILayout.Width(120)))
                {
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
                }
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
                SetStatus($"PNG を書き出しました: {Path.GetFileName(savePath)}", StatusType.Success);

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
                SetStatus($"PNG の書き出しに失敗しました: {e.Message}", StatusType.Error);
                EditorUtility.DisplayDialog("書き出しエラー",
                    $"PNG の保存に失敗しました:\n{e.Message}", "OK");
            }
        }

        /// <summary>指定されたディレクトリ、ファイル名で衝突しない一意なパスを取得する。</summary>
        string GetUniqueExportPath(string dir, string baseNameWithoutExt, string ext)
        {
            string targetDir = dir;
            // "Assets" で始まる相対パスをプロジェクトルートからの絶対パスに変換
            bool isAssetsRelative =
                dir.Equals("Assets", StringComparison.OrdinalIgnoreCase) ||
                dir.StartsWith("Assets/",  StringComparison.OrdinalIgnoreCase) ||
                dir.StartsWith("Assets\\", StringComparison.OrdinalIgnoreCase);
            if (isAssetsRelative)
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

            int lossCount = CountUnsupportedForPsdExport(_psdFile.Layers);
            if (lossCount > 0)
            {
                bool proceed = EditorUtility.DisplayDialog("PSD 書き出しの注意",
                    $"調整レイヤー・べた塗り・レイヤー効果など、PSD 書き出しで保持できない" +
                    $"設定を持つレイヤーが {lossCount} 件あります。\n" +
                    "これらは空のレイヤーとして書き出され、Photoshop で開くと見た目が変わる可能性があります。\n\n" +
                    "続行しますか?",
                    "書き出す", "キャンセル");
                if (!proceed) return;
            }

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
                SetStatus($"PSD を書き出しました: {Path.GetFileName(savePath)}", StatusType.Success);

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
                SetStatus($"PSD の書き出しに失敗しました: {e.Message}", StatusType.Error);
                EditorUtility.DisplayDialog("書き出しエラー",
                    $"PSD の保存に失敗しました:\n{e.Message}", "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        // ── TGA 書き出し ───────────────────────────────────────────────────

        void ExportTGA()
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

                string savePath = GetUniqueExportPath(_exportDir, baseName, ".tga");

                byte[] tga = EncodeToTGA(_compositeTexture);
                File.WriteAllBytes(savePath, tga);
                Debug.Log($"[PSDSimpleEditor] TGA を保存しました: {savePath}");
                SetStatus($"TGA を書き出しました: {Path.GetFileName(savePath)}", StatusType.Success);

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
                Debug.LogError($"[PSDSimpleEditor] TGA 保存失敗: {e}");
                SetStatus($"TGA の書き出しに失敗しました: {e.Message}", StatusType.Error);
                EditorUtility.DisplayDialog("書き出しエラー",
                    $"TGA の保存に失敗しました:\n{e.Message}", "OK");
            }
        }

        /// <summary>32bit 未圧縮 TGA にエンコードする。</summary>
        byte[] EncodeToTGA(Texture2D tex)
        {
            int width = tex.width;
            int height = tex.height;
            Color32[] pixels = tex.GetPixels32();

            byte[] tga = new byte[18 + width * height * 4];

            // TGAヘッダー(18バイト)
            tga[2] = 2; // 未圧縮True-Color
            tga[12] = (byte)(width & 0xFF);
            tga[13] = (byte)((width >> 8) & 0xFF);
            tga[14] = (byte)(height & 0xFF);
            tga[15] = (byte)((height >> 8) & 0xFF);
            tga[16] = 32; // 32bit (アルファあり)
            tga[17] = 8;  // アルファチャンネルの深さは8bit

            // ピクセルデータ書き込み (BGRA順)
            int idx = 18;
            for (int i = 0; i < pixels.Length; i++)
            {
                Color32 p = pixels[i];
                tga[idx++] = p.b;
                tga[idx++] = p.g;
                tga[idx++] = p.r;
                tga[idx++] = p.a;
            }

            return tga;
        }

        /// <summary>PSD 書き出しで内容を保持できないレイヤー (調整/SoCo/効果) を数える。</summary>
        static int CountUnsupportedForPsdExport(List<PSDLayer> layers)
        {
            if (layers == null) return 0;
            int count = 0;
            foreach (var l in layers)
            {
                var a = l.Adjustment;
                bool hasUnsupported =
                    (l.IsAdjustmentLayer && a != null &&
                     (a.HasBrightnessContrast || a.HasHueSaturation || a.HasSolidColor ||
                      a.HasInvert || a.HasThreshold || a.HasPosterize || a.HasLevels ||
                      a.HasCurves || a.HasGradientMap || a.HasColorBalance))
                    || (l.Effects != null && l.Effects.HasColorOverlay);
                if (hasUnsupported) count++;
                count += CountUnsupportedForPsdExport(l.Children);
            }
            return count;
        }
    }
}
