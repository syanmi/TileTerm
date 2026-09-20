using System;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
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
    private readonly TerminalScrollBar _scrollBar = new();
    private double _wheelNotches;
    private Point? _dragStartPoint;
    private bool _composing;
    private int _compositionLength;   // characters of the IME box's text that are still being composed
    private Key _lastKey;             // the key behind the input being processed (for an IME-processed key, the key the IME saw)
    private Rect _cursorRect;
    private readonly DispatcherTimer _leftoverTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };

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
        terminalArea.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        terminalArea.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        terminalArea.Children.Add(_canvas);
        terminalArea.Children.Add(_imeBox);
        SetColumn(_scrollBar, 1);
        terminalArea.Children.Add(_scrollBar);
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
        TextCompositionManager.AddPreviewTextInputStartHandler(_imeBox, (_, _) =>
        {
            _composing = true;
            _leftoverTimer.Stop();
        });
        TextCompositionManager.AddPreviewTextInputUpdateHandler(_imeBox, OnCompositionUpdate);
        _imeBox.TextChanged += OnImeBoxTextChanged;
        _leftoverTimer.Tick += (_, _) =>
        {
            _leftoverTimer.Stop();
            if (!_composing && _imeBox.Text.Length > 0) _imeBox.Clear();
        };

        _canvas.SizeInCellsChanged += (cols, rows) => Session?.Resize(cols, rows);
        _canvas.CursorMoved += rect => Dispatcher.BeginInvoke(() =>
        {
            _cursorRect = rect;
            PlaceImeBox();
        });
        _canvas.PreviewMouseDown += (_, _) => _imeBox.Focus();
        _canvas.MouseRightButtonUp += (_, e) =>
        {
            // Like the Windows console: right-click copies the selection, or pastes when nothing is selected.
            if (_canvas.HasSelection) CopySelection();
            else Paste();
            e.Handled = true;
        };
        _canvas.ViewChanged += (max, value, rows) => Dispatcher.BeginInvoke(() => _scrollBar.Update(max, value, rows));
        _scrollBar.PreviewMouseDown += (_, _) => _imeBox.Focus();
        _scrollBar.Scrolled += line => _canvas.ScrollToLine(line);
        _scrollBar.PageRequested += direction =>
            _canvas.ScrollLines(direction * Math.Max(1, (Session?.Terminal.Rows ?? 1) - 1));
        terminalArea.MouseWheel += OnTerminalMouseWheel;

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
            BorderThickness = new Thickness(0),
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

    /// <summary>Keeps the IME box on the terminal cursor cell, so the IME anchors its candidate window
    /// there. Text left in the box from earlier phrases (see <see cref="OnPreviewTextInput"/>) sits in front
    /// of the phrase being composed, so the box is moved left by that much to keep the composition itself
    /// at the cursor.</summary>
    private void PlaceImeBox()
    {
        double leftover = 0;
        int leftoverLength = _imeBox.Text.Length - _compositionLength;
        if (leftoverLength > 0)
        {
            _imeBox.UpdateLayout();
            var rect = _imeBox.GetRectFromCharacterIndex(Math.Min(leftoverLength, _imeBox.Text.Length));
            if (!rect.IsEmpty) leftover = rect.X;
        }

        _imeBox.Margin = new Thickness(_cursorRect.X - leftover, _cursorRect.Y, 0, 0);
        _imeBox.Height = _cursorRect.Height;
    }

    /// <summary>The box is a text sink that gives the IME somewhere to compose and to put its candidate
    /// window; it is never visible. What is being composed is drawn on the terminal itself
    /// (<see cref="TerminalCanvas.CompositionText"/>), which is why the box's own leftover text does not matter.</summary>
    private void OnCompositionUpdate(object sender, TextCompositionEventArgs e)
    {
        string text = e.TextComposition.CompositionText ?? "";
        _composing = text.Length > 0;
        _compositionLength = text.Length;
        _canvas.CompositionText = text;

        // Cancelled (Esc, or deleted back to nothing): text confirmed earlier can go once things are quiet.
        if (!_composing) _leftoverTimer.Start();
        Dispatcher.BeginInvoke(PlaceImeBox, DispatcherPriority.Loaded);
    }

    private void OnImeBoxTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_imeBox.Text.Length > 0) return;

        _composing = false; // composition cancelled (e.g. Esc) or the box was emptied
        _compositionLength = 0;
        _canvas.CompositionText = null;
    }

    /// <summary>Whether a key can begin a new IME composition — every typing key can; Enter, Esc, Tab,
    /// editing and cursor keys cannot.</summary>
    private static bool CanStartComposition(Key key) =>
        key is not (Key.Enter or Key.Escape or Key.Tab or Key.Back or Key.Delete or Key.Insert
            or Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End or Key.PageUp or Key.PageDown)
        && !(key >= Key.F1 && key <= Key.F24);

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

        bool imeCommit = _composing;
        Send(sb.ToString());
        _composing = false;
        _compositionLength = 0;
        _canvas.CompositionText = null;

        // Plain typed characters never reach the box; consuming them keeps it that way. An IME's
        // confirmed text is left alone (not marked handled): consuming it stops the IME from starting the
        // next composition with the same keystroke — typing "ga" after converting a phrase confirms it
        // and begins a new phrase with that key, and that first key was being lost.
        if (!imeCommit)
        {
            e.Handled = true;
            return;
        }

        // The box keeps its copy of the confirmed text. Changing the box's text while the IME is starting
        // its next composition cancels that composition, so the text is only removed right away when the
        // key that confirmed it (Enter and the like) cannot begin another one; otherwise it waits for
        // a quiet moment (see _leftoverTimer).
        if (CanStartComposition(_lastKey))
        {
            _leftoverTimer.Stop();
            _leftoverTimer.Start();
        }
        else
        {
            Dispatcher.BeginInvoke(() => { if (!_composing) _imeBox.Clear(); });
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        _lastKey = e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key;

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

        if (TryHandleClipboardKey(e)) return;
        if (TryHandleScrollKey(e, terminal)) return;

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

        // Typing (or pasting) while looking at older output jumps back to the live end, like the
        // Windows console and Windows Terminal do.
        if (!_canvas.IsAtBottom) _canvas.ScrollToBottom();
        if (_canvas.HasSelection) _canvas.ClearSelection();

        _ = Session?.SendTextAsync(sequence);
    }

    /// <summary>Copy and paste shortcuts. Ctrl+C copies only while text is selected — with nothing selected
    /// it is still the interrupt key and goes to the program — and Ctrl+Shift+C / Ctrl+Insert always
    /// mean "copy". Ctrl+V, Ctrl+Shift+V and Shift+Insert paste.</summary>
    private bool TryHandleClipboardKey(KeyEventArgs e)
    {
        var modifiers = Keyboard.Modifiers;
        bool ctrl = modifiers == ModifierKeys.Control;
        bool ctrlShift = modifiers == (ModifierKeys.Control | ModifierKeys.Shift);

        bool copy = (ctrl && e.Key == Key.C && _canvas.HasSelection)
            || (ctrlShift && e.Key == Key.C)
            || (ctrl && e.Key == Key.Insert);
        bool paste = ((ctrl || ctrlShift) && e.Key == Key.V)
            || (modifiers == ModifierKeys.Shift && e.Key == Key.Insert);
        if (!copy && !paste) return false;

        if (copy) CopySelection();
        else Paste();
        e.Handled = true;
        return true;
    }

    private void CopySelection()
    {
        string text = _canvas.GetSelectedText();
        if (text.Length == 0) return;

        if (TrySetClipboard(text))
            _canvas.ClearSelection();
    }

    /// <summary>Sends the clipboard's text to the program as if it had been typed. Line breaks become
    /// Enter, other control characters are dropped (a stray Esc or Ctrl+C in copied text should not act
    /// as a command), and a program that asked for bracketed paste gets the text wrapped in the markers
    /// that tell it "this is a paste" (shells use them so a pasted line is not run before you press Enter).</summary>
    private void Paste()
    {
        var terminal = Session?.Terminal;
        if (terminal is null) return;

        string? text = TryGetClipboardText();
        if (string.IsNullOrEmpty(text)) return;

        var sb = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\r')
            {
                sb.Append('\r');
                if (i + 1 < text.Length && text[i + 1] == '\n') i++;
            }
            else if (c == '\n') sb.Append('\r');
            else if (c == '\t' || (c >= ' ' && c != '\u007f')) sb.Append(c);
        }
        if (sb.Length == 0) return;

        if (terminal.BracketedPasteMode)
            Send("\u001b[200~" + sb + "\u001b[201~");
        else
            Send(sb.ToString());
    }

    // Another program can hold the clipboard open for a moment; that surfaces as an ExternalException.
    private static string? TryGetClipboardText()
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                return Clipboard.ContainsText() ? Clipboard.GetText() : null;
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                System.Threading.Thread.Sleep(20);
            }
        }
        return null;
    }

    private static bool TrySetClipboard(string text)
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Clipboard.SetText(text);
                return true;
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                System.Threading.Thread.Sleep(20);
            }
        }
        return false;
    }

    /// <summary>The keyboard side of scrolling back through output (only while the normal screen is shown;
    /// a full-screen app owns its own scrolling): Shift+PageUp/PageDown a page at a time, Ctrl+Shift+Up/Down
    /// a line at a time, Ctrl+Shift+Home/End to the very top/bottom — Windows Terminal's shortcuts. Keys
    /// with Shift/Ctrl+Shift are otherwise unused by shells, so nothing a program relies on is taken.</summary>
    private bool TryHandleScrollKey(KeyEventArgs e, XTerm.Terminal terminal)
    {
        if (terminal.IsAlternateBufferActive) return false;

        var modifiers = Keyboard.Modifiers;
        int page = Math.Max(1, terminal.Rows - 1);

        if (modifiers == ModifierKeys.Shift)
        {
            switch (e.Key)
            {
                case Key.PageUp: _canvas.ScrollLines(-page); e.Handled = true; return true;
                case Key.PageDown: _canvas.ScrollLines(page); e.Handled = true; return true;
            }
        }
        else if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            switch (e.Key)
            {
                case Key.Up: _canvas.ScrollLines(-1); e.Handled = true; return true;
                case Key.Down: _canvas.ScrollLines(1); e.Handled = true; return true;
                case Key.Home: _canvas.ScrollToTop(); e.Handled = true; return true;
                case Key.End: _canvas.ScrollToBottom(); e.Handled = true; return true;
            }
        }
        return false;
    }

    /// <summary>Mouse wheel over the tile. Depending on what the program in the tile has asked for, the
    /// wheel scrolls our own history (an ordinary shell), is passed to the program as mouse-wheel events
    /// (it enabled mouse reporting — holding Shift overrides that, as in other terminals), or becomes
    /// Up/Down keys (a full-screen program that did not ask for the mouse, like less).</summary>
    private void OnTerminalMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var terminal = Session?.Terminal;
        if (terminal is null) return;
        e.Handled = true;

        // Touchpads report many small deltas; count whole wheel notches and keep the remainder.
        _wheelNotches += e.Delta / 120.0;
        int notches = (int)_wheelNotches;
        if (notches == 0) return;
        _wheelNotches -= notches;

        bool towardsOlder = notches > 0;
        int count = Math.Abs(notches);

        if (terminal.MouseTrackingMode != XTerm.Input.MouseTrackingMode.None && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
        {
            var (col, row) = _canvas.CellAt(e.GetPosition(_canvas));
            var button = towardsOlder ? XTerm.Input.MouseButton.WheelUp : XTerm.Input.MouseButton.WheelDown;
            var type = towardsOlder ? XTerm.Input.MouseEventType.WheelUp : XTerm.Input.MouseEventType.WheelDown;
            var modifiers = TerminalCanvas.MapModifiers(Keyboard.Modifiers);
            for (int i = 0; i < count; i++)
                _ = Session?.SendTextAsync(terminal.GenerateMouseEvent(button, col, row, type, modifiers));
            return;
        }

        // The system setting is lines per notch; -1 means "one screen per notch".
        int linesPerNotch = SystemParameters.WheelScrollLines > 0 ? SystemParameters.WheelScrollLines : terminal.Rows;
        int lines = count * linesPerNotch;

        if (terminal.IsAlternateBufferActive)
        {
            var arrow = terminal.GenerateKeyInput(towardsOlder ? XTerm.Input.Key.UpArrow : XTerm.Input.Key.DownArrow,
                XTerm.Input.KeyModifiers.None);
            _ = Session?.SendTextAsync(string.Concat(Enumerable.Repeat(arrow, lines)));
            return;
        }

        _canvas.ScrollLines(towardsOlder ? -lines : lines);
    }
}
