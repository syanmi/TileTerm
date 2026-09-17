using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TileTerm.Terminal;

namespace TileTerm;

/// <summary>
/// The app's settings dialog. Has a category sidebar on the left (only
/// "プロンプト" exists today, but the structure leaves room for more) and,
/// for that category, the profile list + editor on the right: add/edit/remove
/// profiles, pick which one is the 既定 (default), and mark any number of
/// them as お気に入り (favorites). Backed by <see cref="ProfileStore"/>, which
/// persists to <c>%AppData%\TileTerm\profiles.json</c>.
/// </summary>
public sealed class SettingsWindow : Window
{
    private readonly ProfileStore _store;
    private readonly ListBox _list = new();
    private readonly TextBox _nameBox = Theme.TextBox();
    private readonly TextBox _iconBox = Theme.TextBox();
    private readonly TextBox _exeBox = Theme.TextBox();
    private readonly TextBox _argsBox = Theme.TextBox();
    private readonly TextBox _cwdBox = Theme.TextBox();
    private readonly Button _defaultButton = Theme.Button("既定のプロンプトにする");
    private readonly CheckBox _favoriteCheck = new() { Content = "お気に入りに登録（タイトルバーにボタンが表示されます）", Margin = new Thickness(0, 10, 0, 0) };
    private ProfileDefinition? _editing;
    private bool _suppressToggleEvents;

    public SettingsWindow(ProfileStore store)
    {
        _store = store;
        Title = "TileTerm - 設定";
        Width = 760;
        Height = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Theme.Apply(this);

        var root = new Grid { Margin = new Thickness(10) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Category sidebar — just one category today; structured so more can be added later.
        var categoryList = new ListBox { BorderThickness = new Thickness(0), Background = Theme.BgWindow, Foreground = Theme.Fg };
        categoryList.Items.Add("プロンプト");
        categoryList.SelectedIndex = 0;
        Grid.SetColumn(categoryList, 0);
        root.Children.Add(categoryList);

        // Profile list for the "プロンプト" category.
        var leftPanel = new DockPanel();
        var listButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        var addButton = Theme.Button("追加");
        addButton.Margin = new Thickness(0, 0, 4, 0);
        var removeButton = Theme.Button("削除");
        listButtons.Children.Add(addButton);
        listButtons.Children.Add(removeButton);
        DockPanel.SetDock(listButtons, Dock.Bottom);
        leftPanel.Children.Add(listButtons);
        _list.Background = Theme.BgList;
        _list.Foreground = Theme.Fg;
        _list.BorderBrush = Theme.BorderCol;
        leftPanel.Children.Add(_list);
        Grid.SetColumn(leftPanel, 2);
        root.Children.Add(leftPanel);

        // Editor for the selected profile.
        var form = new StackPanel();
        form.Children.Add(LabeledBox("名前", _nameBox));
        form.Children.Add(LabeledBox("表示アイコン（絵文字や \"PS\" のような短い文字列。空欄なら名前の頭文字）", _iconBox));
        form.Children.Add(LabeledBoxWithBrowse("実行ファイル (.exe / .cmd / .bat)", _exeBox));
        form.Children.Add(LabeledBox("引数（スペース区切り。空白を含む場合は \"...\" で囲む）", _argsBox));
        form.Children.Add(LabeledBox("作業ディレクトリ（空欄ならユーザーフォルダ）", _cwdBox));

        var saveButton = Theme.Button("保存");
        saveButton.Margin = new Thickness(0, 4, 0, 0);
        saveButton.HorizontalAlignment = HorizontalAlignment.Left;
        saveButton.Click += OnSave;
        form.Children.Add(saveButton);

        form.Children.Add(new Separator
        {
            Margin = new Thickness(0, 14, 0, 10), Background = Theme.BorderCol, BorderBrush = Theme.BorderCol,
        });

        _defaultButton.Padding = new Thickness(8, 3, 8, 3);
        _defaultButton.HorizontalAlignment = HorizontalAlignment.Left;
        _defaultButton.Click += (_, _) =>
        {
            if (_editing is not null) { _store.SetDefault(_editing); Refresh(); }
        };
        form.Children.Add(_defaultButton);

        _favoriteCheck.Foreground = Theme.Fg;
        _favoriteCheck.Checked += (_, _) => OnFavoriteToggled(true);
        _favoriteCheck.Unchecked += (_, _) => OnFavoriteToggled(false);
        form.Children.Add(_favoriteCheck);

        var closeButton = Theme.Button("閉じる");
        closeButton.Margin = new Thickness(0, 18, 0, 0);
        closeButton.HorizontalAlignment = HorizontalAlignment.Left;
        closeButton.Click += (_, _) => Close();
        form.Children.Add(closeButton);

        Grid.SetColumn(form, 4);
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
        panel.Children.Add(new TextBlock { Text = label, Foreground = Theme.Fg, Margin = new Thickness(0, 0, 0, 2), TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(box);
        return panel;
    }

    private UIElement LabeledBoxWithBrowse(string label, TextBox box)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        panel.Children.Add(new TextBlock { Text = label, Foreground = Theme.Fg, Margin = new Thickness(0, 0, 0, 2) });

        var row = new DockPanel();
        var browseButton = Theme.Button("参照...");
        browseButton.Padding = new Thickness(6, 2, 6, 2);
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
        var profile = new ProfileDefinition { Name = "新しいプロンプト" };
        _store.AddProfile(profile);
        Refresh();
        SelectRowFor(profile);
    }

    private void RemoveSelected()
    {
        if ((_list.SelectedItem as ProfileRow)?.Profile is not { } profile) return;
        _store.RemoveProfile(profile);
        _editing = null;
        Refresh();
    }

    private void LoadSelected()
    {
        _editing = (_list.SelectedItem as ProfileRow)?.Profile;

        _suppressToggleEvents = true;
        _nameBox.Text = _editing?.Name ?? "";
        _iconBox.Text = _editing?.Icon ?? "";
        _exeBox.Text = _editing?.Executable ?? "";
        _argsBox.Text = _editing is null ? "" : CommandLineText.Join(_editing.Arguments);
        _cwdBox.Text = _editing?.WorkingDirectory ?? "";

        bool hasSelection = _editing is not null;
        _defaultButton.IsEnabled = hasSelection && _store.GetDefaultProfile() != _editing;
        _favoriteCheck.IsEnabled = hasSelection;
        _favoriteCheck.IsChecked = hasSelection && _store.IsFavorite(_editing!);
        _suppressToggleEvents = false;
    }

    private void OnFavoriteToggled(bool isFavorite)
    {
        if (_suppressToggleEvents || _editing is null) return;
        _store.SetFavorite(_editing, isFavorite);
        Refresh();
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (_editing is null)
        {
            MessageDialog.Show(this, "編集するプロンプトをリストから選択するか、「追加」してください。", "TileTerm");
            return;
        }

        _editing.Name = string.IsNullOrWhiteSpace(_nameBox.Text) ? "(名称未設定)" : _nameBox.Text.Trim();
        _editing.Icon = _iconBox.Text.Trim();
        _editing.Executable = _exeBox.Text.Trim();
        _editing.Arguments = CommandLineText.Split(_argsBox.Text);
        _editing.WorkingDirectory = string.IsNullOrWhiteSpace(_cwdBox.Text) ? null : _cwdBox.Text.Trim();

        _store.Save();
        Refresh();
    }

    private void SelectRowFor(ProfileDefinition profile)
    {
        if (_list.ItemsSource is System.Collections.Generic.IEnumerable<ProfileRow> rows)
            _list.SelectedItem = rows.FirstOrDefault(r => r.Profile == profile);
    }

    private void Refresh()
    {
        var selected = _editing;
        var rows = _store.Profiles.Select(p => new ProfileRow(p, FormatRow(p))).ToList();
        _list.ItemsSource = rows;

        if (selected is not null)
            _list.SelectedItem = rows.FirstOrDefault(r => r.Profile == selected);

        LoadSelected();
    }

    private string FormatRow(ProfileDefinition p)
    {
        string star = p.Id == _store.DefaultProfileId ? "⭐" : "・";
        string heart = _store.IsFavorite(p) ? " ♥" : "";
        return $"{star} {p.DisplayIcon()}  {p.Name}{heart}";
    }

    private sealed record ProfileRow(ProfileDefinition Profile, string Display)
    {
        public override string ToString() => Display;
    }
}
