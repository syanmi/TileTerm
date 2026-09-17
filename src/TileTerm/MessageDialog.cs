using System.Windows;
using System.Windows.Controls;

namespace TileTerm;

/// <summary>
/// Dark-themed replacement for <see cref="MessageBox"/>. The native MessageBox
/// always renders with the OS's own (light) theme regardless of this app's
/// theme, which looks jarringly out of place next to the rest of TileTerm.
/// </summary>
public sealed class MessageDialog : Window
{
    public MessageBoxResult Result { get; private set; } = MessageBoxResult.None;

    private MessageDialog(string message, string title, MessageBoxButton buttons)
    {
        Title = title;
        SizeToContent = SizeToContent.WidthAndHeight;
        MinWidth = 360;
        MaxWidth = 480;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Theme.Apply(this);

        var root = new Grid { Margin = new Thickness(20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(20) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var text = new TextBlock
        {
            Text = message,
            Foreground = Theme.Fg,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
        };
        Grid.SetRow(text, 0);
        root.Children.Add(text);

        var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        Grid.SetRow(buttonPanel, 2);
        root.Children.Add(buttonPanel);

        if (buttons == MessageBoxButton.OKCancel)
        {
            buttonPanel.Children.Add(MakeButton("OK", MessageBoxResult.OK, isDefault: true));
            buttonPanel.Children.Add(MakeButton("キャンセル", MessageBoxResult.Cancel, isCancel: true));
        }
        else
        {
            buttonPanel.Children.Add(MakeButton("OK", MessageBoxResult.OK, isDefault: true));
        }

        Content = root;
    }

    private Button MakeButton(string label, MessageBoxResult result, bool isDefault = false, bool isCancel = false)
    {
        var button = Theme.Button(label);
        button.MinWidth = 84;
        button.Margin = new Thickness(6, 0, 0, 0);
        button.IsDefault = isDefault;
        button.IsCancel = isCancel;
        button.Click += (_, _) =>
        {
            Result = result;
            Close();
        };
        return button;
    }

    /// <summary>Shows a dark-themed modal message dialog. Closing without choosing a
    /// button (e.g. Alt+F4) reports <see cref="MessageBoxResult.None"/>, which callers
    /// checking specifically for <see cref="MessageBoxResult.OK"/> will correctly treat
    /// as "not confirmed".</summary>
    public static MessageBoxResult Show(Window? owner, string message, string title, MessageBoxButton buttons = MessageBoxButton.OK)
    {
        var dialog = new MessageDialog(message, title, buttons) { Owner = owner };
        dialog.ShowDialog();
        return dialog.Result;
    }
}
