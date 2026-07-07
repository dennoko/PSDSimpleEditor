namespace PSDSimpleEditor
{
    /// <summary>
    /// ツールの現行バージョンとアップデートチェック先リポジトリを 1 か所にまとめる定数。
    /// リリースのたびに <see cref="Current"/> を上げ、リモート直下の version.json と揃える。
    /// </summary>
    internal static class PSDEditorVersion
    {
        internal const string Current = "1.0.0";

        // チェック先 (設定されているリモートリポジトリ: dennoko/PSDSimpleEditor)
        internal const string RepoOwner       = "dennoko";
        internal const string RepoName        = "PSDSimpleEditor";
        internal const string RepoBranch      = "master";
        internal const string VersionFilePath = "version.json";
    }
}
