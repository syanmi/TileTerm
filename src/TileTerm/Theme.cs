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

    /// <summary>Corner radius for text fields and buttons. Kept small on purpose — JetBrains
    /// settings dialogs (the reference for this styling) use nearly square controls, and larger
    /// radii read as "soft/chunky" next to that. List rows are fully square (no radius).</summary>
    private const double Radius = 2;

    /// <summary>Height shared by single-line fields and buttons, so a row of them lines up
    /// and the dialog stays as dense as the JetBrains reference (~24px controls).</summary>
    public const double ControlHeight = 24;

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
        Padding = new Thickness(12, 0, 12, 0),
        MinHeight = ControlHeight,
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
        Padding = new Thickness(12, 0, 12, 0),
        MinHeight = ControlHeight,
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
        button.MinHeight = 0;
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
        Padding = new Thickness(5, 0, 5, 0),
        VerticalContentAlignment = VerticalAlignment.Center,
        Height = ControlHeight,
        FontSize = 12,
    };

    /// <summary>A ListBox styled with accent-colored selection instead of the OS's own
    /// (which would clash with the rest of the dark theme). Rows run edge to edge — no inset
    /// and no rounded corners — like the JetBrains settings tree/list.</summary>
    public static ListBox ListBox() => new()
    {
        Background = BgList,
        Foreground = Fg,
        BorderBrush = BorderCol,
        BorderThickness = new Thickness(1),
        Padding = new Thickness(0),
        FontSize = 12,
        ItemContainerStyle = ListBoxItemStyle,
    };

    /// <summary>A dark-themed check box: a small square that fills with the accent color and
    /// shows a check mark when checked (the stock one is a white OS-styled box).</summary>
    public static CheckBox CheckBox(string content)
    {
        var template = new ControlTemplate(typeof(CheckBox));

        var root = new FrameworkElementFactory(typeof(StackPanel));
        root.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        root.SetValue(Panel.BackgroundProperty, Brushes.Transparent);

        var box = new FrameworkElementFactory(typeof(Border)) { Name = "Box" };
        box.SetValue(FrameworkElement.WidthProperty, 14.0);
        box.SetValue(FrameworkElement.HeightProperty, 14.0);
        box.SetValue(Border.BackgroundProperty, BgField);
        box.SetValue(Border.BorderBrushProperty, BorderCol);
        box.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        box.SetValue(Border.CornerRadiusProperty, new CornerRadius(Radius));
        box.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);

        var check = new FrameworkElementFactory(typeof(System.Windows.Shapes.Path)) { Name = "Check" };
        check.SetValue(System.Windows.Shapes.Path.DataProperty, Geometry.Parse("M 2.5,6.5 L 5.5,9.5 L 10.5,3.5"));
        check.SetValue(System.Windows.Shapes.Shape.StrokeProperty, Brushes.White);
        check.SetValue(System.Windows.Shapes.Shape.StrokeThicknessProperty, 1.6);
        check.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);
        box.AppendChild(check);
        root.AppendChild(box);

        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.MarginProperty, new Thickness(6, 0, 0, 0));
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        root.AppendChild(presenter);

        template.VisualTree = root;

        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BorderBrushProperty, FgMuted, "Box"));
        template.Triggers.Add(hover);

        var isChecked = new Trigger { Property = System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, Value = true };
        isChecked.Setters.Add(new Setter(Border.BackgroundProperty, Accent, "Box"));
        isChecked.Setters.Add(new Setter(Border.BorderBrushProperty, Accent, "Box"));
        isChecked.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "Check"));
        template.Triggers.Add(isChecked);

        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(Control.ForegroundProperty, FgDisabled));
        disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.6, "Box"));
        template.Triggers.Add(disabled);

        return new CheckBox
        {
            Content = content,
            Template = template,
            Foreground = Fg,
            FontSize = 12,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
    }

    /// <summary>A form row's label, sitting to the left of its field (JetBrains layout).</summary>
    public static TextBlock Label(string text) => new()
    {
        Text = text,
        Foreground = Fg,
        FontSize = 12,
        VerticalAlignment = VerticalAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    /// <summary>Small muted explanatory text shown under a field.</summary>
    public static TextBlock Hint(string text) => new()
    {
        Text = text,
        Foreground = FgMuted,
        FontSize = 11,
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
        host.SetValue(FrameworkElement.VerticalAlignmentProperty, new TemplateBindingExtension(Control.VerticalContentAlignmentProperty));
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
        border.SetValue(Border.PaddingProperty, new Thickness(8, 2, 8, 2));
        border.SetValue(FrameworkElement.MarginProperty, new Thickness(0));
        border.SetValue(FrameworkElement.MinHeightProperty, ControlHeight - 2);

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
