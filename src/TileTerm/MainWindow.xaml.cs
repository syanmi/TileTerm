using System;
using System.ComponentModel;
using System.Text;
using System.Windows;
using System.Windows.Input;
using TileTerm.Terminal;

namespace TileTerm;

/// <summary>
/// First-milestone shell: a single window hosting a single terminal session.
/// Splitting into multiple panes/sessions and a profile-picker UI are not
/// implemented yet (see CLAUDE.md) — this proves out the ConPTY + XTerm.NET
/// core the rest of the app will build on.
/// </summary>
public partial class MainWindow : Window
{
    private TerminalSession? _session;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        TerminalHost.PreviewTextInput += OnPreviewTextInput;
        TerminalHost.PreviewKeyDown += OnPreviewKeyDown;
        TerminalHost.SizeInCellsChanged += OnSizeInCellsChanged;

        var (cols, rows) = TerminalHost.MeasureCells(new Size(TerminalHost.ActualWidth, TerminalHost.ActualHeight));
        _session = new TerminalSession(cols, rows);
        TerminalHost.Terminal = _session.Terminal;

        _session.OutputReceived += () => Dispatcher.BeginInvoke(() => TerminalHost.InvalidateVisual());
        _session.Exited += code => Dispatcher.BeginInvoke(() => Title = $"TileTerm - プロセス終了 (exit code {code})");

        TerminalHost.Focus();

        try
        {
            await _session.StartAsync(ProfileDefinition.Default);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"セッションの起動に失敗しました:\n{ex.Message}", "TileTerm",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnSizeInCellsChanged(int cols, int rows) => _session?.Resize(cols, rows);

    private void OnClosing(object? sender, CancelEventArgs e) => _session?.Dispose();

    private void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        var terminal = _session?.Terminal;
        if (terminal is null || string.IsNullOrEmpty(e.Text)) return;

        var sb = new StringBuilder();
        foreach (char c in e.Text)
            sb.Append(terminal.GenerateCharInput(c, XTerm.Input.KeyModifiers.None));

        Send(sb.ToString());
        e.Handled = true;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var terminal = _session?.Terminal;
        if (terminal is null) return;

        var modifiers = TerminalCanvas.MapModifiers(Keyboard.Modifiers);

        // Ctrl+letter (e.g. Ctrl+C) is not a TextInput event, so it has to be
        // handled here and turned back into a plain character for XTerm.NET.
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key is >= Key.A and <= Key.Z)
        {
            char c = (char)('a' + (e.Key - Key.A));
            Send(terminal.GenerateCharInput(c, modifiers));
            e.Handled = true;
            return;
        }

        var mapped = TerminalCanvas.MapKey(e.Key);
        if (mapped is { } key)
        {
            Send(terminal.GenerateKeyInput(key, modifiers));
            e.Handled = true;
        }
    }

    private void Send(string sequence)
    {
        if (string.IsNullOrEmpty(sequence)) return;
        _ = _session?.SendTextAsync(sequence);
    }
}
