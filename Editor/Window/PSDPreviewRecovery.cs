using System;
using UnityEngine;
using UnityEditor;

namespace PSDSimpleEditor
{
    /// <summary>
    /// リアルタイムプレビュー中の異常終了やクラッシュ、自動コンパイルによる
    /// マテリアルテクスチャ設定の破損を防ぎ、自動復元するためのクラス。
    /// InitializeOnLoad を用いて Unity 起動時およびドメインリロード（コンパイル）時に復元処理を評価する。
    /// </summary>
    [InitializeOnLoad]
    public static class PSDPreviewRecovery
    {
        private const string PrefKey = "PSDSE_PreviewBackup";

        [Serializable]
        private class PreviewBackupData
        {
            public string MaterialGuid;
            public string PropertyName;
            public string OriginalTextureGuid;
        }

        static PSDPreviewRecovery()
        {
            // エディタ起動およびコンパイル後に、アセットのロード準備が整ってから実行されるよう遅延コールに登録する
            EditorApplication.delayCall += CheckAndRecover;
        }

        /// <summary>
        /// プレビュー開始時にマテリアルと元のテクスチャの情報を退避して永続化する。
        /// </summary>
        public static void SaveBackup(Material material, string propertyName, Texture originalTexture)
        {
            if (material == null || string.IsNullOrEmpty(propertyName)) return;

            string matPath = AssetDatabase.GetAssetPath(material);
            string matGuid = AssetDatabase.AssetPathToGUID(matPath);
            if (string.IsNullOrEmpty(matGuid)) return; // アセットデータベースに登録されていないマテリアルは対象外

            string texPath = originalTexture != null ? AssetDatabase.GetAssetPath(originalTexture) : "";
            string texGuid = !string.IsNullOrEmpty(texPath) ? AssetDatabase.AssetPathToGUID(texPath) : "";

            var data = new PreviewBackupData
            {
                MaterialGuid = matGuid,
                PropertyName = propertyName,
                OriginalTextureGuid = texGuid
            };

            string json = JsonUtility.ToJson(data);
            EditorPrefs.SetString(PrefKey, json);
        }

        /// <summary>
        /// プレビュー終了または正常に復元された場合に、バックアップデータを消去する。
        /// </summary>
        public static void ClearBackup()
        {
            if (EditorPrefs.HasKey(PrefKey))
            {
                EditorPrefs.DeleteKey(PrefKey);
            }
        }

        /// <summary>
        /// 起動時またはコンパイル後に復元用データが残存していれば、マテリアルに元のテクスチャを設定し直す。
        /// </summary>
        private static void CheckAndRecover()
        {
            if (!EditorPrefs.HasKey(PrefKey)) return;

            string json = EditorPrefs.GetString(PrefKey);
            if (string.IsNullOrEmpty(json)) return;

            try
            {
                var data = JsonUtility.FromJson<PreviewBackupData>(json);
                if (data != null && !string.IsNullOrEmpty(data.MaterialGuid))
                {
                    string matPath = AssetDatabase.GUIDToAssetPath(data.MaterialGuid);
                    var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                    if (mat != null)
                    {
                        Texture originalTex = null;
                        if (!string.IsNullOrEmpty(data.OriginalTextureGuid))
                        {
                            string texPath = AssetDatabase.GUIDToAssetPath(data.OriginalTextureGuid);
                            originalTex = AssetDatabase.LoadAssetAtPath<Texture>(texPath);
                        }

                        // 元のテクスチャを再アサインして修復
                        mat.SetTexture(data.PropertyName, originalTex);
                        
                        // シーンのダーティフラグを立てて保存を促す、またはAssetのダーティ化
                        EditorUtility.SetDirty(mat);
                        AssetDatabase.SaveAssetIfDirty(mat);

                        Debug.Log($"[PSDSimpleEditor] 異常終了またはリロードに伴うマテリアルテクスチャの復元を実行しました: {mat.name} ({data.PropertyName} -> {originalTex?.name ?? "null"})");
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[PSDSimpleEditor] 自動復元中にエラーが発生しました: {e.Message}\n{e.StackTrace}");
            }
            finally
            {
                // 一度復元を試みたら、成否にかかわらずバックアップキーは削除して二重実行を防ぐ
                EditorPrefs.DeleteKey(PrefKey);
            }
        }
    }
}
