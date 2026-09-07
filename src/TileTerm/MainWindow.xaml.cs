using System.ComponentModel;
using System.Windows;
using TileTerm.Terminal;

namespace TileTerm;

/// <summary>
/// App shell: a thin top bar (Settings) plus a <see cref="PaneManager"/>-driven
/// content area that can be split into any number of terminal panes.
/// </summary>
public partial class MainWindow : Window
{
    private readonly ProfileStore _profileStore = new();
    private PaneManager? _paneManager;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closing += OnClosing;
        SettingsButton.Click += (_, _) => OpenSettings();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _profileStore.Load();
        var initial = _profileStore.Profiles.Count > 0 ? _profileStore.Profiles[0] : new ProfileDefinition();
        _paneManager = new PaneManager(PaneHost, _profileStore, initial);
    }

    private void OpenSettings()
    {
        var window = new ProfileSettingsWindow(_profileStore) { Owner = this };
        window.ShowDialog();
    }

    private void OnClosing(object? sender, CancelEventArgs e) => _paneManager?.ShutdownAll();
}
