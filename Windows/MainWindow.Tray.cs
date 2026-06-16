using System;
using System.Windows;
using AudioQualityChecker.Services;

namespace AudioQualityChecker
{
    /// <summary>
    /// System-tray (minimize-to-tray) support for the main window: the tray icon, its dark-themed
    /// context menu, and restore-from-tray. The <see cref="MainWindow.OnClosing"/> override stays in
    /// MainWindow.xaml.cs but reads <c>_forceClose</c> declared here (partials share state).
    /// </summary>
    public partial class MainWindow
    {
        // System tray icon (minimize to tray)
        private System.Windows.Forms.NotifyIcon? _trayIcon;
        private bool _forceClose; // true when exiting via tray context menu

        // ── Dark-themed tray context menu color table ──
        private class DarkColorTable : System.Windows.Forms.ProfessionalColorTable
        {
            private readonly System.Drawing.Color _bg = System.Drawing.Color.FromArgb(30, 30, 46);      // PanelBg-ish
            private readonly System.Drawing.Color _hover = System.Drawing.Color.FromArgb(58, 58, 74);   // ButtonBg-ish
            private readonly System.Drawing.Color _accent = System.Drawing.Color.FromArgb(88, 101, 242); // AccentColor-ish
            private readonly System.Drawing.Color _text = System.Drawing.Color.FromArgb(224, 224, 224);  // TextPrimary-ish
            private readonly System.Drawing.Color _border = System.Drawing.Color.FromArgb(58, 58, 74);   // ButtonBorder-ish

            public override System.Drawing.Color MenuBorder => _border;
            public override System.Drawing.Color MenuItemBorder => System.Drawing.Color.Transparent;
            public override System.Drawing.Color MenuItemSelected => _hover;
            public override System.Drawing.Color MenuItemSelectedGradientBegin => _hover;
            public override System.Drawing.Color MenuItemSelectedGradientEnd => _hover;
            public override System.Drawing.Color MenuItemPressedGradientBegin => _accent;
            public override System.Drawing.Color MenuItemPressedGradientEnd => _accent;
            public override System.Drawing.Color ToolStripDropDownBackground => _bg;
            public override System.Drawing.Color ImageMarginGradientBegin => _bg;
            public override System.Drawing.Color ImageMarginGradientEnd => _bg;
            public override System.Drawing.Color ImageMarginGradientMiddle => _bg;
            public override System.Drawing.Color SeparatorDark => _border;
            public override System.Drawing.Color SeparatorLight => _border;
        }

        private void InitializeTrayIcon()
        {
            if (_trayIcon != null) return;

            _trayIcon = new System.Windows.Forms.NotifyIcon
            {
                Icon = new System.Drawing.Icon(
                    System.Windows.Application.GetResourceStream(
                        new Uri("pack://application:,,,/Resources/app.ico"))!.Stream),
                Text = "AudioAuditor",
                Visible = false
            };

            _trayIcon.DoubleClick += (_, _) => RestoreFromTray();

            var menu = new System.Windows.Forms.ContextMenuStrip
            {
                Renderer = new System.Windows.Forms.ToolStripProfessionalRenderer(new DarkColorTable()),
                BackColor = System.Drawing.Color.FromArgb(30, 30, 46),
                ForeColor = System.Drawing.Color.FromArgb(224, 224, 224),
                ShowImageMargin = false
            };
            menu.Items.Add("Show AudioAuditor", null, (_, _) => RestoreFromTray());
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add("Exit", null, (_, _) =>
            {
                _forceClose = true;
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                _trayIcon = null;
                Close();
            });
            _trayIcon.ContextMenuStrip = menu;
        }

        private void RestoreFromTray()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
            if (_trayIcon != null) _trayIcon.Visible = false;

            // Re-apply theme and color-match after tray restore
            if (ThemeManager.MainColorMatchEnabled && _mainAlbumPrimary != default)
                ApplyMainColorMatch();
            else
                ApplyThemeTitleBar();
            ResumeAnimations();
            NpResumeVisibleWork(forceReloadLyrics: true, forceLyricResync: true);
        }
    }
}
