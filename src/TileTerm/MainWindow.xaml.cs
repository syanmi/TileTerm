using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shell;
using TileTerm.Terminal;

namespace TileTerm;

/// <summary>
/// App shell: a custom (VSCode-style) title bar that doubles as the pane
/// split/close controls, plus a <see cref="PaneManager"/>-driven content area.
/// </summary>
public partial class MainWindow : Window
{
    private readonly ProfileStore _profileStore = new();
    private PaneManager? _paneManager;

    public MainWindow()
    {
        InitializeComponent();

        SplitRightButton.Content = Icons.SplitRight();
        SplitDownButton.Content = Icons.SplitDown();
        CloseTileButton.Content = Icons.Close(14);
        MinimizeButton.Content = Icons.Minimize();
        MaximizeButton.Content = Icons.Maximize();
        CloseWindowButton.Content = Icons.Close();

        Loaded += OnLoaded;
        Closing += OnClosing;
        StateChanged += (_, _) =>
        {
            MaximizeButton.Content = WindowState == WindowState.Maximized ? Icons.Restore() : Icons.Maximize();
            // WindowChrome still counts the (now invisible) resize border + non-client frame
            // as part of the window when maximized, which clips a few pixels off every edge.
            // Pad the content back in by that same amount only while maximized (standard
            // WindowChrome workaround — WindowResizeBorderThickness alone isn't enough).
            if (WindowState == WindowState.Maximized)
            {
                var resize = SystemParameters.WindowResizeBorderThickness;
                var frame = SystemParameters.WindowNonClientFrameThickness;
                RootGrid.Margin = new Thickness(
                    resize.Left + frame.Left, resize.Top + frame.Top,
                    resize.Right + frame.Right, resize.Bottom + frame.Bottom);
            }
            else
            {
                RootGrid.Margin = new Thickness(0);
            }
        };

        SettingsButton.Click += (_, _) => OpenSettings();
        SplitRightButton.Click += (_, _) => ShowSplitMenu(SplitRightButton, SplitDirection.Right);
        SplitDownButton.Click += (_, _) => ShowSplitMenu(SplitDownButton, SplitDirection.Down);
        CloseTileButton.Click += (_, _) => _paneManager?.CloseActive();

        MinimizeButton.Click += (_, _) => SystemCommands.MinimizeWindow(this);
        MaximizeButton.Click += (_, _) =>
        {
            if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this);
            else SystemCommands.MaximizeWindow(this);
        };
        CloseWindowButton.Click += (_, _) => Close();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _profileStore.Load();
        var initial = _profileStore.Profiles.Count > 0 ? _profileStore.Profiles[0] : new ProfileDefinition();
        _paneManager = new PaneManager(PaneHost, initial);
    }

    private void ShowSplitMenu(Button anchor, SplitDirection direction)
    {
        var menu = new ContextMenu();
        foreach (var p in _profileStore.Profiles)
        {
            var item = new MenuItem { Header = p.Name };
            item.Click += (_, _) => _paneManager?.SplitActive(direction, p);
            menu.Items.Add(item);
        }
        if (menu.Items.Count == 0)
            menu.Items.Add(new MenuItem { Header = "(プロファイルがありません。設定から追加してください)", IsEnabled = false });

        anchor.ContextMenu = menu;
        menu.IsOpen = true;
    }

    private void OpenSettings()
    {
        var window = new ProfileSettingsWindow(_profileStore) { Owner = this };
        window.ShowDialog();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        var result = MessageBox.Show(
            this, "開いているすべてのタイルを閉じます。よろしいですか？", "TileTerm",
            MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel);

        if (result != MessageBoxResult.OK)
        {
            e.Cancel = true;
            return;
        }

        _paneManager?.ShutdownAll();
    }
}
