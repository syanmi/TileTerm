using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace TileTerm;

/// <summary>
/// Shared dark palette + control styling, used by every window in the app
/// (MainWindow's own title bar draws itself; this is for the rest —
/// SettingsWindow, MessageDialog, and anything added later). Modeled loosely
/// after VSCode's/JetBrains' settings dialogs: one accent color used
/// consistently for selection/focus/primary actions, rounded-corner fields,
/// and thin low-contrast dividers instead of heavy borders everywhere.
///
/// <see cref="Button"/>/<see cref="TextBox"/>/<see cref="ListBox"/> all need
/// their own <see cref="ControlTemplate"/>, not just Background/Foreground
/// property values: the stock WPF templates' hover/pressed/selected/disabled
/// visuals come from the OS theme and ignore those properties (this is what
/// made a themed button turn unreadable on hover before this class existed).
/// A custom template fixes every state at once instead of chasing individual
/// triggers per window.
/// </summary>
internal static class Theme
{
    public static readonly Brush BgWindow = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));
    public static readonly Brush BgField = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x2D));
    public static readonly Brush BgList = new SolidColorBrush(Color.FromRgb(0x21, 0x21, 0x21));
    public static readonly Brush BgHover = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A));
    public static readonly Brush BgPressed = new SolidColorBrush(Color.FromRgb(0x48, 0x48, 0x48));
    public static readonly Brush BorderCol = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A));
    public static readonly Brush Fg = Brushes.Gainsboro;
    public static readonly Brush FgMuted = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A));
    public static readonly Brush FgDisabled = new SolidColorBrush(Color.FromRgb(0x77, 0x77, 0x77));
    public static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(0x3A, 0x9B, 0xF5));
    public static readonly Brush AccentHover = new SolidColorBrush(Color.FromRgb(0x5B, 0xAC, 0xF7));

    /// <summary>Colors for the "既定/お気に入り" status glyphs in a profile list row.
    /// Deliberately not <see cref="Accent"/> — a gold star on an Accent-colored selected
    /// row would be nearly invisible; gold/pink both stay legible on either background.</summary>
    public static readonly Brush GoldStar = new SolidColorBrush(Color.FromRgb(0xE0, 0xB0, 0x4A));
    public static readonly Brush Favorite = new SolidColorBrush(Color.FromRgb(0xE0, 0x60, 0x7D));

    private const double Radius = 3;

    private static readonly ControlTemplate SharedButtonTemplate = BuildButtonTemplate(BgField, BgHover, BgPressed);
    private static readonly ControlTemplate PrimaryButtonTemplate = BuildButtonTemplate(Accent, AccentHover, Accent);
    private static readonly ControlTemplate TextBoxTemplate = BuildTextBoxTemplate();
    private static readonly Style ListBoxItemStyle = BuildListBoxItemStyle();

    /// <summary>Applies the window-level dark theme: content background/foreground, and
    /// (via <see cref="DarkTitleBar"/>) the OS-drawn title bar strip too.</summary>
    public static void Apply(Window window)
    {
        window.Background = BgWindow;
        window.Foreground = Fg;
        DarkTitleBar.Apply(window);
    }

    /// <summary>A secondary/flat button — every state (normal, hover, pressed, disabled)
    /// stays legible and matches the rest of the dialog.</summary>
    public static Button Button(object content) => new()
    {
        Content = content,
        Template = SharedButtonTemplate,
        Background = BgField,
        Foreground = Fg,
        BorderBrush = BorderCol,
        BorderThickness = new Thickness(1),
        Padding = new Thickness(10, 3, 10, 3),
        FontSize = 12,
    };

    /// <summary>The accent-colored "main action" button for a dialog (e.g. Save/OK).</summary>
    public static Button PrimaryButton(object content) => new()
    {
        Content = content,
        Template = PrimaryButtonTemplate,
        Background = Accent,
        Foreground = Brushes.White,
        BorderThickness = new Thickness(0),
        Padding = new Thickness(11, 3, 11, 3),
        FontSize = 12,
    };

    /// <summary>A small icon-only button (list toolbars: +/− etc.) — same states as
    /// <see cref="Button"/> but compact and without a visible border at rest.</summary>
    public static Button IconButton(object content)
    {
        var button = Button(content);
        button.Padding = new Thickness(4);
        button.BorderThickness = new Thickness(0);
        button.MinWidth = 0;
        return button;
    }

    public static TextBox TextBox() => new()
    {
        Template = TextBoxTemplate,
        Background = BgField,
        Foreground = Fg,
        BorderBrush = BorderCol,
        CaretBrush = Fg,
        BorderThickness = new Thickness(1),
        Padding = new Thickness(5, 3, 5, 3),
        FontSize = 12,
    };

    /// <summary>A ListBox styled with accent-colored selection instead of the OS's own
    /// (which would clash with the rest of the dark theme).</summary>
    public static ListBox ListBox() => new()
    {
        Background = BgList,
        Foreground = Fg,
        BorderBrush = BorderCol,
        BorderThickness = new Thickness(1),
        Padding = new Thickness(2),
        FontSize = 12,
        ItemContainerStyle = ListBoxItemStyle,
    };

    /// <summary>Small muted caption text used above a field (name, meaning, etc.).</summary>
    public static TextBlock Label(string text) => new()
    {
        Text = text,
        Foreground = FgMuted,
        FontSize = 10.5,
        Margin = new Thickness(0, 0, 0, 3),
        TextWrapping = TextWrapping.Wrap,
    };

    /// <summary>
    /// A draggable, resizable divider between two Grid columns/rows — a wide
    /// transparent hit-test strip with a 1px line centered inside it, so it reads
    /// as a thin, unobtrusive divider rather than one solid thick bar, while
    /// staying easy to grab. Used both between panes (<c>PaneManager</c>) and
    /// between the panels of a settings-style dialog.
    /// </summary>
    public static GridSplitter Splitter(bool vertical)
    {
        var splitter = new GridSplitter
        {
            Width = vertical ? 5 : double.NaN,
            Height = vertical ? double.NaN : 5,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Background = Brushes.Transparent,
            ResizeBehavior = GridResizeBehavior.PreviousAndNext,
            Cursor = vertical ? Cursors.SizeWE : Cursors.SizeNS,
            UseLayoutRounding = true,
            SnapsToDevicePixels = true,
        };

        var line = new FrameworkElementFactory(typeof(Border));
        line.SetValue(Border.BackgroundProperty, BorderCol);
        line.SetValue(UIElement.SnapsToDevicePixelsProperty, true);
        if (vertical)
        {
            line.SetValue(FrameworkElement.WidthProperty, 1.0);
            line.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            line.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Stretch);
        }
        else
        {
            line.SetValue(FrameworkElement.HeightProperty, 1.0);
            line.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            line.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        }

        splitter.Template = new ControlTemplate(typeof(GridSplitter)) { VisualTree = line };
        return splitter;
    }

    /// <summary>A fixed (non-draggable) 1px low-contrast divider line — vertical if
    /// <paramref name="vertical"/>, horizontal otherwise. Use <see cref="Splitter"/>
    /// instead when the two sides should be resizable.</summary>
    public static Border Divider(bool vertical) => new()
    {
        Background = BorderCol,
        Width = vertical ? 1 : double.NaN,
        Height = vertical ? double.NaN : 1,
        HorizontalAlignment = vertical ? HorizontalAlignment.Stretch : HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Stretch,
        SnapsToDevicePixels = true,
    };

    private static ControlTemplate BuildButtonTemplate(Brush normal, Brush hoverBg, Brush pressedBg)
    {
        var template = new ControlTemplate(typeof(Button));

        var border = new FrameworkElementFactory(typeof(Border)) { Name = "Bd" };
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(Radius));

        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.MarginProperty, new TemplateBindingExtension(Control.PaddingProperty));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);

        template.VisualTree = border;

        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, hoverBg, "Bd"));
        template.Triggers.Add(hover);

        var pressed = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter(Border.BackgroundProperty, pressedBg, "Bd"));
        template.Triggers.Add(pressed);

        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(Control.ForegroundProperty, FgDisabled));
        disabled.Setters.Add(new Setter(Border.BackgroundProperty, BgField, "Bd"));
        disabled.Setters.Add(new Setter(Border.BorderBrushProperty, BgField, "Bd"));
        template.Triggers.Add(disabled);

        return template;
    }

    private static ControlTemplate BuildTextBoxTemplate()
    {
        var template = new ControlTemplate(typeof(TextBox));

        var border = new FrameworkElementFactory(typeof(Border)) { Name = "Bd" };
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(Radius));

        // TextBox's editing surface must be a "PART_ContentHost" element — required by the
        // TextBox control model regardless of how the rest of the template looks.
        var host = new FrameworkElementFactory(typeof(ScrollViewer)) { Name = "PART_ContentHost" };
        host.SetValue(FrameworkElement.MarginProperty, new TemplateBindingExtension(Control.PaddingProperty));
        border.AppendChild(host);

        template.VisualTree = border;

        var focused = new Trigger { Property = UIElement.IsFocusedProperty, Value = true };
        focused.Setters.Add(new Setter(Border.BorderBrushProperty, Accent, "Bd"));
        template.Triggers.Add(focused);

        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(Control.ForegroundProperty, FgDisabled));
        template.Triggers.Add(disabled);

        return template;
    }

    private static Style BuildListBoxItemStyle()
    {
        var template = new ControlTemplate(typeof(ListBoxItem));

        var border = new FrameworkElementFactory(typeof(Border)) { Name = "Bd" };
        border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(Radius));
        border.SetValue(Border.PaddingProperty, new Thickness(6, 3, 6, 3));
        border.SetValue(FrameworkElement.MarginProperty, new Thickness(0));

        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        border.AppendChild(presenter);
        template.VisualTree = border;

        // Order matters: later triggers win when more than one applies at once, so the
        // "selected" look must be added after "hover" to stay on top of a hovered+selected row.
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, BgHover, "Bd"));
        template.Triggers.Add(hover);

        var selected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Border.BackgroundProperty, Accent, "Bd"));
        selected.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
        template.Triggers.Add(selected);

        var style = new Style(typeof(ListBoxItem));
        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        style.Setters.Add(new Setter(Control.ForegroundProperty, Fg));
        style.Setters.Add(new Setter(FrameworkElement.FocusVisualStyleProperty, null));
        return style;
    }
}
