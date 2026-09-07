using System.Windows;
using System.Windows.Controls;
using TileTerm.Terminal;

namespace TileTerm;

/// <summary>
/// Lets the user view/add/edit/remove the profiles (.exe path + arguments)
/// that "New Session"/split menus offer. Backed by <see cref="ProfileStore"/>,
/// which persists to <c>%AppData%\TileTerm\profiles.json</c>.
/// </summary>
public sealed class ProfileSettingsWindow : Window
{
    private readonly ProfileStore _store;
    private readonly ListBox _list = new() { DisplayMemberPath = "Name" };
    private readonly TextBox _nameBox = new();
    private readonly TextBox _exeBox = new();
    private readonly TextBox _argsBox = new();
    private readonly TextBox _cwdBox = new();
    private ProfileDefinition? _editing;

    public ProfileSettingsWindow(ProfileStore store)
    {
        _store = store;
        Title = "TileTerm - プロファイル設定";
        Width = 640;
        Height = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new Grid { Margin = new Thickness(10) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var leftPanel = new DockPanel();
        var listButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        var addButton = new Button { Content = "追加", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 4, 0) };
        var removeButton = new Button { Content = "削除", Padding = new Thickness(8, 2, 8, 2) };
        listButtons.Children.Add(addButton);
        listButtons.Children.Add(removeButton);
        DockPanel.SetDock(listButtons, Dock.Bottom);
        leftPanel.Children.Add(listButtons);
        leftPanel.Children.Add(_list);
        Grid.SetColumn(leftPanel, 0);
        root.Children.Add(leftPanel);

        var form = new StackPanel();
        form.Children.Add(LabeledBox("名前", _nameBox));
        form.Children.Add(LabeledBoxWithBrowse("実行ファイル (.exe / .cmd / .bat)", _exeBox));
        form.Children.Add(LabeledBox("引数（スペース区切り。空白を含む場合は \"...\" で囲む）", _argsBox));
        form.Children.Add(LabeledBox("作業ディレクトリ（空欄ならユーザーフォルダ）", _cwdBox));

        var saveButton = new Button
        {
            Content = "保存", Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Left,
        };
        saveButton.Click += OnSave;
        form.Children.Add(saveButton);

        var closeButton = new Button
        {
            Content = "閉じる", Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Left,
        };
        closeButton.Click += (_, _) => Close();
        form.Children.Add(closeButton);

        Grid.SetColumn(form, 2);
        root.Children.Add(form);

        Content = root;

        addButton.Click += (_, _) => AddNew();
        removeButton.Click += (_, _) => RemoveSelected();
        _list.SelectionChanged += (_, _) => LoadSelected();

        Refresh();
    }

    private static UIElement LabeledBox(string label, TextBox box)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 2), TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(box);
        return panel;
    }

    private UIElement LabeledBoxWithBrowse(string label, TextBox box)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 2) });

        var row = new DockPanel();
        var browseButton = new Button { Content = "参照...", Padding = new Thickness(6, 2, 6, 2) };
        browseButton.Click += (_, _) => BrowseExe(box);
        DockPanel.SetDock(browseButton, Dock.Right);
        row.Children.Add(browseButton);
        row.Children.Add(box);

        panel.Children.Add(row);
        return panel;
    }

    private void BrowseExe(TextBox target)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "実行可能ファイル (*.exe;*.cmd;*.bat)|*.exe;*.cmd;*.bat|すべてのファイル (*.*)|*.*",
            CheckFileExists = true,
        };
        if (dlg.ShowDialog(this) == true)
            target.Text = dlg.FileName;
    }

    private void AddNew()
    {
        var profile = new ProfileDefinition { Name = "新しいプロファイル" };
        _store.Profiles.Add(profile);
        _store.Save();
        Refresh();
        _list.SelectedItem = profile;
    }

    private void RemoveSelected()
    {
        if (_list.SelectedItem is not ProfileDefinition p) return;
        _store.Profiles.Remove(p);
        _store.Save();
        Refresh();
    }

    private void LoadSelected()
    {
        _editing = _list.SelectedItem as ProfileDefinition;
        _nameBox.Text = _editing?.Name ?? "";
        _exeBox.Text = _editing?.Executable ?? "";
        _argsBox.Text = _editing is null ? "" : CommandLineText.Join(_editing.Arguments);
        _cwdBox.Text = _editing?.WorkingDirectory ?? "";
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (_editing is null)
        {
            MessageBox.Show(this, "編集するプロファイルをリストから選択するか、「追加」してください。", "TileTerm");
            return;
        }

        _editing.Name = string.IsNullOrWhiteSpace(_nameBox.Text) ? "(名称未設定)" : _nameBox.Text.Trim();
        _editing.Executable = _exeBox.Text.Trim();
        _editing.Arguments = CommandLineText.Split(_argsBox.Text);
        _editing.WorkingDirectory = string.IsNullOrWhiteSpace(_cwdBox.Text) ? null : _cwdBox.Text.Trim();

        _store.Save();
        Refresh();
    }

    private void Refresh()
    {
        var selected = _editing;
        _list.ItemsSource = null;
        _list.ItemsSource = _store.Profiles;
        if (selected is not null && _store.Profiles.Contains(selected))
            _list.SelectedItem = selected;
    }
}
