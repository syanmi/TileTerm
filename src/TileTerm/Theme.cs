using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace TileTerm;

/// <summary>
/// Shared dark palette, applied consistently to every window in the app
/// (MainWindow's own title bar draws itself; this is for the rest —
/// SettingsWindow, MessageDialog, and anything added later).
///
/// <see cref="Button"/> specifically needs its own <see cref="ControlTemplate"/>
/// (<see cref="Button"/>), not just Background/Foreground property values:
/// the stock WPF button template's hover/pressed/disabled visuals come from
/// the OS theme and ignore those properties, which is exactly what made a
/// themed button turn unreadable (light hover highlight, still-light text)
/// the moment the mouse moved over it. A custom template fixes every state
/// at once instead of chasing individual triggers per window.
/// </summary>
internal static class Theme
{
    public static readonly Brush BgWindow = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));
    public static readonly Brush BgField = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x2D));
    public static readonly Brush BgList = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x25));
    public static readonly Brush BgHover = new SolidColorBrush(Color.FromRgb(0x3F, 0x3F, 0x3F));
    public static readonly Brush BgPressed = new SolidColorBrush(Color.FromRgb(0x50, 0x50, 0x50));
    public static readonly Brush BorderCol = new SolidColorBrush(Color.FromRgb(0x3F, 0x3F, 0x3F));
    public static readonly Brush Fg = Brushes.Gainsboro;
    public static readonly Brush FgDisabled = new SolidColorBrush(Color.FromRgb(0x77, 0x77, 0x77));

    private static readonly ControlTemplate SharedButtonTemplate = BuildButtonTemplate();

    /// <summary>Applies the window-level dark theme: content background/foreground, and
    /// (via <see cref="DarkTitleBar"/>) the OS-drawn title bar strip too.</summary>
    public static void Apply(Window window)
    {
        window.Background = BgWindow;
        window.Foreground = Fg;
        DarkTitleBar.Apply(window);
    }

    /// <summary>A Button pre-wired with the dark template — every state (normal, hover,
    /// pressed, disabled) stays legible.</summary>
    public static Button Button(object content)
    {
        return new Button
        {
            Content = content,
            Template = SharedButtonTemplate,
            Background = BgField,
            Foreground = Fg,
            BorderBrush = BorderCol,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 4, 10, 4),
        };
    }

    public static TextBox TextBox()
    {
        return new TextBox
        {
            Background = BgField,
            Foreground = Fg,
            BorderBrush = BorderCol,
            CaretBrush = Fg,
            Padding = new Thickness(3),
        };
    }

    private static ControlTemplate BuildButtonTemplate()
    {
        var template = new ControlTemplate(typeof(Button));

        var border = new FrameworkElementFactory(typeof(Border)) { Name = "Bd" };
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));

        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.MarginProperty, new TemplateBindingExtension(Control.PaddingProperty));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);

        template.VisualTree = border;

        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, BgHover, "Bd"));
        template.Triggers.Add(hover);

        var pressed = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter(Border.BackgroundProperty, BgPressed, "Bd"));
        template.Triggers.Add(pressed);

        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(Control.ForegroundProperty, FgDisabled));
        disabled.Setters.Add(new Setter(Border.BorderBrushProperty, BgField, "Bd"));
        template.Triggers.Add(disabled);

        return template;
    }
}
