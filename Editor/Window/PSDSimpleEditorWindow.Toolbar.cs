using System;
using System.IO;
using UnityEngine;
using UnityEditor;

namespace PSDSimpleEditor
{
    // ── 設定カード (PSD パス / 出力先 / マテリアルプレビュー / 履歴) ────────────
    // ─── partial 見取り図 ───────────────────────────────────────────
    // 責務   : PSD パスの読み込み履歴ドロップダウンメニューの描画と制御
    // 宣言   : なし
    // 参照   : _history (RW), _psdPath (W)
    // 依存   : LoadPSD (本体)
    // ────────────────────────────────────────────────────────────────
    public partial class PSDSimpleEditorWindow
    {
        // ── 履歴ドロップダウン ───────────────────────────────────────────────

        /// <summary>履歴ドロップダウンを表示する。項目選択で即ロードする。</summary>
        void ShowHistoryMenu()
        {
            var list = _history.Items;
            var menu = new GenericMenu();

            if (list.Count == 0)
            {
                menu.AddDisabledItem(new GUIContent(PSDTranslation.Get("NoHistory", "(履歴なし)")));
            }
            else
            {
                for (int i = 0; i < list.Count; i++)
                {
                    string path = list[i];
                    bool exists = File.Exists(path);
                    // GenericMenu は '/' をサブメニュー区切りとして解釈するため除算記号へ置換
                    string display = $"{i + 1}: {path}".Replace('/', '∕');
                    if (!exists) display += PSDTranslation.Get("HistoryNotFound", "  (見つかりません)");

                    if (exists)
                        menu.AddItem(new GUIContent(display), false, () => LoadFromHistory(path));
                    else
                        menu.AddDisabledItem(new GUIContent(display));
                }
                menu.AddSeparator("");
                menu.AddItem(new GUIContent(PSDTranslation.Get("HistoryClear", "履歴をクリア")), false, ClearHistory);
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
