using UnityEditor;
using UnityEngine.UIElements;

namespace PSDSimpleEditor
{
    // ─── partial 見取り図 ───────────────────────────────────────────
    // 責務   : ヘッダーのバージョン表記 + アップデートチェック結果の表示。取得と SessionState への
    //          保存は PSDEditorVersion (自己完結・InitializeOnLoad) が担い、ここは表示専任。
    //          State はキャッシュせず「現在のローカル版 vs 取得済みの最新版」で都度再計算する。
    // 宣言   : _versionLabel, _versionReloadButton, _versionResult
    // 参照   : PSDTranslation (状態→文言化), SessionState (PSDEditorVersion が保存した結果)
    // 依存   : BuildHeader (.UIToolkit.cs) が _versionLabel/_versionReloadButton を生成し
    //          StartVersionCheck を呼ぶ。PSDEditorVersion.OnVersionChecked が
    //          LoadVersionResultFromSessionState を呼んで再描画する。
    // ────────────────────────────────────────────────────────────────
    public partial class PSDSimpleEditorWindow
    {
        private Label _versionLabel;
        private Button _versionReloadButton;
        private DennokoVersionChecker.Result _versionResult = new DennokoVersionChecker.Result
        {
            State = DennokoVersionChecker.State.Checking,
            LocalVersion = PSDEditorVersion.Current,
        };

        void StartVersionCheck()
        {
            LoadVersionResultFromSessionState();
            // 取得の要否は StartCheckBackgroundTask 内で判定する (成功済みなら何もしない／
            // 前回エラーなら再試行)。ウィンドウを開き直すたびに一時的な失敗から自己回復できる。
            PSDEditorVersion.StartCheckBackgroundTask();
        }

        internal void LoadVersionResultFromSessionState()
        {
            // State (更新有無) はキャッシュせず、常に「現在のローカル版 vs 取得済みの最新版」で
            // 再計算する。こうしないと、取得時のローカル版が後から正しく解決された場合に
            // 「v1.0.0 更新あり 1.0.0」のような矛盾表示が残ってしまう。
            string local  = PSDEditorVersion.Current;
            string latest = SessionState.GetString(PSDEditorVersion.VerCheckLatestKey, string.Empty);
            bool   done   = SessionState.GetBool(PSDEditorVersion.VerCheckDoneKey, false);
            bool   error  = SessionState.GetBool(PSDEditorVersion.VerCheckErrorKey, false);

            DennokoVersionChecker.State state;
            if (!done)
                state = DennokoVersionChecker.State.Checking;
            else if (error || string.IsNullOrEmpty(latest))
                state = DennokoVersionChecker.State.Error;
            else if (DennokoVersionChecker.IsUpdateAvailable(latest, local))
                state = DennokoVersionChecker.State.UpdateAvailable;
            else
                state = DennokoVersionChecker.State.UpToDate;

            _versionResult = new DennokoVersionChecker.Result
            {
                State = state,
                LocalVersion = local,
                LatestVersion = latest,
                Url = SessionState.GetString(PSDEditorVersion.VerCheckUrlKey, string.Empty),
                Message = SessionState.GetString(PSDEditorVersion.VerCheckMessageKey, string.Empty),
            };
            ApplyVersionLabel();
        }

        void ApplyVersionLabel()
        {
            if (_versionLabel == null) return;

            var r = _versionResult;
            string baseText = "v" + r.LocalVersion;
            string text;
            bool update = false, error = false;
            switch (r.State)
            {
                case DennokoVersionChecker.State.UpdateAvailable:
                    text = baseText + "  " + PSDTranslation.GetFormat("VersionUpdateAvailable", r.LatestVersion);
                    update = true;
                    break;
                case DennokoVersionChecker.State.Error:
                    text = baseText + "  " + PSDTranslation.Get("VersionCheckFailed", "最新版を取得できません");
                    error = true;
                    break;
                case DennokoVersionChecker.State.Checking:
                    text = baseText + "  " + PSDTranslation.Get("VersionChecking", "確認中...");
                    break;
                default: // UpToDate
                    text = baseText;
                    break;
            }
            _versionLabel.text = text;
            _versionLabel.EnableInClassList("version-label--update", update);
            _versionLabel.EnableInClassList("version-label--error", error);
        }
    }
}
