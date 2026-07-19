using System;
using System.IO;
using UnityEngine;
using UnityEditor;

namespace PSDSimpleEditor
{
    // ── マスク生成: 色域選択マスク + 不透明範囲マスクの PNG 出力 ──
    // ─── partial 見取り図 ───────────────────────────────────────────
    // 責務   : 「マスク生成」セクション UI (色域選択マスク / 不透明範囲マスク)、
    //          プレビュー上でのスポイト色抽出、ハイライトプレビュー生成、PNG 書き出し。
    //          レイヤー・グループ (フォルダ) の両方を対象にする (グループは配下の平坦化結果を走査)。
    // 宣言   : なし
    // 参照   : _eyedropperTarget (RW), _colorRangePreviewLayer (RW), _colorRangePreviewTex (RW),
    //          _colorRangePreviewDirty (RW), _exportDir (R), _compositor (R)
    // 依存   : DrawSectionFoldout (.LayerPanel.cs), RowSpace (.LayerPanel.cs), SetStatus (本体)
    // ────────────────────────────────────────────────────────────────
    public partial class PSDSimpleEditorWindow
    {
        /// <summary>
        /// 「マスク生成」フォールドアウト。色域選択マスクと不透明範囲マスクをまとめる。
        /// グループ (フォルダ) の場合は配下を平坦化した画素を走査対象にする。
        /// </summary>
        void DrawMaskGenControls(PSDLayer layer, int indent)
        {
            layer.UI.MaskGenExpanded = DrawSectionFoldout(
                new GUIContent(PSDTranslation.Get("MaskGeneration", "マスク生成"),
                               PSDTranslation.Get("MaskGenerationTooltip", "このレイヤーの画素からマスク画像 (PNG) を生成します。色域選択マスクと不透明範囲マスクを出力できます。")),
                layer.UI.MaskGenExpanded, indent);
            if (!layer.UI.MaskGenExpanded)
            {
                // フォールドアウトを閉じたら、このレイヤーのスポイト待機・ハイライトを解除する
                CancelColorRangeUIFor(layer);
                RowSpace();
                return;
            }
            RowSpace();

            int ci = indent + 1;
            DrawColorRangeMaskSection(layer, ci);
            DrawOpacityMaskSection(layer, ci);
        }

        /// <summary>このレイヤーが対象のスポイト待機・ハイライトプレビューを解除する。</summary>
        void CancelColorRangeUIFor(PSDLayer layer)
        {
            if (_eyedropperTarget == layer)        _eyedropperTarget = null;
            if (_colorRangePreviewLayer == layer)  EndColorRangePreview();
        }

        /// <summary>
        /// 色域選択マスク: 対象画素から、対象色 ± 閾値で選択範囲を作り PNG 出力する。
        /// スポイトはプレビューをクリックして「走査対象の画素」を拾う (背景との合成結果ではない)。
        /// </summary>
        void DrawColorRangeMaskSection(PSDLayer layer, int indent)
        {
            layer.UI.ColorRangeExpanded = DrawSectionFoldout(new GUIContent(PSDTranslation.Get("ColorRangeMask", "色域選択マスク"), PSDTranslation.Get("ColorRangeMaskTooltip", "特定の色とその周辺色を抽出した選択範囲マスクを生成します。スポイトで色を選択し、閾値で範囲を広げられます。")), layer.UI.ColorRangeExpanded, indent, layer, ClipboardKind.ColorRangeMask);
            if (!layer.UI.ColorRangeExpanded)
            {
                CancelColorRangeUIFor(layer);
                RowSpace();
                return;
            }
            RowSpace();

            // 対象色 + スポイトトグル
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(indent * IndentWidth + 18f);
            GUILayout.Label(new GUIContent(PSDTranslation.Get("ColorRangeTarget", "対象色"), PSDTranslation.Get("ColorRangeTargetTooltip", "抽出する対象の色を指定します。")), PSDEditorTheme.ControlLabelStyle,
                            GUILayout.Width(48), GUILayout.Height(RowH));
            Color nc = EditorGUILayout.ColorField(new GUIContent("", PSDTranslation.Get("ColorRangeTargetTooltip", "抽出する対象の色を指定します。")), layer.UI.ColorRangeTarget, true, false, false,
                                                  GUILayout.Width(80), GUILayout.Height(RowH));
            GUILayout.FlexibleSpace();
            bool armed = _eyedropperTarget == layer;
            bool newArmed = GUILayout.Toggle(armed, new GUIContent(PSDTranslation.Get("Eyedropper", "スポイト"), PSDTranslation.Get("EyedropperTooltip", "スポイトツールを有効にします。プレビュー上でクリックした対象レイヤーの画素色を直接取得できます。")), PSDEditorTheme.MiniButtonStyle,
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
                GUILayout.Label(PSDTranslation.Get("EyedropperPrompt", "プレビュー上の対象レイヤー領域をクリックで色を取得"),
                                System.String.Equals(PSDTranslation.CurrentLanguage, "ja") ? PSDEditorTheme.ControlLabelStyle : PSDEditorTheme.CaptionStyle, GUILayout.Height(RowH));
                EditorGUILayout.EndHorizontal();
                RowSpace();
            }

            // 閾値
            float nt = IndentedSlider(new GUIContent(PSDTranslation.Get("ThresholdSlider", "閾値"), PSDTranslation.Get("ThresholdSliderTooltip", "指定した対象色から許容する色のズレ幅を設定します。値が大きいほど、似た色を広く含めるようになります（0.0で完全一致のみ）。")), layer.UI.ColorRangeThreshold, 0f, 1f, indent);
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
                if (GUILayout.Button(new GUIContent(PSDTranslation.Get("EndPreview", "プレビュー終了"), PSDTranslation.Get("EndPreviewTooltip", "画面上の赤色の選択範囲ハイライト表示を終了します。")), PSDEditorTheme.ToolButtonStyle, GUILayout.ExpandWidth(true)))
                    EndColorRangePreview();
            }
            if (GUILayout.Button(new GUIContent(PSDTranslation.Get("ExportMaskPng", "マスクを PNG 出力"), PSDTranslation.Get("ExportMaskPngTooltip", "現在の選択範囲を白、それ以外を黒（透明部分含む）としたグレースケールのマスク画像（PNG）を出力先フォルダへ書き出します。")), PSDEditorTheme.ToolButtonStyle, GUILayout.ExpandWidth(true)))
                ExportColorRangeMask(layer);
            EditorGUILayout.EndHorizontal();
            RowSpace();
        }

        /// <summary>不透明範囲マスク: 色のある (不透明な) 範囲の α をグレースケールマスクとして PNG 出力する。</summary>
        void DrawOpacityMaskSection(PSDLayer layer, int indent)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(indent * IndentWidth + 18f);
            if (GUILayout.Button(new GUIContent(PSDTranslation.Get("ExportOpacityMaskPng", "不透明範囲マスクを PNG 出力"),
                                                PSDTranslation.Get("ExportOpacityMaskPngTooltip", "色のある範囲（不透明部分）を白、透明部分を黒としたグレースケールのマスク画像（PNG）を出力先フォルダへ書き出します。半透明部分はグレーになります。")),
                                 PSDEditorTheme.ToolButtonStyle, GUILayout.ExpandWidth(true)))
                ExportOpacityMask(layer);
            EditorGUILayout.EndHorizontal();
            RowSpace();
        }

        // 走査元ピクセルのキャッシュ (レイヤーテクスチャは読み込み後不変のため、
        // 同一レイヤーのプレビュー更新中は GetPixels32 を繰り返さない。
        // グループの平坦化結果は内容が変わるため、再合成時に InvalidateGroupMaskSourceCache で破棄する)
        [NonSerialized] Color32[] _colorRangeSrcPixels;
        [NonSerialized] PSDLayer  _colorRangeSrcLayer;

        /// <summary>
        /// 走査元ピクセル (ボトムアップ) を (レイヤー単位でキャッシュしつつ) 取得する。
        /// グループはコンポジターで配下を平坦化したキャンバスサイズの画素を返す。
        /// </summary>
        Color32[] GetColorRangeSourcePixels(PSDLayer layer)
        {
            if (_colorRangeSrcLayer != layer || _colorRangeSrcPixels == null)
            {
                _colorRangeSrcLayer  = layer;
                _colorRangeSrcPixels = layer.Children != null
                    ? _compositor?.RenderGroupPixels(layer)
                    : ColorRangeMask.GetSourcePixels(layer);
            }
            return _colorRangeSrcPixels;
        }

        /// <summary>
        /// 走査対象のキャンバス上の矩形 (PSD 左上原点)。
        /// グループはキャンバス全面、レイヤーは自身のバウンディングボックス。
        /// </summary>
        void GetMaskSourceRect(PSDLayer layer, out int left, out int top, out int w, out int h)
        {
            if (layer.Children != null)
            {
                left = 0; top = 0;
                w = _psdFile != null ? _psdFile.Width  : 0;
                h = _psdFile != null ? _psdFile.Height : 0;
            }
            else
            {
                left = layer.Left; top = layer.Top;
                w = layer.Width; h = layer.Height;
            }
        }

        /// <summary>
        /// 再合成でグループの内容が変わった可能性があるため、グループ由来の走査元キャッシュを破棄し、
        /// 表示中のハイライトプレビューがあれば作り直しを予約する。RecompositeNow から呼ぶ。
        /// </summary>
        void InvalidateGroupMaskSourceCache()
        {
            if (_colorRangeSrcLayer == null || _colorRangeSrcLayer.Children == null) return;
            _colorRangeSrcLayer  = null;
            _colorRangeSrcPixels = null;
            if (_colorRangePreviewLayer != null && _colorRangePreviewLayer.Children != null)
                _colorRangePreviewDirty = true;
        }

        /// <summary>指定レイヤーの色域選択ハイライトプレビューを開始/更新する。</summary>
        void BeginColorRangePreview(PSDLayer layer)
        {
            _colorRangePreviewLayer = layer;
            _colorRangePreviewDirty = true;
            Repaint();
        }

        /// <summary>色域選択ハイライトプレビューを終了し、テクスチャ・キャッシュを破棄する。</summary>
        void EndColorRangePreview()
        {
            _colorRangePreviewLayer = null;
            _colorRangePreviewDirty = false;
            _colorRangeSrcLayer     = null;
            _colorRangeSrcPixels    = null;
            SafeDestroy(ref _colorRangePreviewTex);
            Repaint();
        }

        /// <summary>
        /// 色域選択ハイライト: 対象の選択範囲を色付きテクスチャでプレビューへ重ね描きする。
        /// 走査対象の矩形 (レイヤー矩形 / グループはキャンバス全面) をプレビュー描画矩形へ写像して配置する。
        /// </summary>
        void DrawColorRangeHighlight(Rect drawRect)
        {
            if (_colorRangePreviewLayer == null || _psdFile == null) return;

            EnsureColorRangePreviewTex();
            if (_colorRangePreviewTex == null) return;

            var layer = _colorRangePreviewLayer;
            GetMaskSourceRect(layer, out int left, out int top, out int w, out int h);
            if (w <= 0 || h <= 0) return;

            float fw = _psdFile.Width;
            float fh = _psdFile.Height;
            var r = new Rect(
                drawRect.x + (left / fw) * drawRect.width,
                drawRect.y + (top  / fh) * drawRect.height,
                (w / fw) * drawRect.width,
                (h / fh) * drawRect.height);
            GUI.DrawTexture(r, _colorRangePreviewTex, ScaleMode.StretchToFill, true);
        }

        /// <summary>ハイライト用テクスチャを (必要時のみ) 再生成する。同サイズなら再利用する。</summary>
        void EnsureColorRangePreviewTex()
        {
            if (!_colorRangePreviewDirty) return;
            _colorRangePreviewDirty = false;

            var layer = _colorRangePreviewLayer;
            if (layer == null) { SafeDestroy(ref _colorRangePreviewTex); return; }

            var src = GetColorRangeSourcePixels(layer);
            GetMaskSourceRect(layer, out _, out _, out int w, out int h);
            var px = ColorRangeMask.BuildHighlightPixels(
                src, w, h, layer.UI.ColorRangeTarget, layer.UI.ColorRangeThreshold,
                ColorRangeHighlightColor);
            if (px == null) { SafeDestroy(ref _colorRangePreviewTex); return; }

            // スライダードラッグ中の連続更新で毎回破棄・生成しない (サイズ一致時は書き換えのみ)
            if (_colorRangePreviewTex == null ||
                _colorRangePreviewTex.width != w || _colorRangePreviewTex.height != h)
            {
                SafeDestroy(ref _colorRangePreviewTex);
                _colorRangePreviewTex = new Texture2D(w, h, TextureFormat.RGBA32, false, linear: false)
                { hideFlags = HideFlags.HideAndDontSave };
            }
            _colorRangePreviewTex.SetPixels32(px);
            _colorRangePreviewTex.Apply(false);
        }

        /// <summary>
        /// 色域選択スポイト: プレビュー描画矩形上のクリックを走査対象のローカル画素へ写像し、
        /// その画素の色を対象色として取得する (背景との合成結果ではなく走査対象の画素を拾う)。
        /// </summary>
        void HandleEyedropper(Rect drawRect)
        {
            var layer = _eyedropperTarget;
            if (layer == null) return;
            if (layer.Children == null && layer.Texture == null) return;

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

            // 走査対象ローカル (左上原点)
            GetMaskSourceRect(layer, out int left, out int top, out int w, out int h);
            int lx = cx - left;
            int ly = cy - top;
            if (lx < 0 || ly < 0 || lx >= w || ly >= h)
            {
                // 対象領域の外をクリック → 取得しない (待機は継続)
                e.Use();
                return;
            }

            var src = GetColorRangeSourcePixels(layer);
            if (src == null || src.Length < w * h) { e.Use(); return; }

            // 走査元はボトムアップ。トップダウン ly を反転して取得。
            int by = h - 1 - ly;
            Color picked = src[by * w + lx];
            picked.a = 1f;
            layer.UI.ColorRangeTarget = picked;

            _eyedropperTarget = null;          // 1 回拾ったら解除
            BeginColorRangePreview(layer);     // 取得色でハイライトプレビューを表示
            e.Use();
        }

        /// <summary>色域選択マスクを生成し、Export Dir へグレースケール PNG として書き出す。</summary>
        void ExportColorRangeMask(PSDLayer layer)
        {
            if (!ValidateMaskExportTarget(layer)) return;

            GetMaskSourceRect(layer, out int left, out int top, out int w, out int h);
            var maskPixels = ColorRangeMask.BuildMaskPixels(
                GetColorRangeSourcePixels(layer), w, h,
                layer.UI.ColorRangeTarget, layer.UI.ColorRangeThreshold);
            if (maskPixels == null)
            {
                EditorUtility.DisplayDialog(PSDTranslation.Get("Error", "エラー"), PSDTranslation.Get("ColorRangeErrorFailed", "マスクの生成に失敗しました。"), "OK");
                return;
            }

            SaveMaskPng(layer, maskPixels, left, top, w, h, "mask",
                path => PSDTranslation.GetFormat("ColorRangeSuccessFormat", path));
        }

        /// <summary>不透明範囲マスク (α のグレースケール) を生成し、Export Dir へ PNG として書き出す。</summary>
        void ExportOpacityMask(PSDLayer layer)
        {
            if (!ValidateMaskExportTarget(layer)) return;

            GetMaskSourceRect(layer, out int left, out int top, out int w, out int h);
            var maskPixels = ColorRangeMask.BuildOpacityMaskPixels(GetColorRangeSourcePixels(layer), w, h);
            if (maskPixels == null)
            {
                EditorUtility.DisplayDialog(PSDTranslation.Get("Error", "エラー"), PSDTranslation.Get("ColorRangeErrorFailed", "マスクの生成に失敗しました。"), "OK");
                return;
            }

            SaveMaskPng(layer, maskPixels, left, top, w, h, "opacity_mask",
                path => PSDTranslation.GetFormat("OpacityMaskSuccessFormat", path));
        }

        /// <summary>マスク書き出しの共通事前チェック (走査対象の有無 / 出力先フォルダ)。</summary>
        bool ValidateMaskExportTarget(PSDLayer layer)
        {
            bool hasPixels = layer != null && (layer.Children != null ? _compositor != null : layer.Texture != null);
            if (!hasPixels)
            {
                EditorUtility.DisplayDialog(PSDTranslation.Get("Error", "エラー"),
                    PSDTranslation.Get("ColorRangeErrorNoPixels", "このレイヤーには画素がないため、色域選択マスクを生成できません。"), "OK");
                return false;
            }
            if (string.IsNullOrEmpty(_exportDir))
            {
                EditorUtility.DisplayDialog(PSDTranslation.Get("Error", "エラー"), PSDTranslation.Get("ColorRangeErrorNoExportDir", "出力先フォルダが指定されていません。"), "OK");
                return false;
            }
            return true;
        }

        /// <summary>
        /// マスク画素 (ボトムアップ・矩形サイズ) をキャンバス全体サイズの黒地へ貼り付けて
        /// PNG として保存する共通処理。保存後はプロジェクト内なら Ping、外ならエクスプローラー表示。
        /// </summary>
        void SaveMaskPng(PSDLayer layer, Color32[] maskPixels, int left, int top, int w, int h,
                         string suffix, Func<string, string> successMessage)
        {
            Texture2D maskTex = null;
            try
            {
                // キャンバス全体サイズで黒マスクを作り、走査矩形位置へ貼り付ける
                int cw = _psdFile.Width;
                int ch = _psdFile.Height;
                var canvas = new Color32[cw * ch];
                for (int i = 0; i < canvas.Length; i++)
                    canvas[i] = new Color32(0, 0, 0, 255);

                // Unity テクスチャはボトムアップ。マスク pixel y=0 は矩形の PSD 下端行。
                // キャンバス上の対応行: ch - (top + h) + y
                int baseRow = ch - (top + h);
                for (int y = 0; y < h; y++)
                {
                    int canvasY = baseRow + y;
                    if (canvasY < 0 || canvasY >= ch) continue;
                    for (int x = 0; x < w; x++)
                    {
                        int canvasX = left + x;
                        if (canvasX < 0 || canvasX >= cw) continue;
                        canvas[canvasY * cw + canvasX] = maskPixels[y * w + x];
                    }
                }

                maskTex = new Texture2D(cw, ch, TextureFormat.RGBA32, false, linear: false)
                { hideFlags = HideFlags.HideAndDontSave };
                maskTex.SetPixels32(canvas);
                maskTex.Apply(false);

                string psdName  = string.IsNullOrEmpty(_psdPath)
                    ? "psd" : Path.GetFileNameWithoutExtension(_psdPath);
                string baseName = SanitizeFileName($"{psdName}_{layer.Name}_{suffix}");
                string savePath = GetUniqueExportPath(_exportDir, baseName, ".png");

                byte[] png = maskTex.EncodeToPNG();
                File.WriteAllBytes(savePath, png);
                Debug.Log($"[PSDSimpleEditor] マスクを保存しました: {savePath}");
                SetStatus(successMessage(Path.GetFileName(savePath)), StatusType.Success);

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
                Debug.LogError($"[PSDSimpleEditor] マスク保存失敗: {ex}");
                EditorUtility.DisplayDialog(PSDTranslation.Get("ExportError", "書き出しエラー"),
                    PSDTranslation.GetFormat("ColorRangeErrorSaveFailedFormat", ex.Message), "OK");
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
