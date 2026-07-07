using UnityEditor;
using UnityEngine.UIElements;

namespace PSDSimpleEditor
{
    // ─── partial 見取り図 ───────────────────────────────────────────
    // 責務   : ヘッダーのバージョン表記 + GitHub version.json を用いたアップデートチェック配線。
    //          チェッカー本体は DennokoVersionChecker (自己完結) / 版数定数は PSDEditorVersion。
    // 宣言   : _versionLabel, _versionResult
    // 参照   : PSDTranslation (状態→文言化), SessionState (同一セッション中の再フェッチ抑制)
    // 依存   : BuildHeader (.UIToolkit.cs) が _versionLabel を生成し StartVersionCheck を呼ぶ
    // ────────────────────────────────────────────────────────────────
    public partial class PSDSimpleEditorWindow
    {
        private Label _versionLabel;
        private DennokoVersionChecker.Result _versionResult = new DennokoVersionChecker.Result
        {
            State = DennokoVersionChecker.State.Checking,
            LocalVersion = PSDEditorVersion.Current,
        };

        const string VerCheckDoneKey   = "DennokoPSDEditor_VerCheck_Done";
        const string VerCheckStateKey  = "DennokoPSDEditor_VerCheck_State";
        const string VerCheckLatestKey = "DennokoPSDEditor_VerCheck_Latest";

        void StartVersionCheck()
        {
            // 同一 Unity セッション中はキャッシュ結果を再利用し、ウィンドウ開閉のたびに叩かない
            if (SessionState.GetBool(VerCheckDoneKey, false))
            {
                _versionResult = new DennokoVersionChecker.Result
                {
                    State = (DennokoVersionChecker.State)SessionState.GetInt(VerCheckStateKey, 0),
                    LocalVersion = PSDEditorVersion.Current,
                    LatestVersion = SessionState.GetString(VerCheckLatestKey, string.Empty),
                };
                ApplyVersionLabel();
                return;
            }

            ApplyVersionLabel(); // Checking 状態を先に反映
            DennokoVersionChecker.CheckAsync(
                PSDEditorVersion.RepoOwner, PSDEditorVersion.RepoName, PSDEditorVersion.RepoBranch,
                PSDEditorVersion.VersionFilePath, PSDEditorVersion.Current, OnVersionChecked);
        }

        void OnVersionChecked(DennokoVersionChecker.Result result)
        {
            _versionResult = result;
            SessionState.SetBool(VerCheckDoneKey, true);
            SessionState.SetInt(VerCheckStateKey, (int)result.State);
            SessionState.SetString(VerCheckLatestKey, result.LatestVersion ?? string.Empty);
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
