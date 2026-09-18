using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using TileTerm.Terminal;

namespace TileTerm;

/// <summary>
/// The app's settings dialog. Has a category sidebar on the left (only
/// "プロンプト" exists today, but the structure leaves room for more) and,
/// for that category, the profile list + editor on the right: add/edit/remove
/// profiles, pick which one is the 既定 (default), and mark any number of
/// them as お気に入り (favorites). Backed by <see cref="ProfileStore"/>, which
/// persists to <c>%AppData%\TileTerm\profiles.json</c>.
///
/// Laid out like a typical IDE settings dialog (VSCode/JetBrains): category
/// tree | list-with-toolbar | editor form, with draggable splitters between
/// each (not fixed dividers — a static-width sidebar next to a resizable
/// editor form reads as un-idiomatic for this kind of dialog), and a fixed
/// action bar pinned to the bottom of the whole window rather than a Save
/// button buried mid-form.
/// </summary>
public sealed class SettingsWindow : Window
{
    private readonly ProfileStore _store;
    private readonly ListBox _list = Theme.ListBox();
    private readonly TextBox _nameBox = Theme.TextBox();
    private readonly TextBox _iconBox = Theme.TextBox();
    private readonly TextBox _exeBox = Theme.TextBox();
    private readonly TextBox _argsBox = Theme.TextBox();
    private readonly TextBox _cwdBox = Theme.TextBox();
    private readonly Button _defaultButton = Theme.Button("既定のプロンプトにする");
    private readonly CheckBox _favoriteCheck = new()
    {
        Content = "お気に入りに登録", Foreground = Theme.Fg, VerticalAlignment = VerticalAlignment.Center,
        ToolTip = "タイトルバーにこのプロンプトのボタンが表示されます",
    };
    private ProfileDefinition? _editing;
    private bool _suppressToggleEvents;

    public SettingsWindow(ProfileStore store)
    {
        _store = store;
        Title = "TileTerm - 設定";
        Width = 740;
        Height = 440;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Theme.Apply(this);

        var outer = new Grid();
        outer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var mainArea = BuildMainArea();
        Grid.SetRow(mainArea, 0);
        outer.Children.Add(mainArea);

        var actionBar = BuildActionBar();
        Grid.SetRow(actionBar, 1);
        outer.Children.Add(actionBar);

        Content = outer;
        Refresh();
    }

    private Grid BuildMainArea()
    {
        var main = new Grid { Margin = new Thickness(0) };
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160), MinWidth = 110 });
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 300 });

        // Category sidebar — just one category today; structured so more can be added later.
        var categoryList = Theme.ListBox();
        categoryList.BorderThickness = new Thickness(0);
        categoryList.Background = Theme.BgWindow;
        categoryList.Margin = new Thickness(6);
        categoryList.Items.Add("プロンプト");
        categoryList.SelectedIndex = 0;
        Grid.SetColumn(categoryList, 0);
        main.Children.Add(categoryList);

        var splitter1 = Theme.Splitter(vertical: true);
        Grid.SetColumn(splitter1, 1);
        main.Children.Add(splitter1);

        var rightSide = new Grid();
        rightSide.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        rightSide.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        rightSide.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new TextBlock
        {
            Text = "プロンプト", Foreground = Theme.Fg, FontSize = 13, FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(14, 10, 14, 8),
        };
        Grid.SetRow(header, 0);
        rightSide.Children.Add(header);

        var hDivider = Theme.Divider(vertical: false);
        Grid.SetRow(hDivider, 1);
        rightSide.Children.Add(hDivider);

        var body = BuildBody();
        Grid.SetRow(body, 2);
        rightSide.Children.Add(body);

        Grid.SetColumn(rightSide, 2);
        main.Children.Add(rightSide);

        return main;
    }

    private Grid BuildBody()
    {
        var body = new Grid { Margin = new Thickness(14, 10, 14, 10) };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220), MinWidth = 140 });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 220 });

        // Profile list, with a small +/- toolbar above it.
        var listPanel = new DockPanel();

        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        var addButton = Theme.IconButton(Icons.Plus());
        addButton.ToolTip = "プロンプトを追加";
        AutomationProperties.SetName(addButton, "プロンプトを追加");
        var removeButton = Theme.IconButton(Icons.Minus());
        removeButton.ToolTip = "選択したプロンプトを削除";
        AutomationProperties.SetName(removeButton, "選択したプロンプトを削除");
        toolbar.Children.Add(addButton);
        toolbar.Children.Add(removeButton);
        DockPanel.SetDock(toolbar, Dock.Top);
        listPanel.Children.Add(toolbar);

        _list.ItemTemplate = BuildProfileRowTemplate();
        listPanel.Children.Add(_list);

        Grid.SetColumn(listPanel, 0);
        body.Children.Add(listPanel);

        var splitter2 = Theme.Splitter(vertical: true);
        Grid.SetColumn(splitter2, 1);
        body.Children.Add(splitter2);

        var form = BuildForm();
        Grid.SetColumn(form, 2);
        body.Children.Add(form);

        addButton.Click += (_, _) => AddNew();
        removeButton.Click += (_, _) => RemoveSelected();
        _list.SelectionChanged += (_, _) => LoadSelected();

        return body;
    }

    /// <summary>
    /// One row: profile name left-aligned (filling remaining space, ellipsized if long),
    /// with the 既定/お気に入り status shown as right-aligned glyphs — an outline glyph
    /// when off, a filled one when on (☆→★, ♡→♥), rather than mixing in a letter marker.
    /// </summary>
    private static DataTemplate BuildProfileRowTemplate()
    {
        var starGlyph = new BoolToValueConverter { WhenTrue = "★", WhenFalse = "☆" };
        var starBrush = new BoolToValueConverter { WhenTrue = Theme.GoldStar, WhenFalse = Theme.FgMuted };
        var heartGlyph = new BoolToValueConverter { WhenTrue = "♥", WhenFalse = "♡" };
        var heartBrush = new BoolToValueConverter { WhenTrue = Theme.Favorite, WhenFalse = Theme.FgMuted };

        var dock = new FrameworkElementFactory(typeof(DockPanel));
        dock.SetValue(DockPanel.LastChildFillProperty, true);

        var rightPanel = new FrameworkElementFactory(typeof(StackPanel));
        rightPanel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        rightPanel.SetValue(DockPanel.DockProperty, Dock.Right);
        rightPanel.SetValue(FrameworkElement.MarginProperty, new Thickness(8, 0, 0, 0));

        var starText = new FrameworkElementFactory(typeof(TextBlock));
        starText.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 6, 0));
        starText.SetValue(TextBlock.FontSizeProperty, 12.0);
        starText.SetValue(FrameworkElement.ToolTipProperty, "既定のプロンプト");
        starText.SetBinding(TextBlock.TextProperty, new Binding(nameof(ProfileRow.IsDefault)) { Converter = starGlyph });
        starText.SetBinding(TextBlock.ForegroundProperty, new Binding(nameof(ProfileRow.IsDefault)) { Converter = starBrush });
        rightPanel.AppendChild(starText);

        var heartText = new FrameworkElementFactory(typeof(TextBlock));
        heartText.SetValue(TextBlock.FontSizeProperty, 12.0);
        heartText.SetValue(FrameworkElement.ToolTipProperty, "お気に入り");
        heartText.SetBinding(TextBlock.TextProperty, new Binding(nameof(ProfileRow.IsFavorite)) { Converter = heartGlyph });
        heartText.SetBinding(TextBlock.ForegroundProperty, new Binding(nameof(ProfileRow.IsFavorite)) { Converter = heartBrush });
        rightPanel.AppendChild(heartText);

        dock.AppendChild(rightPanel);

        var nameText = new FrameworkElementFactory(typeof(TextBlock));
        nameText.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        nameText.SetBinding(TextBlock.TextProperty, new Binding(nameof(ProfileRow.IconAndName)));
        dock.AppendChild(nameText);

        return new DataTemplate { VisualTree = dock };
    }

    private ScrollViewer BuildForm()
    {
        var form = new StackPanel();
        form.Children.Add(LabeledBox("名前", _nameBox));
        form.Children.Add(LabeledBox("表示アイコン（絵文字や \"PS\" のような短い文字列。空欄なら名前の頭文字）", _iconBox));
        form.Children.Add(LabeledBoxWithBrowse("実行ファイル (.exe / .cmd / .bat)", _exeBox));
        form.Children.Add(LabeledBox("引数（スペース区切り。空白を含む場合は \"...\" で囲む）", _argsBox));
        form.Children.Add(LabeledBox("作業ディレクトリ（空欄ならユーザーフォルダ）", _cwdBox));

        form.Children.Add(Theme.Divider(vertical: false).Also(d => d.Margin = new Thickness(0, 6, 0, 10)));

        var optionsRow = new StackPanel { Orientation = Orientation.Horizontal };
        optionsRow.Children.Add(_defaultButton);
        _favoriteCheck.Margin = new Thickness(12, 0, 0, 0);
        _favoriteCheck.Checked += (_, _) => OnFavoriteToggled(true);
        _favoriteCheck.Unchecked += (_, _) => OnFavoriteToggled(false);
        optionsRow.Children.Add(_favoriteCheck);
        form.Children.Add(optionsRow);

        _defaultButton.Click += (_, _) =>
        {
            if (_editing is not null) { _store.SetDefault(_editing); Refresh(); }
        };

        return new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 4, 0) };
    }

    private Border BuildActionBar()
    {
        var bar = new Border
        {
            BorderBrush = Theme.BorderCol,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(14, 8, 14, 8),
        };

        var panel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };

        var closeButton = Theme.Button("閉じる");
        closeButton.Click += (_, _) => Close();
        panel.Children.Add(closeButton);

        var saveButton = Theme.PrimaryButton("保存");
        saveButton.Margin = new Thickness(8, 0, 0, 0);
        saveButton.Click += OnSave;
        panel.Children.Add(saveButton);

        bar.Child = panel;
        return bar;
    }

    private static UIElement LabeledBox(string label, TextBox box)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        panel.Children.Add(Theme.Label(label));
        panel.Children.Add(box);
        return panel;
    }

    private UIElement LabeledBoxWithBrowse(string label, TextBox box)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        panel.Children.Add(Theme.Label(label));

        var row = new DockPanel();
        var browseButton = Theme.Button("参照...");
        browseButton.Margin = new Thickness(6, 0, 0, 0);
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
        var rows = _store.Profiles.Select(p => new ProfileRow(
            p, $"{p.DisplayIcon()}  {p.Name}", p.Id == _store.DefaultProfileId, _store.IsFavorite(p))).ToList();
        _list.ItemsSource = rows;

        if (selected is not null)
            _list.SelectedItem = rows.FirstOrDefault(r => r.Profile == selected);

        LoadSelected();
    }

    private sealed record ProfileRow(ProfileDefinition Profile, string IconAndName, bool IsDefault, bool IsFavorite)
    {
        // WPF exposes a ListBoxItem's UI Automation Name via the bound object's
        // ToString() unless told otherwise — without this override it falls back
        // to the record's verbose auto-generated dump.
        public override string ToString()
        {
            var suffix = (IsDefault, IsFavorite) switch
            {
                (true, true) => "（既定・お気に入り）",
                (true, false) => "（既定）",
                (false, true) => "（お気に入り）",
                _ => "",
            };
            return IconAndName + suffix;
        }
    }

    /// <summary>Picks one of two fixed values based on a bound bool — used to turn
    /// IsDefault/IsFavorite into their outline/filled glyph and color.</summary>
    private sealed class BoolToValueConverter : IValueConverter
    {
        public object? WhenTrue { get; init; }
        public object? WhenFalse { get; init; }

        public object? Convert(object value, System.Type targetType, object parameter, CultureInfo culture) =>
            value is true ? WhenTrue : WhenFalse;

        public object ConvertBack(object value, System.Type targetType, object parameter, CultureInfo culture) =>
            throw new System.NotSupportedException();
    }
}

file static class FrameworkElementExtensions
{
    public static T Also<T>(this T element, System.Action<T> configure)
    {
        configure(element);
        return element;
    }
}
