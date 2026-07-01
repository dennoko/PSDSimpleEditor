using UnityEngine;
using UnityEditor;

namespace PSDSimpleEditor
{
    // ── プレビューパネル (右): 合成結果表示 / チェッカー背景 / マージ参照小窓 ──
    public partial class PSDSimpleEditorWindow
    {
        void DrawPreviewPanel()
        {
            EditorGUILayout.BeginVertical();
            GUILayout.Label("プレビュー", EditorStyles.boldLabel);

            Rect area = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none,
                                                 GUILayout.ExpandWidth(true),
                                                 GUILayout.ExpandHeight(true));

            bool hasPreview = _compositeTexture != null && area.width > 8f && area.height > 8f;

            // 描画矩形は全イベントフェーズで必要 (スポイトのヒット判定に使う)
            Rect drawRect = default;
            if (hasPreview)
            {
                float aspect = (float)_compositeTexture.width / _compositeTexture.height;
                drawRect = FitRectKeepAspect(area, aspect);
            }

            // スポイト (色域選択マスク): 対象レイヤー自身の画素を拾う
            if (hasPreview && _eyedropperTarget != null)
                HandleEyedropper(drawRect);

            if (Event.current.type == EventType.Repaint)
            {
                if (hasPreview)
                {
                    // 透明部可視化のチェッカー背景 → その上にアルファ合成で描画
                    DrawCheckerBackground(drawRect);
                    GUI.DrawTexture(drawRect, _compositeTexture, ScaleMode.StretchToFill, true);

                    // 色域選択ハイライト (対象レイヤーの選択範囲を色付きで重ね描き)
                    DrawColorRangeHighlight(drawRect);

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
    }
}
