using System;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace TileTerm.Terminal;

/// <summary>
/// One visible pane: a thin title bar (profile name + restart/close buttons)
/// over a <see cref="TerminalCanvas"/>, plus the <see cref="TerminalSession"/>
/// that backs it. This is the leaf-level building block <see cref="PaneManager"/>
/// arranges into a splittable grid. Splitting itself is driven from the main
/// window's own title bar (see <see cref="MainWindow"/>), not from here.
/// </summary>
public sealed class TerminalPaneControl : Grid
{
    private readonly TerminalCanvas _canvas = new();
    private readonly TextBlock _titleText;
    private readonly Border _activeBorder;

    public TerminalSession? Session { get; private set; }
    public ProfileDefinition Profile { get; }

    /// <summary>Raised when this pane's terminal receives keyboard focus.</summary>
    public event Action? Activated;

    /// <summary>Raised when the user clicks the close ("×") button.</summary>
    public event Action? CloseRequested;

    public TerminalPaneControl(ProfileDefinition profile)
    {
        Profile = profile;

        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        _titleText = new TextBlock
        {
            Foreground = Brushes.Gainsboro,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0),
            FontSize = 12,
            Text = profile.Name,
        };

        var header = BuildHeader();
        SetRow(header, 0);
        Children.Add(header);

        SetRow(_canvas, 1);
        Children.Add(_canvas);

        // A thin overlay border spanning both rows, used to highlight whichever pane is active.
        _activeBorder = new Border
        {
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(2),
            IsHitTestVisible = false,
        };
        SetRow(_activeBorder, 0);
        SetRowSpan(_activeBorder, 2);
        Children.Add(_activeBorder);

        _canvas.PreviewTextInput += OnPreviewTextInput;
        _canvas.PreviewKeyDown += OnPreviewKeyDown;
        _canvas.SizeInCellsChanged += (cols, rows) => Session?.Resize(cols, rows);
        _canvas.GotKeyboardFocus += (_, _) => Activated?.Invoke();
        _canvas.PreviewMouseDown += (_, _) => _canvas.Focus();

        Loaded += OnLoaded;
    }

    private FrameworkElement BuildHeader()
    {
        var panel = new DockPanel
        {
            Background = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x25)),
            LastChildFill = true,
        };

        var closeButton = MakeButton("×");
        closeButton.ToolTip = "このタイルを閉じる";
        closeButton.Click += (_, _) => CloseRequested?.Invoke();
        DockPanel.SetDock(closeButton, Dock.Right);
        panel.Children.Add(closeButton);

        var refreshButton = MakeButton("更新");
        refreshButton.ToolTip = "このタイルのコンソールを再起動";
        refreshButton.Click += (_, _) => RestartSession();
        DockPanel.SetDock(refreshButton, Dock.Right);
        panel.Children.Add(refreshButton);

        panel.Children.Add(_titleText);
        return panel;
    }

    private static Button MakeButton(string text) => new()
    {
        Content = text,
        Padding = new Thickness(6, 2, 6, 2),
        Margin = new Thickness(1),
        Background = Brushes.Transparent,
        BorderThickness = new Thickness(0),
        Foreground = Brushes.Gainsboro,
        FontSize = 11,
    };

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await StartSessionAsync();
    }

    /// <summary>(Re)creates the session and wires it up to the canvas. Used both for the
    /// initial launch and for the "更新" (restart) button.</summary>
    private async System.Threading.Tasks.Task StartSessionAsync()
    {
        var (cols, rows) = _canvas.MeasureCells(new Size(_canvas.ActualWidth, _canvas.ActualHeight));
        Session = new TerminalSession(cols, rows);
        _canvas.Terminal = Session.Terminal;

        Session.OutputReceived += () => Dispatcher.BeginInvoke(() => _canvas.InvalidateVisual());
        Session.Exited += code => Dispatcher.BeginInvoke(() => _titleText.Text = $"{Profile.Name} (終了 code={code})");

        _titleText.Text = Profile.Name;
        _canvas.Focus();

        try
        {
            await Session.StartAsync(Profile);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                Window.GetWindow(this), $"セッションの起動に失敗しました:\n{Profile.Executable}\n\n{ex.Message}",
                "TileTerm", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void RestartSession()
    {
        Session?.Dispose();
        Session = null;
        _canvas.Terminal = null;
        _canvas.InvalidateVisual();

        await StartSessionAsync();
    }

    public void SetActive(bool active) =>
        _activeBorder.BorderBrush = active ? Brushes.DodgerBlue : Brushes.Transparent;

    public void FocusCanvas() => _canvas.Focus();

    /// <summary>Tears down the backing session. Call once when this pane is removed from the tree.</summary>
    public void Shutdown() => Session?.Dispose();

    private void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        var terminal = Session?.Terminal;
        if (terminal is null || string.IsNullOrEmpty(e.Text)) return;

        var sb = new StringBuilder();
        foreach (char c in e.Text)
            sb.Append(terminal.GenerateCharInput(c, XTerm.Input.KeyModifiers.None));

        Send(sb.ToString());
        e.Handled = true;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var terminal = Session?.Terminal;
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
        _ = Session?.SendTextAsync(sequence);
    }
}
