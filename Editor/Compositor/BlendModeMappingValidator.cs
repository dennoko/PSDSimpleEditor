using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace PSDSimpleEditor
{
    /// <summary>
    /// BlendMode enum の int 値と LayerBlend.shader のブレンド分岐番号の
    /// 1:1 対応 (凍結契約) をシェーダーソースの照合で検証する。
    /// ドメインリロードごとに初回 1 回のみ実行。不整合は LogError で報告する
    /// (合成は止めない — 誤検出でツールが使えなくなる方が害が大きい)。
    /// </summary>
    internal static class BlendModeMappingValidator
    {
        static bool s_done;

        // 例: "else if (mode == 11) return B_LinearDodge (cb, cs);   // LinearDodge (Add)"
        //  → 番号 11, 名前 "LinearDodge"
        static readonly Regex BranchPattern = new Regex(
            @"mode\s*==\s*(\d+)\s*\).*?//\s*(\w+)", RegexOptions.Compiled);

        internal static void ValidateOnce(Shader shader)
        {
            if (s_done || shader == null) return;
            s_done = true;

            string path = AssetDatabase.GetAssetPath(shader);
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return; // ビルトイン化等で読めない場合は黙ってスキップ

            string src;
            try { src = File.ReadAllText(path); }
            catch (Exception) { return; }

            int checkedCount = 0;
            foreach (Match m in BranchPattern.Matches(src))
            {
                int    num  = int.Parse(m.Groups[1].Value);
                string name = m.Groups[2].Value;
                if (!Enum.TryParse(name, out BlendMode mode))
                    continue; // enum 名でないコメント (説明文など) は無視
                checkedCount++;
                if ((int)mode != num)
                    Debug.LogError($"[LayerCompositor] BlendMode 凍結契約違反: シェーダー分岐 {num} のコメントは " +
                                   $"{name} だが、enum {name} の値は {(int)mode}。" +
                                   "PSDData.cs と LayerBlend.shader を確認してください。");
            }

            // 分岐の抽出自体に失敗した場合 (シェーダー改修で書式が変わった等) も気付けるようにする
            if (checkedCount < 20)
                Debug.LogWarning($"[LayerCompositor] BlendMode 整合チェック: 照合できた分岐が {checkedCount} 件しかありません。" +
                                 "LayerBlend.shader の分岐コメント書式が変わっていないか確認してください。");
        }
    }
}
