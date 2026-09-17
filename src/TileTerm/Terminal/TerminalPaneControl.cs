using System;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TileTerm;

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
    /// <summary>Thickness of the active-pane highlight border itself. 1px read as too faint
    /// to notice, so this is deliberately a bit heavier.</summary>
    private const double ActiveBorderThickness = 2;

    /// <summary>How far the header/canvas are inset from the pane's true edge — always at
    /// least <see cref="ActiveBorderThickness"/> (so the border never paints over content) plus a
    /// little extra breathing room, so text doesn't start on the pixel right next to the line.</summary>
    private const double ContentInset = ActiveBorderThickness + 2;

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
            Text = $"{profile.DisplayIcon()}  {profile.Name}",
        };

        // Header + canvas live inside their own inset grid, so the active-pane border
        // (below) can be drawn at the pane's true outer edge without ever overlapping
        // their content — a border painted directly over the canvas used to eat into
        // the terminal text at the edges.
        var content = new Grid { Margin = new Thickness(ContentInset) };
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = BuildHeader();
        Grid.SetRow(header, 0);
        content.Children.Add(header);

        Grid.SetRow(_canvas, 1);
        content.Children.Add(_canvas);

        SetRow(content, 0);
        SetRowSpan(content, 2);
        Children.Add(content);

        _activeBorder = new Border
        {
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(ActiveBorderThickness),
            IsHitTestVisible = false,
            SnapsToDevicePixels = true,
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

        var closeButton = MakeButton(Icons.Close(13));
        closeButton.ToolTip = "このタイルを閉じる";
        AutomationProperties.SetName(closeButton, "このタイルを閉じる");
        closeButton.Click += (_, _) => CloseRequested?.Invoke();
        DockPanel.SetDock(closeButton, Dock.Right);
        panel.Children.Add(closeButton);

        var refreshButton = MakeButton(Icons.Refresh());
        refreshButton.ToolTip = "このタイルのコンソールを再起動";
        AutomationProperties.SetName(refreshButton, "このタイルのコンソールを再起動");
        refreshButton.Click += (_, _) => RestartSession();
        DockPanel.SetDock(refreshButton, Dock.Right);
        panel.Children.Add(refreshButton);

        panel.Children.Add(_titleText);
        return panel;
    }

    private static Button MakeButton(object content) => new()
    {
        Content = content,
        Padding = new Thickness(7, 4, 7, 4),
        Margin = new Thickness(1),
        Background = Brushes.Transparent,
        BorderThickness = new Thickness(0),
        Foreground = Brushes.Gainsboro,
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
        Session.Exited += code => Dispatcher.BeginInvoke(() =>
            _titleText.Text = $"{Profile.DisplayIcon()}  {Profile.Name} (終了 code={code})");

        _titleText.Text = $"{Profile.DisplayIcon()}  {Profile.Name}";
        _canvas.Focus();

        try
        {
            await Session.StartAsync(Profile);
        }
        catch (Exception ex)
        {
            MessageDialog.Show(
                Window.GetWindow(this), $"セッションの起動に失敗しました:\n{Profile.Executable}\n\n{ex.Message}", "TileTerm");
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
