using UnityEngine;
using UnityEditor;

namespace PSDSimpleEditor
{
    // ── EditorStyles の一時上書き (Light Mode Fix) ──────────────────────────
    // ─── partial 見取り図 ───────────────────────────────────────────
    // 責務   : Unity 標準の EditorStyles / GUI.skin をテーマ色に一時上書きし、
    //          OnGUI 終了時に元へ復元するスコープ機構 (PushEditorTheme / PopEditorTheme)
    // 宣言   : _overrideActive, _backups, GUIStyleBackup, カーソル/選択色バックアップ
    // 依存   : FixAllTextColors / Hex / TextPrimary / _texSearchField (PSDEditorTheme.cs)
    // ────────────────────────────────────────────────────────────────
    internal static partial class PSDEditorTheme
    {
        private static bool _overrideActive;
        public static bool IsOverrideActive => _overrideActive;

        private static Color _backupCursorColor;
        private static Color _backupSelectionColor;
        private static bool _settingsBackupActive;

        private class GUIStyleBackup
        {
            private readonly GUIStyle _style;
            private readonly Color _normalColor, _hoverColor, _activeColor, _focusedColor;
            private readonly Color _onNormalColor, _onHoverColor, _onActiveColor, _onFocusedColor;
            private readonly Texture2D _normalBg, _hoverBg, _activeBg, _focusedBg;
            private readonly Texture2D _onNormalBg, _onHoverBg, _onActiveBg, _onFocusedBg;
            private readonly RectOffset _border;
            private readonly RectOffset _padding;

            public GUIStyleBackup(GUIStyle style)
            {
                _style = style;
                _normalColor = style.normal.textColor;
                _hoverColor = style.hover.textColor;
                _activeColor = style.active.textColor;
                _focusedColor = style.focused.textColor;
                _onNormalColor = style.onNormal.textColor;
                _onHoverColor = style.onHover.textColor;
                _onActiveColor = style.onActive.textColor;
                _onFocusedColor = style.onFocused.textColor;

                _normalBg = style.normal.background;
                _hoverBg = style.hover.background;
                _activeBg = style.active.background;
                _focusedBg = style.focused.background;
                _onNormalBg = style.onNormal.background;
                _onHoverBg = style.onHover.background;
                _onActiveBg = style.onActive.background;
                _onFocusedBg = style.onFocused.background;

                _border = style.border;
                _padding = style.padding;
            }

            public void Restore()
            {
                _style.normal.textColor = _normalColor;
                _style.hover.textColor = _hoverColor;
                _style.active.textColor = _activeColor;
                _style.focused.textColor = _focusedColor;
                _style.onNormal.textColor = _onNormalColor;
                _style.onHover.textColor = _onHoverColor;
                _style.onActive.textColor = _onActiveColor;
                _style.onFocused.textColor = _onFocusedColor;

                _style.normal.background = _normalBg;
                _style.hover.background = _hoverBg;
                _style.active.background = _activeBg;
                _style.focused.background = _focusedBg;
                _style.onNormal.background = _onNormalBg;
                _style.onHover.background = _onHoverBg;
                _style.onActive.background = _onActiveBg;
                _style.onFocused.background = _onFocusedBg;

                _style.border = _border;
                _style.padding = _padding;
            }
        }

        private static GUIStyleBackup[] _backups;

        /// <summary>
        /// OnGUI 先頭で Initialize() の直後に呼ぶ。
        /// ライト/ダーク両モードで EditorStyles をテーマ定義色に一時上書きする。
        /// PopEditorTheme を finally ブロックで必ず呼ぶこと。
        /// </summary>
        public static void PushEditorTheme()
        {
            _overrideActive = true;

            if (_backups == null)
            {
                _backups = new[]
                {
                    new GUIStyleBackup(EditorStyles.label),
                    new GUIStyleBackup(EditorStyles.objectField),
                    new GUIStyleBackup(EditorStyles.numberField),
                    new GUIStyleBackup(EditorStyles.textField),
                    new GUIStyleBackup(EditorStyles.popup),
                    new GUIStyleBackup(EditorStyles.toggle),
                    new GUIStyleBackup(EditorStyles.foldout),
                    new GUIStyleBackup(GUI.skin.textField),
                    new GUIStyleBackup(GUI.skin.label)
                };
            }

            if (!_settingsBackupActive)
            {
                _backupCursorColor = GUI.skin.settings.cursorColor;
                _backupSelectionColor = GUI.skin.settings.selectionColor;
                _settingsBackupActive = true;
            }

            // ─ テキスト色を固定 (無効化されていないパラメーター/表記は完全な白)
            FixAllTextColors(EditorStyles.label, TextPrimary);
            FixAllTextColors(EditorStyles.objectField, TextPrimary);
            FixAllTextColors(EditorStyles.numberField, TextPrimary);
            FixAllTextColors(EditorStyles.textField,   TextPrimary);
            FixAllTextColors(EditorStyles.popup,       EditorGUIUtility.isProSkin ? TextPrimary : Hex(0x111111));
            FixAllTextColors(EditorStyles.toggle,      TextPrimary);
            FixAllTextColors(EditorStyles.foldout,     TextPrimary);
            FixAllTextColors(GUI.skin.textField,       TextPrimary);
            FixAllTextColors(GUI.skin.label,           TextPrimary);

            // ── 入力フィールドの背景を固定 (ライトモードの白い背景を防止)
            FixAllStateBackgrounds(EditorStyles.numberField, _texSearchField);
            FixAllStateBackgrounds(EditorStyles.textField, _texSearchField);
            FixAllStateBackgrounds(GUI.skin.textField, _texSearchField);

            // ── カーソルと選択範囲の色を固定 (ライトモードの黒カーソル等を防止)
            GUI.skin.settings.cursorColor = TextPrimary;
            GUI.skin.settings.selectionColor = new Color(1f, 1f, 1f, 0.25f);
        }

        /// <summary>OnGUI 末尾の finally ブロックで必ず呼ぶ。EditorStyles を元に戻す。</summary>
        public static void PopEditorTheme()
        {
            if (!_overrideActive) return;
            _overrideActive = false;

            if (_backups != null)
            {
                foreach (var backup in _backups)
                {
                    backup.Restore();
                }
            }

            if (_settingsBackupActive)
            {
                GUI.skin.settings.cursorColor = _backupCursorColor;
                GUI.skin.settings.selectionColor = _backupSelectionColor;
                _settingsBackupActive = false;
            }
        }

        private static void FixAllStateBackgrounds(GUIStyle style, Texture2D tex)
        {
            style.normal.background    = tex;
            style.hover.background     = tex;
            style.active.background    = tex;
            style.focused.background   = tex;
            style.onNormal.background  = tex;
            style.onHover.background   = tex;
            style.onActive.background  = tex;
            style.onFocused.background = tex;
        }
    }
}
