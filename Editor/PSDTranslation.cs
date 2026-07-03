using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PSDSimpleEditor
{
    [Serializable]
    public class TranslationItem
    {
        public string key;
        public string value;
    }

    [Serializable]
    public class TranslationData
    {
        public List<TranslationItem> items;
    }

    public static class PSDTranslation
    {
        private static Dictionary<string, string> _translations = new Dictionary<string, string>();
        private static string _currentLang = "ja";

        public static string CurrentLanguage => _currentLang;

        public static string Get(string key, string defaultValue = "")
        {
            if (_translations.TryGetValue(key, out string val))
                return val;
            return string.IsNullOrEmpty(defaultValue) ? key : defaultValue;
        }

        public static string GetFormat(string key, params object[] args)
        {
            string fmt = Get(key);
            try
            {
                return string.Format(fmt, args);
            }
            catch (Exception)
            {
                return fmt;
            }
        }

        public static void LoadLanguage(string lang)
        {
            _currentLang = lang;
            _translations.Clear();

            string fileName = $"translation_{lang}";
            
            // Try loading directly via AssetDatabase if path is predictable
            string directPath = $"Assets/dennokoworks/DennokoPSDEditor/Editor/Translations/{fileName}.json";
            var jsonTextAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(directPath);
            
            if (jsonTextAsset == null)
            {
                // Fallback to find by GUID in case user moved directory
                var guids = AssetDatabase.FindAssets($"{fileName} t:TextAsset");
                if (guids.Length > 0)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                    jsonTextAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
                }
            }

            if (jsonTextAsset != null)
            {
                try
                {
                    var data = JsonUtility.FromJson<TranslationData>(jsonTextAsset.text);
                    if (data != null && data.items != null)
                    {
                        foreach (var item in data.items)
                        {
                            _translations[item.key] = item.value;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[PSDSimpleEditor] Failed to parse translation JSON: {ex.Message}");
                }
            }
            else
            {
                Debug.LogWarning($"[PSDSimpleEditor] Translation file not found: {fileName}.json");
            }
        }
    }
}
