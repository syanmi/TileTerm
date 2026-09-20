using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
    private readonly ProfileStore _profileStore = new();
    private PaneManager? _paneManager;
    private bool _applyingUpdate;

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
        Closed += (_, _) => UpdateService.Instance.PendingChanged -= RefreshUpdateButton;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _profileStore.Load();
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
            $"TileTerm v{version} をダウンロードしました。\n今すぐ再起動して更新しますか？\n\n開いているタイルはすべて閉じられます(分割の配置は保存されません)。",
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

        _applyingUpdate = true;   // OnClosing must not ask "close all tiles?" again
        Close();
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
        var window = new SettingsWindow(_profileStore) { Owner = this };
        window.ShowDialog();
        RebuildFavoritesBar(); // profiles / default / favorites may have changed
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
        if (!_applyingUpdate)
        {
            var result = MessageDialog.Show(
                this, "開いているすべてのタイルを閉じます。よろしいですか？", "TileTerm", MessageBoxButton.OKCancel);

            if (result != MessageBoxResult.OK)
            {
                e.Cancel = true;
                return;
            }
        }

        _paneManager?.ShutdownAll();
    }
}
