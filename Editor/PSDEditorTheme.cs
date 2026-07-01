using UnityEngine;
using UnityEditor;

namespace PSDSimpleEditor
{
    /// <summary>
    /// dennokoworks フローティングデザインスキーマに基づくテーマ定義。
    /// (.claude/skills/dennokoworks_color_schema の UniTexTheme テンプレートを PSD Simple Editor 用に移植。)
    /// OnGUI の先頭で Initialize() → PushEditorTheme() を呼び、finally で PopEditorTheme() を呼ぶ。
    ///
    /// テンプレート標準スタイルに加え、レイヤーツリーの入れ子表現用スタイル
    /// (LayerLeafCardStyle / LayerGroupOuterStyle / LayerGroupHeaderStyle / LayerGroupBodyStyle …) を持つ。
    /// </summary>
    internal static class PSDEditorTheme
    {
        // ─── Colors ──────────────────────────────────────────────────────────

        // theme.surface (Neutral Layer)
        public static readonly Color Surface0 = Hex(0x121212); // app background
        public static readonly Color Surface1 = Hex(0x1e1e1e); // cards, inputs
        public static readonly Color Surface2 = Hex(0x2c2c2c); // hover, toolbar

        // theme.outline
        public static readonly Color Outline = Hex(0x3a3a3a);

        // theme.typography
        public static readonly Color TextPrimary   = Hex(0xffffff);
        public static readonly Color TextSecondary = Hex(0xcccccc);
        public static readonly Color TextTertiary  = Hex(0xaaaaaa);
        public static readonly Color TextDisabled  = Hex(0x555555);

        // theme.semantic
        public static readonly Color SemanticError   = Hex(0x9b1b30);
        public static readonly Color SemanticWarning = Hex(0xffb74d);
        public static readonly Color SemanticSuccess = Hex(0x4caf50);
        public static readonly Color SemanticInfo    = Hex(0x64b5f6);

        // theme.interaction
        public static readonly Color Accent       = Color.white;
        public static readonly Color HoverOverlay = new Color(1f, 1f, 1f, 0.05f);

        // ─── Cached Textures ─────────────────────────────────────────────────

        private static Texture2D _texSurface0;
        private static Texture2D _texSurface1;
        private static Texture2D _texSurface2;
        private static Texture2D _texCard;        // Surface1 fill + Outline border (3x3)
        private static Texture2D _texAccentCard;  // Surface2 fill + Outline border (3x3)
        private static Texture2D _texGroupHeader; // Surface2 fill + Outline border (3x3)
        private static Texture2D _texSearchField; // Input fields background (3x3 bordered)

        // ─── Styles ──────────────────────────────────────────────────────────

        private static bool _initialized;
        private static bool _lastIsProSkin;

        // Layout / Container
        public static GUIStyle CardStyle      { get; private set; } // sections (padding あり)
        public static GUIStyle CardOuterStyle { get; private set; } // ツールバー付き外枠 (padding なし)
        public static GUIStyle PanelStyle     { get; private set; } // 左右パネル外枠 (padding/margin なし)
        public static GUIStyle ToolbarStyle   { get; private set; } // ツールバー行

        // Typography
        public static GUIStyle TitleStyle            { get; private set; }
        public static GUIStyle SectionHeaderStyle    { get; private set; }
        public static GUIStyle SecondaryTextStyle    { get; private set; }
        public static GUIStyle CaptionStyle          { get; private set; }
        public static GUIStyle CenteredCaptionStyle  { get; private set; }
        public static GUIStyle ControlLabelStyle     { get; private set; } // スライダー行の小ラベル
        public static GUIStyle LayerNameStyle        { get; private set; } // レイヤー名

        // Buttons
        public static GUIStyle ActionButtonStyle     { get; private set; } // Primary Action
        public static GUIStyle SecondaryButtonStyle  { get; private set; } // Secondary Action
        public static GUIStyle ToolButtonStyle       { get; private set; } // ツールバー内のコンパクトボタン
        public static GUIStyle MiniButtonStyle       { get; private set; }
        public static GUIStyle ToolbarButtonStyle    { get; private set; }
        public static GUIStyle FoldoutButtonStyle    { get; private set; } // ▸ / ▾ 展開ボタン
        public static GUIStyle FoldoutLabelStyle     { get; private set; } // 展開ボタン横のクリック可能ラベル

        // Layer tree (skill 非定義・独自)
        public static GUIStyle LayerLeafCardStyle    { get; private set; } // 通常レイヤーのカード
        public static GUIStyle LayerGroupOuterStyle  { get; private set; } // グループの外枠 (padding 0)
        public static GUIStyle LayerGroupHeaderStyle { get; private set; } // グループのタイトル帯 (Surface2)
        public static GUIStyle LayerGroupBodyStyle   { get; private set; } // グループ内コンテンツ (入れ子インセット)

        // Status bar
        public static GUIStyle StatusInfoStyle    { get; private set; }
        public static GUIStyle StatusSuccessStyle { get; private set; }
        public static GUIStyle StatusErrorStyle   { get; private set; }

        // ─────────────────────────────────────────────────────────────────────

        /// <summary>OnGUI の先頭で呼び出す。初回のみスタイルを構築する。</summary>
        public static void Initialize()
        {
            bool currentProSkin = EditorGUIUtility.isProSkin;
            if (_initialized && _lastIsProSkin != currentProSkin)
            {
                DisposeTextures();
            }
            _lastIsProSkin = currentProSkin;

            if (_initialized) return;
            _initialized = true;
            EnsureTextures();
            BuildStyles();
        }

        private static void EnsureTextures()
        {
            if (!_texSurface0)    _texSurface0    = MakeTex(Surface0);
            if (!_texSurface1)    _texSurface1    = MakeTex(Surface1);
            if (!_texSurface2)    _texSurface2    = MakeTex(Surface2);
            if (!_texCard)        _texCard        = MakeBorderedTex(Surface1, Outline);
            if (!_texAccentCard)  _texAccentCard  = MakeBorderedTex(Surface2, Outline);
            if (!_texGroupHeader) _texGroupHeader = MakeBorderedTex(Surface2, Outline);
            if (!_texSearchField) _texSearchField = MakeBorderedTex(Surface2, Hex(0x5a5a5a));
        }

        private static void BuildStyles()
        {
            // ── Container ────────────────────────────────────────────────────

            CardStyle = new GUIStyle();
            CardStyle.normal.background = _texCard;
            CardStyle.border  = new RectOffset(1, 1, 1, 1);
            CardStyle.padding = new RectOffset(12, 12, 10, 10);
            CardStyle.margin  = new RectOffset(8, 8, 6, 6);

            CardOuterStyle = new GUIStyle();
            CardOuterStyle.normal.background = _texCard;
            CardOuterStyle.border  = new RectOffset(1, 1, 1, 1);
            CardOuterStyle.padding = new RectOffset(0, 0, 0, 0);
            CardOuterStyle.margin  = new RectOffset(8, 8, 6, 6);

            PanelStyle = new GUIStyle();
            PanelStyle.normal.background = _texCard;
            PanelStyle.border  = new RectOffset(1, 1, 1, 1);
            PanelStyle.padding = new RectOffset(0, 0, 0, 0);
            PanelStyle.margin  = new RectOffset(0, 0, 0, 0);

            ToolbarStyle = new GUIStyle();
            ToolbarStyle.normal.background = _texSurface2;
            ToolbarStyle.padding = new RectOffset(8, 8, 5, 5);
            ToolbarStyle.margin  = new RectOffset(0, 0, 0, 0);

            // ── Typography ───────────────────────────────────────────────────

            TitleStyle = new GUIStyle();
            TitleStyle.fontStyle = FontStyle.Bold;
            TitleStyle.fontSize  = 14;
            TitleStyle.alignment = TextAnchor.MiddleLeft;
            FixAllTextColors(TitleStyle, TextPrimary);

            SectionHeaderStyle = new GUIStyle();
            SectionHeaderStyle.fontStyle = FontStyle.Bold;
            SectionHeaderStyle.fontSize  = 10;
            SectionHeaderStyle.margin    = new RectOffset(0, 0, 0, 2);
            SectionHeaderStyle.alignment = TextAnchor.MiddleLeft;
            FixAllTextColors(SectionHeaderStyle, TextTertiary);

            SecondaryTextStyle = new GUIStyle();
            SecondaryTextStyle.wordWrap = true;
            SecondaryTextStyle.fontSize = 11;
            FixAllTextColors(SecondaryTextStyle, TextSecondary);

            CaptionStyle = new GUIStyle();
            CaptionStyle.fontSize = 9;
            CaptionStyle.alignment = TextAnchor.MiddleLeft;
            FixAllTextColors(CaptionStyle, TextPrimary);

            CenteredCaptionStyle = new GUIStyle();
            CenteredCaptionStyle.fontSize  = 10;
            CenteredCaptionStyle.alignment = TextAnchor.MiddleCenter;
            CenteredCaptionStyle.wordWrap  = true;
            FixAllTextColors(CenteredCaptionStyle, TextTertiary);

            ControlLabelStyle = new GUIStyle();
            ControlLabelStyle.fontSize  = 10;
            ControlLabelStyle.alignment = TextAnchor.MiddleLeft;
            FixAllTextColors(ControlLabelStyle, TextPrimary);

            LayerNameStyle = new GUIStyle();
            LayerNameStyle.fontSize  = 11;
            LayerNameStyle.alignment = TextAnchor.MiddleLeft;
            LayerNameStyle.clipping  = TextClipping.Clip;
            FixAllTextColors(LayerNameStyle, TextPrimary);

            // ── Toolbar Button ────────────────────────────────────────────────

            ToolbarButtonStyle = new GUIStyle();
            ToolbarButtonStyle.normal.background   = null;
            ToolbarButtonStyle.hover.background    = MakeTex(Color.Lerp(Surface2, Color.white, 0.10f));
            ToolbarButtonStyle.active.background   = MakeTex(Color.Lerp(Surface2, Color.white, 0.18f));
            ToolbarButtonStyle.border    = new RectOffset(0, 0, 0, 0);
            ToolbarButtonStyle.margin    = new RectOffset(1, 1, 1, 1);
            ToolbarButtonStyle.padding   = new RectOffset(8, 8, 3, 3);
            ToolbarButtonStyle.fontSize  = 10;
            ToolbarButtonStyle.alignment = TextAnchor.MiddleCenter;
            ToolbarButtonStyle.normal.textColor    = TextTertiary;
            ToolbarButtonStyle.hover.textColor     = TextSecondary;
            ToolbarButtonStyle.active.textColor    = TextPrimary;
            ToolbarButtonStyle.focused.textColor   = TextTertiary;
            ToolbarButtonStyle.onNormal.textColor  = TextPrimary;
            ToolbarButtonStyle.onHover.textColor   = TextPrimary;
            ToolbarButtonStyle.onActive.textColor  = TextPrimary;
            ToolbarButtonStyle.onFocused.textColor = TextPrimary;

            // ── Buttons ──────────────────────────────────────────────────────

            ActionButtonStyle = new GUIStyle();
            ActionButtonStyle.normal.background  = _texAccentCard;
            ActionButtonStyle.hover.background   = MakeTex(Color.Lerp(Surface2, Color.white, 0.07f));
            ActionButtonStyle.active.background  = MakeTex(Color.Lerp(Surface2, Color.white, 0.15f));
            ActionButtonStyle.border       = new RectOffset(1, 1, 1, 1);
            ActionButtonStyle.margin       = new RectOffset(4, 4, 2, 2);
            ActionButtonStyle.padding      = new RectOffset(10, 10, 4, 4);
            ActionButtonStyle.fontSize     = 12;
            ActionButtonStyle.fontStyle    = FontStyle.Bold;
            ActionButtonStyle.fixedHeight  = 28;
            ActionButtonStyle.alignment    = TextAnchor.MiddleCenter;
            FixAllTextColors(ActionButtonStyle, TextPrimary);

            SecondaryButtonStyle = new GUIStyle();
            SecondaryButtonStyle.normal.background = _texCard;
            SecondaryButtonStyle.hover.background  = _texAccentCard;
            SecondaryButtonStyle.active.background = MakeTex(Color.Lerp(Surface1, Color.white, 0.10f));
            SecondaryButtonStyle.border       = new RectOffset(1, 1, 1, 1);
            SecondaryButtonStyle.margin       = new RectOffset(4, 4, 2, 2);
            SecondaryButtonStyle.padding      = new RectOffset(10, 10, 4, 4);
            SecondaryButtonStyle.fontSize     = 11;
            SecondaryButtonStyle.fixedHeight  = 26;
            SecondaryButtonStyle.alignment    = TextAnchor.MiddleCenter;
            SecondaryButtonStyle.normal.textColor    = TextSecondary;
            SecondaryButtonStyle.hover.textColor     = TextPrimary;
            SecondaryButtonStyle.active.textColor    = TextPrimary;
            SecondaryButtonStyle.focused.textColor   = TextSecondary;
            SecondaryButtonStyle.onNormal.textColor  = TextSecondary;
            SecondaryButtonStyle.onHover.textColor   = TextPrimary;
            SecondaryButtonStyle.onActive.textColor  = TextPrimary;
            SecondaryButtonStyle.onFocused.textColor = TextSecondary;

            ToolButtonStyle = new GUIStyle();
            ToolButtonStyle.normal.background = _texAccentCard;
            ToolButtonStyle.hover.background  = MakeTex(Color.Lerp(Surface2, Color.white, 0.10f));
            ToolButtonStyle.active.background = MakeTex(Color.Lerp(Surface2, Color.white, 0.18f));
            ToolButtonStyle.border      = new RectOffset(1, 1, 1, 1);
            ToolButtonStyle.margin      = new RectOffset(2, 2, 1, 1);
            ToolButtonStyle.padding     = new RectOffset(8, 8, 2, 2);
            ToolButtonStyle.fontSize    = 11;
            ToolButtonStyle.fixedHeight = 20;
            ToolButtonStyle.alignment   = TextAnchor.MiddleCenter;
            ToolButtonStyle.onNormal.background  = MakeTex(Color.Lerp(Surface2, Accent, 0.25f));
            ToolButtonStyle.onHover.background   = MakeTex(Color.Lerp(Surface2, Accent, 0.32f));
            ToolButtonStyle.onActive.background  = MakeTex(Color.Lerp(Surface2, Accent, 0.40f));
            ToolButtonStyle.normal.textColor    = TextSecondary;
            ToolButtonStyle.hover.textColor     = TextPrimary;
            ToolButtonStyle.active.textColor    = TextPrimary;
            ToolButtonStyle.focused.textColor   = TextSecondary;
            ToolButtonStyle.onNormal.textColor  = TextPrimary;
            ToolButtonStyle.onHover.textColor   = TextPrimary;
            ToolButtonStyle.onActive.textColor  = TextPrimary;
            ToolButtonStyle.onFocused.textColor = TextPrimary;

            MiniButtonStyle = new GUIStyle();
            MiniButtonStyle.normal.background = _texAccentCard;
            MiniButtonStyle.hover.background  = MakeTex(Color.Lerp(Surface2, Color.white, 0.10f));
            MiniButtonStyle.active.background = MakeTex(Color.Lerp(Surface2, Color.white, 0.18f));
            MiniButtonStyle.border      = new RectOffset(1, 1, 1, 1);
            MiniButtonStyle.margin      = new RectOffset(2, 2, 1, 1);
            MiniButtonStyle.padding     = new RectOffset(6, 6, 1, 2);
            MiniButtonStyle.fontSize    = 10;
            MiniButtonStyle.fixedHeight = 18;
            MiniButtonStyle.alignment   = TextAnchor.MiddleCenter;
            MiniButtonStyle.onNormal.background  = MakeTex(Color.Lerp(Surface2, Accent, 0.25f));
            MiniButtonStyle.onHover.background   = MakeTex(Color.Lerp(Surface2, Accent, 0.32f));
            MiniButtonStyle.onActive.background  = MakeTex(Color.Lerp(Surface2, Accent, 0.40f));
            MiniButtonStyle.normal.textColor    = TextTertiary;
            MiniButtonStyle.hover.textColor     = TextSecondary;
            MiniButtonStyle.active.textColor    = TextPrimary;
            MiniButtonStyle.focused.textColor   = TextTertiary;
            MiniButtonStyle.onNormal.textColor  = TextPrimary;
            MiniButtonStyle.onHover.textColor   = TextPrimary;
            MiniButtonStyle.onActive.textColor  = TextPrimary;
            MiniButtonStyle.onFocused.textColor = TextPrimary;

            FoldoutButtonStyle = new GUIStyle();
            FoldoutButtonStyle.normal.background = null;
            FoldoutButtonStyle.hover.background  = null;
            FoldoutButtonStyle.active.background = null;
            FoldoutButtonStyle.border    = new RectOffset(0, 0, 0, 0);
            FoldoutButtonStyle.margin    = new RectOffset(0, 0, 0, 0);
            FoldoutButtonStyle.padding   = new RectOffset(0, 0, 0, 0);
            FoldoutButtonStyle.fontSize  = 14;   // ▸/▾ を視認しやすいサイズに拡大
            FoldoutButtonStyle.alignment = TextAnchor.MiddleCenter;
            FoldoutButtonStyle.normal.textColor  = TextPrimary;
            FoldoutButtonStyle.hover.textColor   = TextPrimary;
            FoldoutButtonStyle.active.textColor  = TextPrimary;
            FoldoutButtonStyle.focused.textColor = TextPrimary;

            // 展開ボタン横のクリック可能ラベル (テーマ管理で light/dark 両対応)
            FoldoutLabelStyle = new GUIStyle();
            FoldoutLabelStyle.normal.background = null;
            FoldoutLabelStyle.hover.background  = null;
            FoldoutLabelStyle.active.background = null;
            FoldoutLabelStyle.border    = new RectOffset(0, 0, 0, 0);
            FoldoutLabelStyle.margin    = new RectOffset(0, 0, 0, 0);
            FoldoutLabelStyle.padding   = new RectOffset(2, 2, 0, 0);
            FoldoutLabelStyle.fontSize  = 11;
            FoldoutLabelStyle.alignment = TextAnchor.MiddleLeft;
            FoldoutLabelStyle.normal.textColor    = TextPrimary;
            FoldoutLabelStyle.hover.textColor     = TextPrimary;
            FoldoutLabelStyle.active.textColor    = TextPrimary;
            FoldoutLabelStyle.focused.textColor   = TextPrimary;
            FoldoutLabelStyle.onNormal.textColor  = TextPrimary;
            FoldoutLabelStyle.onHover.textColor   = TextPrimary;
            FoldoutLabelStyle.onActive.textColor  = TextPrimary;
            FoldoutLabelStyle.onFocused.textColor = TextPrimary;

            // ── Layer tree (独自: 入れ子・グループ表現) ───────────────────────

            LayerLeafCardStyle = new GUIStyle();
            LayerLeafCardStyle.normal.background = _texCard;
            LayerLeafCardStyle.border  = new RectOffset(1, 1, 1, 1);
            LayerLeafCardStyle.padding = new RectOffset(8, 8, 6, 6);
            LayerLeafCardStyle.margin  = new RectOffset(0, 0, 3, 3);

            LayerGroupOuterStyle = new GUIStyle();
            LayerGroupOuterStyle.normal.background = _texCard;
            LayerGroupOuterStyle.border  = new RectOffset(1, 1, 1, 1);
            LayerGroupOuterStyle.padding = new RectOffset(0, 0, 0, 0);
            LayerGroupOuterStyle.margin  = new RectOffset(0, 0, 4, 4);

            LayerGroupHeaderStyle = new GUIStyle();
            LayerGroupHeaderStyle.normal.background = _texGroupHeader;
            LayerGroupHeaderStyle.border  = new RectOffset(1, 1, 1, 1);
            LayerGroupHeaderStyle.padding = new RectOffset(8, 8, 5, 5);
            LayerGroupHeaderStyle.margin  = new RectOffset(0, 0, 0, 0);

            LayerGroupBodyStyle = new GUIStyle();
            LayerGroupBodyStyle.normal.background = null;
            LayerGroupBodyStyle.padding = new RectOffset(10, 8, 8, 8);
            LayerGroupBodyStyle.margin  = new RectOffset(0, 0, 0, 0);

            // ── Status Bar ───────────────────────────────────────────────────

            var statusBase = new GUIStyle();
            statusBase.border    = new RectOffset(1, 1, 1, 1);
            statusBase.padding   = new RectOffset(10, 10, 6, 6);
            statusBase.margin    = new RectOffset(8, 8, 4, 6);
            statusBase.fontSize  = 11;
            statusBase.wordWrap  = true;
            statusBase.alignment = TextAnchor.MiddleLeft;

            StatusInfoStyle = new GUIStyle(statusBase);
            StatusInfoStyle.normal.background = _texCard;
            FixAllTextColors(StatusInfoStyle, TextSecondary);

            StatusSuccessStyle = new GUIStyle(statusBase);
            StatusSuccessStyle.normal.background = MakeTex(Color.Lerp(Surface1, SemanticSuccess, 0.3f));
            FixAllTextColors(StatusSuccessStyle, SemanticSuccess);

            StatusErrorStyle = new GUIStyle(statusBase);
            StatusErrorStyle.normal.background = MakeTex(Color.Lerp(Surface1, SemanticError, 0.5f));
            FixAllTextColors(StatusErrorStyle, new Color(1f, 0.65f, 0.65f));
        }

        // ─── Editor Style Override (Light Mode Fix) ──────────────────────────

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
            FixAllTextColors(EditorStyles.popup,       TextPrimary);
            FixAllTextColors(EditorStyles.toggle,      TextPrimary);
            FixAllTextColors(EditorStyles.foldout,     TextPrimary);
            FixAllTextColors(GUI.skin.textField,       TextPrimary);
            FixAllTextColors(GUI.skin.label,           TextPrimary);

            // ─ 背景テクスチャをすべての状態でダーク色＋ボーダーに固定
            FixAllStateBackgrounds(EditorStyles.objectField, _texSearchField);
            EditorStyles.objectField.border = new RectOffset(1, 1, 1, 1);

            FixAllStateBackgrounds(EditorStyles.numberField, _texSearchField);
            EditorStyles.numberField.border = new RectOffset(1, 1, 1, 1);

            FixAllStateBackgrounds(EditorStyles.textField,   _texSearchField);
            EditorStyles.textField.border = new RectOffset(1, 1, 1, 1);
            EditorStyles.textField.padding = new RectOffset(6, 6, 3, 3);

            FixAllStateBackgrounds(GUI.skin.textField,       _texSearchField);
            GUI.skin.textField.border = new RectOffset(1, 1, 1, 1);
            GUI.skin.textField.padding = new RectOffset(6, 6, 3, 3);

            // ── カーソルと選択範囲の色を固定 (ライトモードの黒カーソル等を防止)
            GUI.skin.settings.cursorColor = TextPrimary;
            GUI.skin.settings.selectionColor = new Color(1f, 1f, 1f, 0.25f);

            // ポップアップは枠線付きカードテクスチャを使用し、9スライス境界を1pxに設定して引き伸ばし縞ノイズを解消
            FixAllStateBackgrounds(EditorStyles.popup, _texCard);
            EditorStyles.popup.border = new RectOffset(1, 1, 1, 1);
            EditorStyles.popup.padding = new RectOffset(6, 18, 4, 4);
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

        /// <summary>テクスチャと状態を明示破棄する（テーマ切り替えやドメインリロード時に安全にクリーンアップするため）。</summary>
        internal static void DisposeTextures()
        {
            PopEditorTheme();

            if (_texSurface0)    Object.DestroyImmediate(_texSurface0);
            if (_texSurface1)    Object.DestroyImmediate(_texSurface1);
            if (_texSurface2)    Object.DestroyImmediate(_texSurface2);
            if (_texCard)        Object.DestroyImmediate(_texCard);
            if (_texAccentCard)  Object.DestroyImmediate(_texAccentCard);
            if (_texGroupHeader) Object.DestroyImmediate(_texGroupHeader);
            if (_texSearchField) Object.DestroyImmediate(_texSearchField);

            _texSurface0    = null;
            _texSurface1    = null;
            _texSurface2    = null;
            _texCard        = null;
            _texAccentCard  = null;
            _texGroupHeader = null;
            _texSearchField = null;
            _initialized    = false;
            _backups        = null;
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

        // ─── Style Utilities ─────────────────────────────────────────────────

        private static void FixAllTextColors(GUIStyle style, Color color)
        {
            style.normal.textColor    = color;
            style.hover.textColor     = color;
            style.active.textColor    = color;
            style.focused.textColor   = color;
            style.onNormal.textColor  = color;
            style.onHover.textColor   = color;
            style.onActive.textColor  = color;
            style.onFocused.textColor = color;
        }

        // ─── Texture Utilities ───────────────────────────────────────────────

        private static Texture2D MakeTex(Color color)
        {
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.SetPixel(0, 0, color);
            tex.Apply();
            tex.hideFlags = HideFlags.HideAndDontSave;
            return tex;
        }

        private static Texture2D MakeBorderedTex(Color fillColor, Color borderColor)
        {
            const int size = 3;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    tex.SetPixel(x, y,
                        (x == 0 || x == size - 1 || y == 0 || y == size - 1)
                            ? borderColor
                            : fillColor);
            tex.Apply();
            tex.filterMode = FilterMode.Point;
            tex.hideFlags  = HideFlags.HideAndDontSave;
            return tex;
        }

        private static Color Hex(int rgb) => new Color(
            ((rgb >> 16) & 0xFF) / 255f,
            ((rgb >>  8) & 0xFF) / 255f,
            ( rgb        & 0xFF) / 255f);
    }
}
