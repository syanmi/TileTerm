using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace TileTerm;

/// <summary>
/// Shared palette + control styling, used by every window in the app. Modeled loosely
/// after VSCode's/JetBrains' settings dialogs: one accent color used
/// consistently for selection/focus/primary actions, rounded-corner fields,
/// and thin low-contrast dividers instead of heavy borders everywhere.
///
/// <b>Switching themes at runtime.</b> Each color is one shared, mutable
/// <see cref="SolidColorBrush"/>; <see cref="ApplyPalette"/> re-colors those same instances,
/// so anything holding one follows a theme change immediately. Two rules make that work:
/// code-built UI assigns the shared brush instance directly to a property (a Freezable set as
/// a plain property value stays live), while XAML and control templates use
/// <c>DynamicResource</c> keys instead (see <see cref="RegisterResources"/>) — a brush placed
/// straight into a Style/Template setter, or into a ResourceDictionary, gets frozen and could
/// never change color again.
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
    private static SolidColorBrush NewBrush(Color color) => new(color);

    public static readonly SolidColorBrush BgWindow = NewBrush(Colors.Transparent);
    public static readonly SolidColorBrush BgField = NewBrush(Colors.Transparent);
    public static readonly SolidColorBrush BgList = NewBrush(Colors.Transparent);
    public static readonly SolidColorBrush BgSidebar = NewBrush(Colors.Transparent);
    public static readonly SolidColorBrush BgHover = NewBrush(Colors.Transparent);
    public static readonly SolidColorBrush BgPressed = NewBrush(Colors.Transparent);
    public static readonly SolidColorBrush BgTitleBar = NewBrush(Colors.Transparent);
    public static readonly SolidColorBrush BgTitleHover = NewBrush(Colors.Transparent);
    public static readonly SolidColorBrush BorderCol = NewBrush(Colors.Transparent);
    public static readonly SolidColorBrush Fg = NewBrush(Colors.Transparent);
    public static readonly SolidColorBrush FgMuted = NewBrush(Colors.Transparent);
    public static readonly SolidColorBrush FgDisabled = NewBrush(Colors.Transparent);
    public static readonly SolidColorBrush Accent = NewBrush(Colors.Transparent);
    public static readonly SolidColorBrush AccentHover = NewBrush(Colors.Transparent);
    public static readonly SolidColorBrush PaneActiveBorder = NewBrush(Colors.Transparent);

    // Tiles look the same in every theme (see TileScheme), so these never change color.
    public static readonly SolidColorBrush TileHeaderBg = NewBrush(TileScheme.HeaderBg);
    public static readonly SolidColorBrush TileFg = NewBrush(TileScheme.HeaderFg);
    public static readonly SolidColorBrush TerminalBg = NewBrush(TileScheme.TerminalBg);
    public static readonly SolidColorBrush TerminalFg = NewBrush(TileScheme.TerminalFg);

    /// <summary>Colors for the "既定/お気に入り" status glyphs in a profile list row.
    /// Deliberately not <see cref="Accent"/> — a gold star on an Accent-colored selected
    /// row would be nearly invisible; gold/pink both stay legible on either background.</summary>
    public static readonly SolidColorBrush GoldStar = NewBrush(Colors.Transparent);
    public static readonly SolidColorBrush Favorite = NewBrush(Colors.Transparent);

    // DynamicResource keys for the brushes above (see the class summary).
    private const string KeyBgField = "Theme.BgField";
    private const string KeyBgHover = "Theme.BgHover";
    private const string KeyBgPressed = "Theme.BgPressed";
    private const string KeyBorder = "Theme.Border";
    private const string KeyFg = "Theme.Fg";
    private const string KeyFgMuted = "Theme.FgMuted";
    private const string KeyFgDisabled = "Theme.FgDisabled";
    private const string KeyAccent = "Theme.Accent";
    private const string KeyAccentHover = "Theme.AccentHover";

    private static DynamicResourceExtension Res(string key) => new(key);

    private static ResourceDictionary? _resources;

    /// <summary>The brushes that XAML and templates reach through <c>DynamicResource</c>, with
    /// the palette color each one shows. Kept as one table so registering and re-coloring
    /// can't drift apart.</summary>
    private static readonly (string Key, SolidColorBrush Brush, Func<ThemePalette, Color> Pick)[] Shared =
    {
        ("Theme.BgWindow", BgWindow, p => p.BgWindow),
        (KeyBgField, BgField, p => p.BgField),
        ("Theme.BgList", BgList, p => p.BgList),
        (KeyBgHover, BgHover, p => p.BgHover),
        (KeyBgPressed, BgPressed, p => p.BgPressed),
        ("Theme.BgTitleBar", BgTitleBar, p => p.BgTitleBar),
        ("Theme.BgTitleHover", BgTitleHover, p => p.BgTitleHover),
        ("Theme.BgSidebar", BgSidebar, p => p.BgSidebar),
        (KeyBorder, BorderCol, p => p.Border),
        (KeyFg, Fg, p => p.Fg),
        (KeyFgMuted, FgMuted, p => p.FgMuted),
        (KeyFgDisabled, FgDisabled, p => p.FgDisabled),
        (KeyAccent, Accent, p => p.Accent),
        (KeyAccentHover, AccentHover, p => p.AccentHover),
    };

    /// <summary>Publishes the palette as application resources so XAML and templates can
    /// reference it with <c>{DynamicResource Theme.*}</c>. The resources are separate brush
    /// instances from the shared ones above (a ResourceDictionary freezes the Freezables put in
    /// it, which would make the shared brushes un-recolorable), so a theme change replaces
    /// the resource entries — DynamicResource re-resolves on replacement — as well as re-coloring
    /// the shared brushes that code-built UI holds directly.</summary>
    public static void RegisterResources(ResourceDictionary resources)
    {
        _resources = resources;
        foreach (var (key, brush, _) in Shared)
            resources[key] = new SolidColorBrush(brush.Color);
    }

    /// <summary>Switches every brush — shared instances and registered resources — to <paramref name="palette"/>.</summary>
    public static void ApplyPalette(ThemePalette palette)
    {
        foreach (var (key, brush, pick) in Shared)
        {
            var color = pick(palette);
            brush.Color = color;
            if (_resources is not null)
                _resources[key] = new SolidColorBrush(color);
        }

        PaneActiveBorder.Color = palette.PaneActiveBorder;
        GoldStar.Color = palette.GoldStar;
        Favorite.Color = palette.Favorite;
    }

    /// <summary>Corner radius for text fields and buttons. Kept small on purpose — JetBrains
    /// settings dialogs (the reference for this styling) use nearly square controls, and larger
    /// radii read as "soft/chunky" next to that. List rows are fully square (no radius).</summary>
    private const double Radius = 2;

    /// <summary>Height shared by single-line fields and buttons, so a row of them lines up
    /// and the dialog stays as dense as the JetBrains reference (~24px controls).</summary>
    public const double ControlHeight = 24;

    private static readonly ControlTemplate SharedButtonTemplate = BuildButtonTemplate(KeyBgHover, KeyBgPressed);
    private static readonly ControlTemplate PrimaryButtonTemplate = BuildButtonTemplate(KeyAccentHover, KeyAccent);
    private static readonly ControlTemplate TextBoxTemplate = BuildTextBoxTemplate();
    private static readonly Style ListBoxItemStyle = BuildListBoxItemStyle();

    static Theme() => ApplyPalette(ThemePalette.Dark);

    /// <summary>Applies the window-level theme: content background/foreground, and
    /// (via <see cref="TitleBarTheme"/>) the OS-drawn title bar strip too.</summary>
    public static void Apply(Window window)
    {
        window.Background = BgWindow;
        window.Foreground = Fg;
        TitleBarTheme.Apply(window);
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
        box.SetResourceReference(Border.BackgroundProperty, KeyBgField);
        box.SetResourceReference(Border.BorderBrushProperty, KeyBorder);
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
        hover.Setters.Add(new Setter(Border.BorderBrushProperty, Res(KeyFgMuted), "Box"));
        template.Triggers.Add(hover);

        var isChecked = new Trigger { Property = System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, Value = true };
        isChecked.Setters.Add(new Setter(Border.BackgroundProperty, Res(KeyAccent), "Box"));
        isChecked.Setters.Add(new Setter(Border.BorderBrushProperty, Res(KeyAccent), "Box"));
        isChecked.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "Check"));
        template.Triggers.Add(isChecked);

        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(Control.ForegroundProperty, Res(KeyFgDisabled)));
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

    /// <summary>A selectable "card": a bordered panel around arbitrary content that acts as a radio
    /// button — the border turns accent-colored when selected and mid-gray on hover. Used for the
    /// theme picker, where each choice is a little preview rather than a text label.</summary>
    public static RadioButton CardRadioButton(object content)
    {
        var template = new ControlTemplate(typeof(RadioButton));

        var border = new FrameworkElementFactory(typeof(Border)) { Name = "Bd" };
        border.SetResourceReference(Border.BackgroundProperty, KeyBgField);
        border.SetResourceReference(Border.BorderBrushProperty, KeyBorder);
        border.SetValue(Border.BorderThicknessProperty, new Thickness(2));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(Radius + 2));
        border.SetValue(Border.PaddingProperty, new Thickness(8));
        border.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
        template.VisualTree = border;

        // Later triggers win, so "checked" goes after "hover" and stays accent-colored under the mouse.
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BorderBrushProperty, Res(KeyFgMuted), "Bd"));
        template.Triggers.Add(hover);

        var isChecked = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
        isChecked.Setters.Add(new Setter(Border.BorderBrushProperty, Res(KeyAccent), "Bd"));
        template.Triggers.Add(isChecked);

        return new RadioButton
        {
            Content = content,
            Template = template,
            Cursor = Cursors.Hand,
            Focusable = true,
            FocusVisualStyle = null,
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
        line.SetResourceReference(Border.BackgroundProperty, KeyBorder);
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

    private static ControlTemplate BuildButtonTemplate(string hoverBgKey, string pressedBgKey)
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
        hover.Setters.Add(new Setter(Border.BackgroundProperty, Res(hoverBgKey), "Bd"));
        template.Triggers.Add(hover);

        var pressed = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter(Border.BackgroundProperty, Res(pressedBgKey), "Bd"));
        template.Triggers.Add(pressed);

        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(Control.ForegroundProperty, Res(KeyFgDisabled)));
        disabled.Setters.Add(new Setter(Border.BackgroundProperty, Res(KeyBgField), "Bd"));
        disabled.Setters.Add(new Setter(Border.BorderBrushProperty, Res(KeyBgField), "Bd"));
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
        focused.Setters.Add(new Setter(Border.BorderBrushProperty, Res(KeyAccent), "Bd"));
        template.Triggers.Add(focused);

        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(Control.ForegroundProperty, Res(KeyFgDisabled)));
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
        hover.Setters.Add(new Setter(Border.BackgroundProperty, Res(KeyBgHover), "Bd"));
        template.Triggers.Add(hover);

        var selected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Border.BackgroundProperty, Res(KeyAccent), "Bd"));
        selected.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
        template.Triggers.Add(selected);

        var style = new Style(typeof(ListBoxItem));
        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        style.Setters.Add(new Setter(Control.ForegroundProperty, Res(KeyFg)));
        style.Setters.Add(new Setter(FrameworkElement.FocusVisualStyleProperty, null));
        return style;
    }
}
