using System;
using UnityEngine;
using UnityEngine.Networking;

namespace PSDSimpleEditor
{
    /// <summary>
    /// GitHub Public リポジトリ上の version.json を取得し、ローカル版と比較する
    /// エディタ専用の自己完結アップデートチェッカー。
    ///
    /// version.json の形式:
    ///   { "version": "1.2.0", "url": "https://.../releases", "message": "" }
    ///
    /// owner / repo は各プロジェクトの「設定されているリモートリポジトリ」を
    /// 呼び出し側から渡す (ハードコードしない)。文言は返さず State だけ返す。
    /// </summary>
    public static class DennokoVersionChecker
    {
        public enum State { Checking, UpToDate, UpdateAvailable, Error }

        public struct Result
        {
            public State State;
            public string LocalVersion;
            public string LatestVersion;
            public string Url;
            public string Message;
        }

        [Serializable]
        private class VersionInfo
        {
            public string version;
            public string url;
            public string message;
        }

        /// <summary>
        /// version.json を非同期取得して結果を onResult に渡す。例外は投げず、失敗時は
        /// State.Error を返す。onResult は Unity のメインスレッド上で呼ばれる。
        /// </summary>
        public static void CheckAsync(
            string owner, string repo, string branch, string filePath,
            string localVersion, Action<Result> onResult)
        {
            if (onResult == null) return;

            UnityWebRequest req;
            try
            {
                var url = $"https://raw.githubusercontent.com/{owner}/{repo}/{branch}/{filePath}";
                req = UnityWebRequest.Get(url);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DennokoVersionChecker] request build failed: {e.Message}");
                onResult(Error(localVersion));
                return;
            }

            var op = req.SendWebRequest();
            op.completed += _ =>
            {
                try
                {
                    onResult(BuildResult(req, localVersion));
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[DennokoVersionChecker] callback failed: {e.Message}");
                    onResult(Error(localVersion));
                }
                finally
                {
                    req.Dispose();
                }
            };
        }

        private static Result BuildResult(UnityWebRequest req, string localVersion)
        {
#if UNITY_2020_2_OR_NEWER
            bool hasError = req.result != UnityWebRequest.Result.Success;
#else
            bool hasError = req.isNetworkError || req.isHttpError;
#endif
            if (hasError) return Error(localVersion);

            var json = req.downloadHandler != null ? req.downloadHandler.text : null;
            if (string.IsNullOrEmpty(json)) return Error(localVersion);

            VersionInfo info;
            try { info = JsonUtility.FromJson<VersionInfo>(json); }
            catch { return Error(localVersion); }

            if (info == null || string.IsNullOrEmpty(info.version)) return Error(localVersion);

            var state = IsNewer(info.version, localVersion) ? State.UpdateAvailable : State.UpToDate;
            return new Result
            {
                State = state,
                LocalVersion = localVersion,
                LatestVersion = info.version,
                Url = info.url,
                Message = info.message,
            };
        }

        private static Result Error(string localVersion) => new Result
        {
            State = State.Error,
            LocalVersion = localVersion,
            LatestVersion = null,
            Url = null,
            Message = null,
        };

        /// <summary>latest がローカル版より新しいか。SemVer 優先、パース不能時は文字列不一致で判定。</summary>
        private static bool IsNewer(string latest, string local)
        {
            var l = Normalize(latest);
            var c = Normalize(local);
            if (Version.TryParse(l, out var vLatest) && Version.TryParse(c, out var vLocal))
                return vLatest > vLocal;
            return !string.Equals(l, c, StringComparison.OrdinalIgnoreCase);
        }

        private static string Normalize(string v)
        {
            if (string.IsNullOrEmpty(v)) return "0";
            v = v.Trim();
            if (v.StartsWith("v") || v.StartsWith("V")) v = v.Substring(1);
            return v;
        }
    }
}
