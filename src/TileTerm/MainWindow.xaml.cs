using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using TileTerm.Terminal;

namespace TileTerm;

/// <summary>
/// App shell: a custom (VSCode-style) title bar that doubles as the pane
/// split/close controls plus a favorites quick-launch bar, and a
/// <see cref="PaneManager"/>-driven content area.
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>Shared by every window (they all live in one process, see <see cref="SingleInstance"/>), so
    /// a profile/favorite edited from any window's settings shows up in all of them.</summary>
    private readonly ProfileStore _profileStore = ProfileStore.Shared;
    private PaneManager? _paneManager;

    /// <summary>The settings dialog, if open. There is only ever one for the whole app — opening it from
    /// another window brings the existing one forward — so two dialogs can't hold conflicting edits.</summary>
    private static SettingsWindow? s_settingsWindow;

    private static ImageSource? s_attentionBadge;

    public MainWindow()
    {
        InitializeComponent();
        WindowMaximizeFix.Apply(this);

        AppIconHost.Content = Icons.App();
        SettingsButton.Content = Icons.Settings();
        SplitRightButton.Content = Icons.SplitRight();
        SplitDownButton.Content = Icons.SplitDown();
        CloseTileButton.Content = Icons.Close(14);
        MinimizeButton.Content = Icons.Minimize();
        MaximizeButton.Content = Icons.Maximize();
        CloseWindowButton.Content = Icons.Close();

        // Not AssemblyInformationalVersionAttribute: the SDK appends a "+<git sha>" suffix
        // to it automatically in a git repo, which is far too long for the title bar.
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = version is null ? "" : $"v{version.Major}.{version.Minor}.{version.Build}";

        Loaded += OnLoaded;
        Closing += OnClosing;
        StateChanged += (_, _) =>
        {
            MaximizeButton.Content = WindowState == WindowState.Maximized ? Icons.Restore() : Icons.Maximize();

            // WindowChrome still hit-tests the top few pixels of a maximized window as
            // "resize border" (they'd normally let you drag-resize that edge), which can
            // steal clicks meant to drag the title bar right at its top edge — the window
            // just silently ignores the click. There's nothing to resize once maximized,
            // so zero the resize border then and restore it when back to normal size.
            var chrome = WindowChrome.GetWindowChrome(this);
            if (chrome is not null)
                chrome.ResizeBorderThickness = WindowState == WindowState.Maximized ? new Thickness(0) : new Thickness(4);
        };

        SettingsButton.Click += (_, _) => OpenSettings();
        SplitRightButton.Click += (_, _) => SplitWithDefault(SplitDirection.Right);
        SplitDownButton.Click += (_, _) => SplitWithDefault(SplitDirection.Down);
        CloseTileButton.Click += (_, _) => _paneManager?.CloseActive();

        MinimizeButton.Click += (_, _) => SystemCommands.MinimizeWindow(this);
        MaximizeButton.Click += (_, _) =>
        {
            if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this);
            else SystemCommands.MaximizeWindow(this);
        };
        CloseWindowButton.Click += (_, _) => Close();

        UpdateButton.Click += (_, _) => ApplyUpdate();
        UpdateService.Instance.PendingChanged += RefreshUpdateButton;
        _profileStore.Changed += RebuildFavoritesBar;
        Closed += (_, _) =>
        {
            UpdateService.Instance.PendingChanged -= RefreshUpdateButton;
            _profileStore.Changed -= RebuildFavoritesBar;
        };

        Activated += (_, _) => ClearAttentionBadge();
    }

    /// <summary>Marks this window's taskbar button (a red dot over the icon) to show that a tile asked for
    /// attention while the window was in the background; cleared when the window is activated.
    /// See <see cref="AttentionService"/>.</summary>
    public void ShowAttentionBadge()
    {
        TaskbarItemInfo ??= new TaskbarItemInfo();
        TaskbarItemInfo.Overlay = s_attentionBadge ??= CreateAttentionBadge();
        TaskbarItemInfo.Description = "TileTerm - 通知があります";
    }

    private void ClearAttentionBadge()
    {
        if (TaskbarItemInfo is null) return;
        TaskbarItemInfo.Overlay = null;
        TaskbarItemInfo.Description = "";
    }

    /// <summary>A red dot with a white rim (so it stands out against any icon), as a bitmap: the taskbar
    /// overlay is converted to an icon, which works best from pixels.</summary>
    private static ImageSource CreateAttentionBadge()
    {
        const int size = 32;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0xE8, 0x11, 0x23)), new Pen(Brushes.White, 3),
                new Point(size / 2.0, size / 2.0), size / 2.0 - 2, size / 2.0 - 2);
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var initial = _profileStore.GetDefaultProfile() ?? new ProfileDefinition();
        _paneManager = new PaneManager(PaneHost, initial);
        _paneManager.LastPaneCloseRequested += () => Close();
        RebuildFavoritesBar();

        RefreshUpdateButton();   // a version downloaded in an earlier run may still be waiting
        _ = CheckForUpdatesAtStartupAsync();
    }

    /// <summary>Looks for a new version in the background and downloads it; the title bar then offers
    /// "更新". A failure here (offline, ...) is deliberately silent: it is not worth interrupting a
    /// terminal session for, and the settings page has a "今すぐ確認" button that reports the reason.</summary>
    private static async Task CheckForUpdatesAtStartupAsync()
    {
        var updates = UpdateService.Instance;
        if (!AppSettings.Current.CheckForUpdates || !updates.IsInstalled) return;
        await updates.CheckAndDownloadAsync();
    }

    /// <summary>Shows the title bar's "更新" button while a downloaded update is waiting to be applied.</summary>
    private void RefreshUpdateButton()
    {
        var version = UpdateService.Instance.PendingVersion;
        if (version is null)
        {
            UpdateButton.Visibility = Visibility.Collapsed;
            return;
        }

        UpdateButton.Content = new TextBlock
        {
            Text = $"↑ v{version} に更新", FontSize = 12, Foreground = Theme.Accent, VerticalAlignment = VerticalAlignment.Center,
        };
        UpdateButton.ToolTip = $"新しいバージョン v{version} をダウンロード済みです。クリックすると、確認のうえ再起動して更新します";
        UpdateButton.Visibility = Visibility.Visible;
    }

    /// <summary>Asks, then restarts into the downloaded version. The app is closed the normal way (so every
    /// session is shut down) and the updater — already started, waiting for this process to end — installs
    /// the new version and launches it.</summary>
    private void ApplyUpdate()
    {
        var version = UpdateService.Instance.PendingVersion;
        if (version is null) return;

        var answer = MessageDialog.Show(
            this,
            $"TileTerm v{version} をダウンロードしました。\n今すぐ再起動して更新しますか？\n\n開いているすべてのウィンドウとタイルが閉じられます(分割の配置は保存されません)。",
            "TileTerm - 更新", MessageBoxButton.OKCancel);
        if (answer != MessageBoxResult.OK) return;

        try
        {
            UpdateService.Instance.ApplyAfterExitAndRestart();
        }
        catch (Exception ex)
        {
            MessageDialog.Show(this, $"更新を開始できませんでした:\n{ex.Message}", "TileTerm - 更新");
            return;
        }

        // Every window, since they are all this one process; none of them asks "close all tiles?" again.
        App.CloseAllForUpdate();
    }

    private void SplitWithDefault(SplitDirection direction)
    {
        var profile = _profileStore.GetDefaultProfile();
        if (profile is null)
        {
            MessageDialog.Show(this, "既定のプロンプトが設定されていません。設定から追加してください。", "TileTerm");
            return;
        }
        _paneManager?.SplitActive(direction, profile);
    }

    private void OpenSettings()
    {
        // Modeless, so the other windows' terminals keep taking input while it is open (a modal dialog
        // disables every window of the thread, i.e. all of TileTerm). Its edits reach every window through
        // ProfileStore.Changed and the theme brushes.
        if (s_settingsWindow is { } open)
        {
            open.Owner = this;   // move it in front of the window it was asked from
            if (open.WindowState == WindowState.Minimized) open.WindowState = WindowState.Normal;
            open.Activate();
            return;
        }

        var window = new SettingsWindow(_profileStore) { Owner = this };
        window.Closed += (_, _) => s_settingsWindow = null;
        s_settingsWindow = window;
        window.Show();
    }

    /// <summary>Rebuilds the row of favorite-profile quick-launch buttons after the
    /// title bar's Settings button. Each one splits the active pane on double-click:
    /// left button = 縦分割 (vertical divider, side by side), right button = 横分割
    /// (horizontal divider, stacked) — matching the split-direction buttons' own
    /// left/right visual metaphor.</summary>
    private void RebuildFavoritesBar()
    {
        FavoritesPanel.Children.Clear();
        foreach (var profile in _profileStore.GetFavoritesInOrder())
            FavoritesPanel.Children.Add(BuildFavoriteButton(profile));
    }

    /// <summary>"アイコン画像 名前" — the profile's icon followed by its name.</summary>
    private static UIElement BuildFavoriteContent(ProfileDefinition profile)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        var icon = ProfileIcons.CreateImage(ProfileIcons.Get(profile));
        icon.VerticalAlignment = VerticalAlignment.Center;
        panel.Children.Add(icon);
        panel.Children.Add(new TextBlock
        {
            Text = profile.Name, FontSize = 12, Foreground = Theme.Fg,
            Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
        });
        return panel;
    }

    private Button BuildFavoriteButton(ProfileDefinition profile)
    {
        // A real Button (rather than a plain Border) so it's a proper control:
        // discoverable via UI Automation/screen readers, and it picks up the
        // same hover styling as the other title-bar buttons for free.
        var button = new Button
        {
            Style = (Style)FindResource("TitleBarButton"),
            Content = BuildFavoriteContent(profile),
            ToolTip = $"{profile.Name}\nダブルクリック: 縦分割 / 右ダブルクリック: 横分割",
        };
        AutomationProperties.SetName(button, $"お気に入り: {profile.Name}");
        WindowChrome.SetIsHitTestVisibleInChrome(button, true);

        button.PreviewMouseDown += (_, e) =>
        {
            if (e.ClickCount != 2) return;
            if (e.ChangedButton == MouseButton.Left)
                _paneManager?.SplitActive(SplitDirection.Right, profile);
            else if (e.ChangedButton == MouseButton.Right)
                _paneManager?.SplitActive(SplitDirection.Down, profile);
        };

        return button;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        // Applying an update already asked (see ApplyUpdate), so it closes without the second question.
        if (!App.ClosingForUpdate)
        {
            var result = MessageDialog.Show(
                this, "このウィンドウで開いているすべてのタイルを閉じます。よろしいですか？", "TileTerm", MessageBoxButton.OKCancel);

            if (result != MessageBoxResult.OK)
            {
                e.Cancel = true;
                return;
            }
        }

        _paneManager?.ShutdownAll();
    }
}
