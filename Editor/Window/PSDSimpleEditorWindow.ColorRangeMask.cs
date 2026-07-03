using System;
using System.IO;
using UnityEngine;
using UnityEditor;

namespace PSDSimpleEditor
{
    // ── 色域選択マスク: 対象色 ± 閾値でレイヤー自身の画素から選択範囲を作り PNG 出力 ──
    // ─── partial 見取り図 ───────────────────────────────────────────
    // 責務   : 色域選択マスクのパラメータ UI、プレビュー上でのスポイト色抽出、ハイライトプレビュー生成、PNG 書き出し
    // 宣言   : なし
    // 参照   : _eyedropperTarget (RW), _colorRangePreviewLayer (RW), _colorRangePreviewTex (RW),
    //          _colorRangePreviewDirty (RW), _exportDir (R)
    // 依存   : DrawSectionFoldout (.LayerPanel.cs), RowSpace (.LayerPanel.cs), SetStatus (本体)
    // ────────────────────────────────────────────────────────────────
    public partial class PSDSimpleEditorWindow
    {
        /// <summary>
        /// 色域選択マスク: このレイヤー自身の画素から、対象色 ± 閾値で選択範囲を作り PNG 出力する。
        /// スポイトはプレビューをクリックして「対象レイヤー自身の画素」を拾う (合成結果ではない)。
        /// </summary>
        void DrawColorRangeMaskControls(PSDLayer layer, int indent)
        {
            layer.UI.ColorRangeExpanded = DrawSectionFoldout(new GUIContent("色域選択マスク", "特定の色とその周辺色を抽出した選択範囲マスクを生成します。スポイトで色を選択し、閾値で範囲を広げられます。"), layer.UI.ColorRangeExpanded, indent, layer, ClipboardKind.ColorRangeMask);
            if (!layer.UI.ColorRangeExpanded)
            {
                // フォールドアウトを閉じたら、このレイヤーのスポイト待機・ハイライトを解除する
                if (_eyedropperTarget == layer)        _eyedropperTarget = null;
                if (_colorRangePreviewLayer == layer)  EndColorRangePreview();
                RowSpace();
                return;
            }
            RowSpace();

            // 対象色 + スポイトトグル
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(indent * IndentWidth + 18f);
            GUILayout.Label(new GUIContent("対象色", "抽出する対象の色を指定します。"), PSDEditorTheme.ControlLabelStyle,
                            GUILayout.Width(48), GUILayout.Height(RowH));
            Color nc = EditorGUILayout.ColorField(new GUIContent("", "抽出する対象の色を指定します。"), layer.UI.ColorRangeTarget, true, false, false,
                                                  GUILayout.Width(80), GUILayout.Height(RowH));
            GUILayout.FlexibleSpace();
            bool armed = _eyedropperTarget == layer;
            bool newArmed = GUILayout.Toggle(armed, new GUIContent("スポイト", "スポイトツールを有効にします。プレビュー上でクリックした対象レイヤーの画素色を直接取得できます。"), PSDEditorTheme.MiniButtonStyle,
                                             GUILayout.Width(60), GUILayout.Height(RowH));
            EditorGUILayout.EndHorizontal();
            if (nc != layer.UI.ColorRangeTarget)
            {
                layer.UI.ColorRangeTarget = nc;
                BeginColorRangePreview(layer);   // 対象色の編集後はハイライトプレビューを表示
            }
            if (newArmed != armed)
            {
                _eyedropperTarget = newArmed ? layer : null;
                Repaint();
            }
            RowSpace();

            if (_eyedropperTarget == layer)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(indent * IndentWidth + 18f);
                GUILayout.Label("プレビュー上の対象レイヤー領域をクリックで色を取得",
                                PSDEditorTheme.ControlLabelStyle, GUILayout.Height(RowH));
                EditorGUILayout.EndHorizontal();
                RowSpace();
            }

            // 閾値
            float nt = IndentedSlider(new GUIContent("閾値", "指定した対象色から許容する色のズレ幅を設定します。値が大きいほど、似た色を広く含めるようになります（0.0で完全一致のみ）。"), layer.UI.ColorRangeThreshold, 0f, 1f, indent);
            if (!Mathf.Approximately(nt, layer.UI.ColorRangeThreshold))
            {
                layer.UI.ColorRangeThreshold = nt;
                BeginColorRangePreview(layer);   // 閾値変更でもハイライトを更新表示
            }

            // ボタン行: [プレビュー終了] [マスクを PNG 出力]
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(indent * IndentWidth + 18f);
            using (new EditorGUI.DisabledScope(_colorRangePreviewLayer != layer))
            {
                if (GUILayout.Button(new GUIContent("プレビュー終了", "画面上の赤色の選択範囲ハイライト表示を終了します。"), PSDEditorTheme.ToolButtonStyle, GUILayout.ExpandWidth(true)))
                    EndColorRangePreview();
            }
            if (GUILayout.Button(new GUIContent("マスクを PNG 出力", "現在の選択範囲を白、それ以外を黒（透明部分含む）としたグレースケールのマスク画像（PNG）を出力先フォルダへ書き出します。"), PSDEditorTheme.ToolButtonStyle, GUILayout.ExpandWidth(true)))
                ExportColorRangeMask(layer);
            EditorGUILayout.EndHorizontal();
            RowSpace();
        }

        /// <summary>指定レイヤーの色域選択ハイライトプレビューを開始/更新する。</summary>
        void BeginColorRangePreview(PSDLayer layer)
        {
            _colorRangePreviewLayer = layer;
            _colorRangePreviewDirty = true;
            Repaint();
        }

        /// <summary>色域選択ハイライトプレビューを終了し、テクスチャを破棄する。</summary>
        void EndColorRangePreview()
        {
            _colorRangePreviewLayer = null;
            _colorRangePreviewDirty = false;
            SafeDestroy(ref _colorRangePreviewTex);
            Repaint();
        }

        /// <summary>
        /// 色域選択ハイライト: 対象レイヤーの選択範囲を色付きテクスチャでプレビューへ重ね描きする。
        /// レイヤーのキャンバス矩形をプレビュー描画矩形へ写像して配置する。
        /// </summary>
        void DrawColorRangeHighlight(Rect drawRect)
        {
            if (_colorRangePreviewLayer == null || _psdFile == null) return;

            EnsureColorRangePreviewTex();
            if (_colorRangePreviewTex == null) return;

            var layer = _colorRangePreviewLayer;
            if (layer.Width <= 0 || layer.Height <= 0) return;

            float fw = _psdFile.Width;
            float fh = _psdFile.Height;
            var r = new Rect(
                drawRect.x + (layer.Left / fw) * drawRect.width,
                drawRect.y + (layer.Top  / fh) * drawRect.height,
                (layer.Width  / fw) * drawRect.width,
                (layer.Height / fh) * drawRect.height);
            GUI.DrawTexture(r, _colorRangePreviewTex, ScaleMode.StretchToFill, true);
        }

        /// <summary>ハイライト用テクスチャを (必要時のみ) 再生成する。</summary>
        void EnsureColorRangePreviewTex()
        {
            if (!_colorRangePreviewDirty) return;
            _colorRangePreviewDirty = false;

            SafeDestroy(ref _colorRangePreviewTex);

            var layer = _colorRangePreviewLayer;
            if (layer == null) return;

            var px = ColorRangeMask.BuildHighlightPixels(
                layer, layer.UI.ColorRangeTarget, layer.UI.ColorRangeThreshold,
                ColorRangeHighlightColor, out int w, out int h);
            if (px == null) return;

            _colorRangePreviewTex = new Texture2D(w, h, TextureFormat.RGBA32, false, linear: false)
            { hideFlags = HideFlags.HideAndDontSave };
            _colorRangePreviewTex.SetPixels32(px);
            _colorRangePreviewTex.Apply(false);
        }

        /// <summary>
        /// 色域選択スポイト: プレビュー描画矩形上のクリックを対象レイヤーのローカル画素へ写像し、
        /// そのレイヤー自身の色を対象色として取得する (合成結果ではなくレイヤー画素を拾う)。
        /// </summary>
        void HandleEyedropper(Rect drawRect)
        {
            var layer = _eyedropperTarget;
            if (layer == null || layer.Texture == null) return;

            // armed の視覚化 (カーソル変更)
            EditorGUIUtility.AddCursorRect(drawRect, MouseCursor.Link);

            Event e = Event.current;
            if (e.type != EventType.MouseDown || e.button != 0) return;
            if (!drawRect.Contains(e.mousePosition)) return;

            // プレビュー (キャンバス全面・左上原点) 内の正規化座標 → キャンバス px
            float u = (e.mousePosition.x - drawRect.x) / drawRect.width;
            float v = (e.mousePosition.y - drawRect.y) / drawRect.height;
            int cx = Mathf.FloorToInt(u * _psdFile.Width);
            int cy = Mathf.FloorToInt(v * _psdFile.Height);

            // レイヤーローカル (左上原点)
            int lx = cx - layer.Left;
            int ly = cy - layer.Top;
            if (lx < 0 || ly < 0 || lx >= layer.Width || ly >= layer.Height)
            {
                // 対象レイヤーの外をクリック → 取得しない (待機は継続)
                e.Use();
                return;
            }

            // layer.Texture はボトムアップ。トップダウン ly を反転して取得。
            int by = layer.Height - 1 - ly;
            Color picked = layer.Texture.GetPixel(lx, by);
            picked.a = 1f;
            layer.UI.ColorRangeTarget = picked;

            _eyedropperTarget = null;          // 1 回拾ったら解除
            BeginColorRangePreview(layer);     // 取得色でハイライトプレビューを表示
            e.Use();
        }

        /// <summary>色域選択マスクを生成し、Export Dir へグレースケール PNG として書き出す。</summary>
        void ExportColorRangeMask(PSDLayer layer)
        {
            if (layer == null || layer.Texture == null)
            {
                EditorUtility.DisplayDialog("エラー",
                    "このレイヤーには画素がないため、色域選択マスクを生成できません。", "OK");
                return;
            }
            if (string.IsNullOrEmpty(_exportDir))
            {
                EditorUtility.DisplayDialog("エラー", "出力先フォルダが指定されていません。", "OK");
                return;
            }

            Texture2D maskTex = null;
            try
            {
                var layerPixels = ColorRangeMask.BuildMaskPixels(
                    layer, layer.UI.ColorRangeTarget, layer.UI.ColorRangeThreshold,
                    out int lw, out int lh);
                if (layerPixels == null)
                {
                    EditorUtility.DisplayDialog("エラー", "マスクの生成に失敗しました。", "OK");
                    return;
                }

                // キャンバス全体サイズで黒マスクを作り、レイヤー位置へ貼り付ける
                int cw = _psdFile.Width;
                int ch = _psdFile.Height;
                var canvas = new Color32[cw * ch];
                for (int i = 0; i < canvas.Length; i++)
                    canvas[i] = new Color32(0, 0, 0, 255);

                // Unity テクスチャはボトムアップ。レイヤー pixel y=0 は PSD 下端 (Bottom-1行目)
                // キャンバス上の対応行: ch - layer.Bottom + y_l
                int baseRow = ch - layer.Bottom;
                for (int y = 0; y < lh; y++)
                {
                    int canvasY = baseRow + y;
                    if (canvasY < 0 || canvasY >= ch) continue;
                    for (int x = 0; x < lw; x++)
                    {
                        int canvasX = layer.Left + x;
                        if (canvasX < 0 || canvasX >= cw) continue;
                        canvas[canvasY * cw + canvasX] = layerPixels[y * lw + x];
                    }
                }

                maskTex = new Texture2D(cw, ch, TextureFormat.RGBA32, false, linear: false)
                { hideFlags = HideFlags.HideAndDontSave };
                maskTex.SetPixels32(canvas);
                maskTex.Apply(false);

                string psdName  = string.IsNullOrEmpty(_psdPath)
                    ? "psd" : Path.GetFileNameWithoutExtension(_psdPath);
                string baseName = SanitizeFileName($"{psdName}_{layer.Name}_mask");
                string savePath = GetUniqueExportPath(_exportDir, baseName, ".png");

                byte[] png = maskTex.EncodeToPNG();
                File.WriteAllBytes(savePath, png);
                Debug.Log($"[PSDSimpleEditor] 色域選択マスクを保存しました: {savePath}");
                SetStatus($"色域選択マスクを書き出しました: {Path.GetFileName(savePath)}", StatusType.Success);

                // プロジェクト内なら AssetDatabase を更新して Ping、外なら Finder/Explorer を開く
                string normalized = savePath.Replace('\\', '/');
                int assetsIndex = normalized.IndexOf("/Assets/", StringComparison.OrdinalIgnoreCase);
                if (assetsIndex != -1)
                {
                    string assetPath = normalized.Substring(assetsIndex + 1);
                    AssetDatabase.Refresh();
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
            catch (Exception ex)
            {
                Debug.LogError($"[PSDSimpleEditor] 色域選択マスク保存失敗: {ex}");
                EditorUtility.DisplayDialog("書き出しエラー",
                    $"マスクの保存に失敗しました:\n{ex.Message}", "OK");
            }
            finally
            {
                SafeDestroy(ref maskTex);
            }
        }

        /// <summary>ファイル名に使えない文字を '_' へ置換する。</summary>
        static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "mask";
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }
    }
}
