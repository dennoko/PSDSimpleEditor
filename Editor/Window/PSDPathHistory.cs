using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PSDSimpleEditor
{
    /// <summary>
    /// PSD 読み込み履歴 (プロジェクトごとに EditorPrefs へ永続化)。新しいものが先頭。
    /// </summary>
    internal class PSDPathHistory
    {
        const int HistoryMaxCount = 20; // プロジェクトごとの履歴保存上限

        /// <summary>JsonUtility 用の履歴シリアライズコンテナ。</summary>
        [Serializable]
        class HistoryData { public List<string> paths = new List<string>(); }

        List<string> _items; // 遅延ロードされる履歴

        /// <summary>EditorPrefs はインストール全体で共有されるため、プロジェクトパスでキーを分けて「プロジェクトごと」にする。</summary>
        static string PrefsKey =>
            "PSDSimpleEditor.History." + Application.dataPath.GetHashCode().ToString("X8");

        /// <summary>履歴一覧 (遅延ロード。新しいものが先頭)。</summary>
        public List<string> Items => Load();

        List<string> Load()
        {
            if (_items != null) return _items;
            _items = new List<string>();

            string json = EditorPrefs.GetString(PrefsKey, "");
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    var data = JsonUtility.FromJson<HistoryData>(json);
                    if (data?.paths != null) _items = data.paths;
                }
                catch { /* 壊れた値は無視して空履歴とする */ }
            }
            return _items;
        }

        void Save()
        {
            if (_items == null) return;
            EditorPrefs.SetString(PrefsKey, JsonUtility.ToJson(new HistoryData { paths = _items }));
        }

        /// <summary>パスを履歴の先頭へ追加する。重複は除去し、上限 (20件) を超えた分は切り捨てる。</summary>
        public void Add(string path)
        {
            if (string.IsNullOrEmpty(path)) return;

            var list = Load();
            // 同一パス (大文字小文字無視) を除去してから先頭へ挿入
            list.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
            list.Insert(0, path);
            if (list.Count > HistoryMaxCount)
                list.RemoveRange(HistoryMaxCount, list.Count - HistoryMaxCount);

            Save();
        }

        /// <summary>このプロジェクトの履歴を全消去する。</summary>
        public void Clear()
        {
            _items = new List<string>();
            Save();
        }
    }
}
