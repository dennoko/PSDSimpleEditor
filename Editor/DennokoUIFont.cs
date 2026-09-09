using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using FontAsset = UnityEngine.TextCore.Text.FontAsset;

namespace PSDSimpleEditor
{
    /// <summary>
    /// dennokoworks UI の標準フォント（OS のメイリオ）を SDF FontAsset として生成・保持し、
    /// フォントキャッシュ消失によるテキスト崩壊を自己修復する共通クラス。
    ///
    /// ⚠ EditorWindow / Inspector 側でフォントを直接生成しないこと。
    ///   ルート要素に対して <see cref="Apply"/> を 1 回呼ぶだけでよい。
    ///
    /// 守っている失敗経路（詳細は dennokoworks_color_schema skill の troubleshooting §8〜§10）:
    ///   ① 実行中に増える追加アトラス Texture2D に hideFlags が付かず UnloadUnusedAssets で破棄される
    ///   ② 破棄済み（fake-null）の FontAsset をキャッシュから返し続ける
    ///   ③ ドメインリロードで static 参照だけ消え、FontAsset とアトラスが leak し続ける
    ///
    /// 本ツールは PNG / マスク書き出し時に AssetDatabase.Refresh() を呼ぶ（= UnloadUnusedAssets が
    /// 暗黙的に走る）ため、①②の対策が特に効く。
    /// </summary>
    [InitializeOnLoad]
    internal static class DennokoUIFont
    {
        private const string FamilyName = "Meiryo";
        private const string StyleName = "Regular";

        // ドメインリロード後に前回生成したインスタンスを拾い直すための識別名。
        // 同じプロジェクト内の複数ツールで 1 つの FontAsset を共有するためでもあるので変更しない。
        private const string AssetName = "Dennoko_UIFont_Meiryo";

        // アトラス増加の検出間隔（秒）。短くしすぎない。
        private const double TickIntervalSec = 2.0;

        // ─── ウォームアップ文字 ─────────────────────────────────────────────
        // ここに列挙した文字は生成時にまとめてアトラスへ焼き込む。実行中のアトラス追加
        // （＝保護漏れの隙）を減らすため、UXML / Label に出る日本語をここへ書き足すこと。
        private const string WarmupAscii =
            " !\"#$%&'()*+,-./0123456789:;<=>?@" +
            "ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`" +
            "abcdefghijklmnopqrstuvwxyz{|}~";

        private const string WarmupJapanese =
            "適用保存解除追加削除設定選択中対象有効無効表示非切替更新確認取消閉開始了" +
            "はいいえ完了失敗警告情報エラー成功準備実行処理読込書出";

        // PSD Simple Editor 固有の UI 文言（translation_ja.json + コード内リテラルから
        // 抽出した実出現文字）。文言を追加したら再抽出して更新する。
        private const string WarmupJapaneseTool =
            "×→↻▸▾、。「」〜あうかがきぎくけげこごさしすせそただつづてでとどなにねのぶへ" +
            "べほまみむめもやよらりるれろわをんァアィイウェォオカガキクグケコサシジスズセゼソ" +
            "ゾタダチッツテデトドニネハバパビピフブプベペポマミムメモャヤュョリルレロン・一上" +
            "下不乗仕付以件似余作使倍値側像元先入全内円再分初判別制前力動包化半参反可右号合名" +
            "含周味回囲在均域基塗変外大容小少展履左差常幅広度式強形彩得復微応性意押抽持指接換" +
            "損数整旧明時暗最期未析果棄構様横機欄正歴比注深済減焼照版特状率現生画番白的目直相" +
            "着知破窓端等算範素細終結絶続維緑線編縦能自致色補複覆見規視許詳調赤超転較輝辺近透" +
            "通造過部重間閾限階際集青面項領鮮黄黒（）";

        private static FontAsset _font;
        private static bool _unavailable;     // 未搭載環境。CreateFontAsset を再試行しない
        private static readonly List<VisualElement> _roots = new List<VisualElement>();
        private static bool _tickHooked;
        private static double _nextTick;

        static DennokoUIFont()
        {
            // UnloadUnusedAssets を暗黙的に挟みうるタイミングで再点検する
            AssemblyReloadEvents.afterAssemblyReload += Revalidate;
            EditorApplication.playModeStateChanged += _ => Revalidate();
            EditorApplication.projectChanged += Revalidate;   // AssetDatabase.Refresh 後
        }

        // ─── 公開 API ───────────────────────────────────────────────────────

        /// <summary>
        /// ルート要素に標準フォントを適用し、以後の自己修復対象として登録する。
        /// EditorWindow の CreateGUI() / Inspector の CreateInspectorGUI() から 1 回呼ぶ。
        /// </summary>
        public static void Apply(VisualElement root)
        {
            if (root == null) return;

            if (!_roots.Contains(root))
            {
                _roots.Add(root);
                // 再ドック・レイアウト変更・リロード後の再アタッチでも確実に貼り直す。
                // ⚠ 再アタッチ時は Track で監視リストへ戻すこと。Detach で外したままだと
                //   以後アトラスが破棄されても自己修復されないウィンドウが残る。
                root.RegisterCallback<AttachToPanelEvent>(_ => { Track(root); ApplyTo(root); });
                root.RegisterCallback<DetachFromPanelEvent>(_ => _roots.Remove(root));
            }

            HookTick(true);
            ApplyTo(root);
        }

        /// <summary>自己修復の監視対象に入れる（冪等）。</summary>
        private static void Track(VisualElement root)
        {
            if (!_roots.Contains(root)) _roots.Add(root);
            HookTick(true);
        }

        // ─── 適用 ───────────────────────────────────────────────────────────

        private static void ApplyTo(VisualElement root)
        {
            var font = Get();

            // 取得できないときは破棄済みへのダングリング参照を残さず、
            // インラインスタイルを消してエディタ標準フォントへ確実に戻す。
            root.style.unityFontDefinition = font != null
                ? new StyleFontDefinition(FontDefinition.FromSDFFont(font))
                : new StyleFontDefinition(StyleKeyword.Null);
        }

        // ─── 取得・生成 ─────────────────────────────────────────────────────

        private static FontAsset Get()
        {
            // ⚠ 「一度探したら二度と作り直さない」ラッチにしないこと。
            //    破棄済みキャッシュを返し続けると UI のテキストが崩れたまま復帰できなくなる。
            if (IsAlive(_font))
            {
                Protect(_font);   // 実行中に増えたアトラスをここで拾う
                return _font;
            }

            if (_unavailable) return null;

            _font = FindExisting() ?? Create();
            if (_font == null)
            {
                _unavailable = true;   // フォント未搭載環境。毎フレーム再試行しない
                return null;
            }
            return _font;
        }

        /// <summary>ドメインリロードを生き延びた前回生成分を拾う（leak 防止）。</summary>
        private static FontAsset FindExisting()
        {
            foreach (var fa in Resources.FindObjectsOfTypeAll<FontAsset>())
            {
                if (fa == null || fa.name != AssetName || !IsAlive(fa)) continue;
                Protect(fa);
                return fa;
            }
            return null;
        }

        private static FontAsset Create()
        {
            try
            {
                // ⚠ レガシー Font (Font.CreateDynamicFontFromOSFont) + FontDefinition.FromFont() は
                //   TextCore がグリフ生成に失敗して文字が一切表示されなくなる。必ずこの API を使う。
                var fa = FontAsset.CreateFontAsset(FamilyName, StyleName);
                if (fa == null) return null;   // 未搭載環境。エディタ標準フォントへフォールバック

                fa.name = AssetName;
                Protect(fa);
                PreWarm(fa);
                Protect(fa);   // PreWarm で増えたアトラスを守る
                return fa;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>UI に出る文字を先に焼き、操作中のアトラス追加を減らす。</summary>
        private static void PreWarm(FontAsset fa)
        {
            // TryAddCharacters は Unity バージョンにより差異があるため必ず try/catch で囲む
            try { fa.TryAddCharacters(WarmupAscii + WarmupJapanese + WarmupJapaneseTool, out _); }
            catch { /* 焼けなくても動作はする（実行中に動的追加される） */ }
        }

        // ─── 保護・検証 ─────────────────────────────────────────────────────

        /// <summary>
        /// FontAsset 本体・アトラス material・全 atlasTextures に HideAndDontSave を伝播させる。
        /// 冪等なので何度呼んでもよい。実行中に追加されたアトラスを拾うため定期的に呼ぶこと。
        /// </summary>
        private static void Protect(FontAsset fa)
        {
            if (fa == null) return;

            fa.hideFlags = HideFlags.HideAndDontSave;

            if (fa.material != null)
                fa.material.hideFlags = HideFlags.HideAndDontSave;

            var atlasTextures = fa.atlasTextures;
            if (atlasTextures == null) return;
            foreach (var tex in atlasTextures)
            {
                if (tex != null)
                    tex.hideFlags = HideFlags.HideAndDontSave;
            }
        }

        /// <summary>本体だけでなくアトラスまで生きているかを判定する（fake-null 対応）。</summary>
        private static bool IsAlive(FontAsset fa)
        {
            if (fa == null) return false;              // Unity の == が破棄済みも false にする
            if (fa.material == null) return false;
            var atlasTextures = fa.atlasTextures;
            return atlasTextures != null && atlasTextures.Length > 0 && atlasTextures[0] != null;
        }

        /// <summary>破棄検知 → 作り直し → 開いている全ルートへ再適用。</summary>
        private static void Revalidate()
        {
            if (IsAlive(_font))
            {
                Protect(_font);   // 増えたアトラスを守るだけでよい
                return;
            }

            _font = null;         // 次の Get() で再生成させる
            for (int i = _roots.Count - 1; i >= 0; i--)
            {
                var root = _roots[i];
                if (root == null || root.panel == null) { _roots.RemoveAt(i); continue; }
                ApplyTo(root);
            }
        }

        // ─── 監視ティック ───────────────────────────────────────────────────
        // 動的に生成される文字列（レイヤー名・エラーメッセージ等）でもアトラスは増える。
        // ウィンドウが開いている間だけ低頻度で Protect を回して保護漏れを塞ぐ。

        private static void HookTick(bool on)
        {
            if (on == _tickHooked) return;
            if (on) EditorApplication.update += Tick;
            else EditorApplication.update -= Tick;
            _tickHooked = on;
        }

        private static void Tick()
        {
            for (int i = _roots.Count - 1; i >= 0; i--)
            {
                var root = _roots[i];
                if (root == null || root.panel == null) _roots.RemoveAt(i);
            }
            if (_roots.Count == 0) { HookTick(false); return; }

            if (EditorApplication.timeSinceStartup < _nextTick) return;
            _nextTick = EditorApplication.timeSinceStartup + TickIntervalSec;

            Revalidate();
        }
    }
}
