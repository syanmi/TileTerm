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
///
/// The title bar also doubles as a VSCode-style drag handle: dragging it over
/// another pane and dropping near an edge docks this pane there, and dropping
/// in the middle swaps the two panes' positions (see <see cref="DockRequested"/>
/// and <see cref="PaneManager"/>, which owns the actual tree surgery).
/// </summary>
public sealed class TerminalPaneControl : Grid
{
    /// <summary>Identifies this pane during a drag — carried in the <see cref="DataObject"/>
    /// since a WPF drag payload can't be a live object reference across the operation.</summary>
    public const string DragFormat = "TileTerm.PaneId";

    /// <summary>Thickness of the active-pane highlight border itself. 1px read as too faint
    /// to notice, so this is deliberately a bit heavier.</summary>
    private const double ActiveBorderThickness = 2;

    /// <summary>How far the header/canvas are inset from the pane's true edge — always at
    /// least <see cref="ActiveBorderThickness"/> (so the border never paints over content) plus a
    /// little extra breathing room, so text doesn't start on the pixel right next to the line.</summary>
    private const double ContentInset = ActiveBorderThickness + 2;

    /// <summary>Outer edge fraction of the pane that counts as a docking zone rather than
    /// the center (swap) zone — see <see cref="ComputeDropZone"/>.</summary>
    private const double EdgeZoneFraction = 0.28;

    private readonly TerminalCanvas _canvas = new();
    private readonly TextBlock _titleText;
    private readonly Image _titleIcon;
    private readonly Border _activeBorder;
    private readonly Border _dropZoneOverlay;
    private readonly TextBox _imeBox;
    private Point? _dragStartPoint;
    private bool _composing;

    public Guid PaneId { get; } = Guid.NewGuid();
    public TerminalSession? Session { get; private set; }
    public ProfileDefinition Profile { get; }

    /// <summary>Raised when this pane's terminal receives keyboard focus, or its title bar
    /// is clicked.</summary>
    public event Action? Activated;

    /// <summary>Raised when the user clicks the close ("×") button.</summary>
    public event Action? CloseRequested;

    /// <summary>Raised when another pane is dropped onto this one — carries the dragged
    /// pane's <see cref="PaneId"/> and where it was released.</summary>
    public event Action<Guid, DropZone>? DockRequested;

    public TerminalPaneControl(ProfileDefinition profile)
    {
        Profile = profile;

        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        _titleText = new TextBlock
        {
            Foreground = Theme.TileFg,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0),
            FontSize = 12,
            Text = profile.Name,
        };
        _titleIcon = ProfileIcons.CreateImage(ProfileIcons.Get(profile));
        _titleIcon.Margin = new Thickness(6, 0, 0, 0);
        _titleIcon.VerticalAlignment = VerticalAlignment.Center;

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

        // The canvas only draws; keyboard focus (and with it the IME) lives in a transparent
        // TextBox stacked on top of it at the terminal cursor. A plain FrameworkElement can
        // technically receive IME-committed text, but it has no way to show the in-progress
        // composition or to tell the IME where the candidate window belongs — so typing
        // Japanese showed nothing until confirmed, with the candidates floating far away.
        // A real TextBox gets both (inline composition, candidate window at its caret) for free.
        _imeBox = BuildImeBox();
        var terminalArea = new Grid();
        terminalArea.Children.Add(_canvas);
        terminalArea.Children.Add(_imeBox);
        Grid.SetRow(terminalArea, 1);
        content.Children.Add(terminalArea);

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

        _dropZoneOverlay = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(90, 0x3A, 0x9B, 0xF5)),
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
        };
        SetRow(_dropZoneOverlay, 0);
        SetRowSpan(_dropZoneOverlay, 2);
        Children.Add(_dropZoneOverlay);

        _imeBox.PreviewTextInput += OnPreviewTextInput;
        _imeBox.PreviewKeyDown += OnPreviewKeyDown;
        _imeBox.GotKeyboardFocus += (_, _) => Activated?.Invoke();
        TextCompositionManager.AddPreviewTextInputStartHandler(_imeBox, (_, _) => _composing = true);
        TextCompositionManager.AddPreviewTextInputUpdateHandler(_imeBox, (_, _) => _composing = true);
        _imeBox.TextChanged += OnImeBoxTextChanged;

        _canvas.SizeInCellsChanged += (cols, rows) => Session?.Resize(cols, rows);
        _canvas.CursorMoved += rect => Dispatcher.BeginInvoke(() => PlaceImeBox(rect));
        _canvas.PreviewMouseDown += (_, _) => _imeBox.Focus();

        AllowDrop = true;
        DragOver += OnDragOver;
        DragLeave += (_, _) => _dropZoneOverlay.Visibility = Visibility.Collapsed;
        Drop += OnDrop;

        Loaded += OnLoaded;
    }

    private static TextBox BuildImeBox()
    {
        var box = new TextBox
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            MinWidth = 2,
            MinHeight = 0,
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0, 0, 0, 1),
            BorderBrush = Brushes.Transparent,
            Background = Brushes.Transparent,
            Foreground = Brushes.Transparent,
            CaretBrush = Brushes.Transparent,
            SelectionBrush = Brushes.Transparent,
            FontFamily = TerminalCanvas.Font,
            FontSize = TerminalCanvas.FontSize,
            IsUndoEnabled = false,
            AllowDrop = false,
            IsHitTestVisible = false,
            ContextMenu = null,
        };
        InputMethod.SetIsInputMethodEnabled(box, true);
        return box;
    }

    /// <summary>Keeps the IME box sitting exactly on the terminal cursor cell, so the IME
    /// anchors its candidate window there.</summary>
    private void PlaceImeBox(Rect cursorRect)
    {
        _imeBox.Margin = new Thickness(cursorRect.X, cursorRect.Y, 0, 0);
        _imeBox.Height = cursorRect.Height;
    }

    /// <summary>The box only exists to host IME composition; while a composition is in
    /// progress it's shown (opaque, over the terminal text) so the user sees what they're
    /// typing, and once the text is committed — already sent to the terminal by
    /// <see cref="OnPreviewTextInput"/>, which TextBox still keeps a copy of — it's emptied.</summary>
    private void OnImeBoxTextChanged(object sender, TextChangedEventArgs e)
    {
        bool hasText = _imeBox.Text.Length > 0;
        if (!hasText) _composing = false; // composition cancelled (e.g. Esc) or emptied
        _imeBox.Foreground = hasText ? Theme.TerminalFg : Brushes.Transparent;
        _imeBox.Background = hasText ? Theme.TerminalBg : Brushes.Transparent;
        _imeBox.BorderBrush = hasText ? Theme.TerminalFg : Brushes.Transparent;

        if (hasText && !_composing)
            Dispatcher.BeginInvoke(() => { if (!_composing) _imeBox.Clear(); });
    }

    private FrameworkElement BuildHeader()
    {
        var panel = new DockPanel
        {
            Background = Theme.TileHeaderBg,
            LastChildFill = true,
            Cursor = Cursors.Hand,
        };

        var closeButton = MakeButton(Icons.Close(13, ink: Theme.TileFg));
        closeButton.ToolTip = "このタイルを閉じる";
        AutomationProperties.SetName(closeButton, "このタイルを閉じる");
        closeButton.Click += (_, _) => CloseRequested?.Invoke();
        DockPanel.SetDock(closeButton, Dock.Right);
        panel.Children.Add(closeButton);

        var refreshButton = MakeButton(Icons.Refresh(ink: Theme.TileFg));
        refreshButton.ToolTip = "このタイルのコンソールを再起動";
        AutomationProperties.SetName(refreshButton, "このタイルのコンソールを再起動");
        refreshButton.Click += (_, _) => RestartSession();
        DockPanel.SetDock(refreshButton, Dock.Right);
        panel.Children.Add(refreshButton);

        DockPanel.SetDock(_titleIcon, Dock.Left);
        panel.Children.Add(_titleIcon);
        panel.Children.Add(_titleText);

        // The title bar is both the "click to activate" target and the drag handle —
        // clicking it should feel the same as clicking anywhere else in the pane.
        panel.PreviewMouseLeftButtonDown += (_, _) =>
        {
            _dragStartPoint = Mouse.GetPosition(null);
            _imeBox.Focus();
        };
        panel.PreviewMouseMove += OnHeaderPreviewMouseMove;
        panel.GiveFeedback += OnHeaderGiveFeedback;

        return panel;
    }

    private void OnHeaderPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStartPoint is null || e.LeftButton != MouseButtonState.Pressed) return;

        var pos = e.GetPosition(null);
        if (Math.Abs(pos.X - _dragStartPoint.Value.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _dragStartPoint.Value.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        _dragStartPoint = null;
        DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(DragFormat, PaneId), DragDropEffects.Move);
    }

    /// <summary>The grab-hand cursor is only for hovering (something you <em>can</em> pick up);
    /// once dragging it goes back to a normal arrow, and — unlike WPF's default — never turns
    /// into a "forbidden" circle-slash over places a tile can't be dropped: that read as an
    /// error, and VSCode's own tile dragging doesn't show one either.</summary>
    private static void OnHeaderGiveFeedback(object sender, GiveFeedbackEventArgs e)
    {
        if (e.Effects != DragDropEffects.None) return;

        e.UseDefaultCursors = false;
        Mouse.SetCursor(Cursors.Arrow);
        e.Handled = true;
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (!IsValidDrag(e))
        {
            e.Effects = DragDropEffects.None;
            _dropZoneOverlay.Visibility = Visibility.Collapsed;
            e.Handled = true;
            return;
        }

        ShowDropOverlay(ComputeDropZone(e.GetPosition(this)));
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        _dropZoneOverlay.Visibility = Visibility.Collapsed;
        if (!IsValidDrag(e)) return;

        var sourcePaneId = (Guid)e.Data.GetData(DragFormat)!;
        var zone = ComputeDropZone(e.GetPosition(this));
        e.Handled = true;
        DockRequested?.Invoke(sourcePaneId, zone);
    }

    private bool IsValidDrag(DragEventArgs e) =>
        e.Data.GetDataPresent(DragFormat) && e.Data.GetData(DragFormat) is Guid id && id != PaneId;

    /// <summary>Left/right/top/bottom outer bands dock against that edge; the middle swaps.</summary>
    private DropZone ComputeDropZone(Point position)
    {
        double w = Math.Max(ActualWidth, 1);
        double h = Math.Max(ActualHeight, 1);
        double xRatio = position.X / w;
        double yRatio = position.Y / h;

        if (xRatio < EdgeZoneFraction) return DropZone.Left;
        if (xRatio > 1 - EdgeZoneFraction) return DropZone.Right;
        if (yRatio < EdgeZoneFraction) return DropZone.Top;
        if (yRatio > 1 - EdgeZoneFraction) return DropZone.Bottom;
        return DropZone.Center;
    }

    private void ShowDropOverlay(DropZone zone)
    {
        _dropZoneOverlay.HorizontalAlignment = HorizontalAlignment.Stretch;
        _dropZoneOverlay.VerticalAlignment = VerticalAlignment.Stretch;
        _dropZoneOverlay.Width = double.NaN;
        _dropZoneOverlay.Height = double.NaN;

        switch (zone)
        {
            case DropZone.Left:
                _dropZoneOverlay.HorizontalAlignment = HorizontalAlignment.Left;
                _dropZoneOverlay.Width = ActualWidth / 2;
                break;
            case DropZone.Right:
                _dropZoneOverlay.HorizontalAlignment = HorizontalAlignment.Right;
                _dropZoneOverlay.Width = ActualWidth / 2;
                break;
            case DropZone.Top:
                _dropZoneOverlay.VerticalAlignment = VerticalAlignment.Top;
                _dropZoneOverlay.Height = ActualHeight / 2;
                break;
            case DropZone.Bottom:
                _dropZoneOverlay.VerticalAlignment = VerticalAlignment.Bottom;
                _dropZoneOverlay.Height = ActualHeight / 2;
                break;
            case DropZone.Center:
                break; // full-size stretch, as reset above
        }

        _dropZoneOverlay.Visibility = Visibility.Visible;
    }

    private static Button MakeButton(object content) => new()
    {
        Content = content,
        Padding = new Thickness(7, 4, 7, 4),
        Margin = new Thickness(1),
        Background = Brushes.Transparent,
        BorderThickness = new Thickness(0),
        Foreground = Theme.TileFg,
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
            _titleText.Text = $"{Profile.Name} (終了 code={code})");

        _titleText.Text = Profile.Name;
        _imeBox.Focus();

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
        _activeBorder.BorderBrush = active ? Theme.PaneActiveBorder : Brushes.Transparent;

    public void FocusCanvas() => _imeBox.Focus();

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
        _composing = false;
        e.Handled = true;

        // TextBox still keeps its own copy of a committed IME composition even though this
        // handler consumed the event; TextChanged fires *before* this (while _composing is
        // still true), so the emptying has to happen here.
        Dispatcher.BeginInvoke(() => { if (!_composing) _imeBox.Clear(); });
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // While an IME composition is in progress, WPF reports every key it intercepts
        // (including Enter/Escape/Space/arrows used to convert and confirm candidates) as
        // Key.ImeProcessed rather than its literal key. Mapping and sending those straight
        // to the terminal — e.g. turning a conversion-confirming Enter into a literal
        // newline — hijacks the key before the IME can finish composing, so full-width
        // (zenkaku) input never completes. Leave these alone; the composed text still
        // arrives normally through PreviewTextInput once it's confirmed.
        if (e.Key == Key.ImeProcessed) return;

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
