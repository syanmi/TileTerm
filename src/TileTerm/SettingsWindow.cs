using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
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
/// Modeled on the JetBrains (CLion) settings dialog: a flush category list |
/// list-with-toolbar | form, with draggable splitters between them; form rows put
/// the label to the left of a compact (24px, nearly square) field with any
/// explanation as small muted text underneath; and a bottom bar with
/// OK / キャンセル / 適用 — 適用 (apply) is only enabled while the form has unsaved
/// edits, OK applies them and closes, キャンセル just closes.
/// </summary>
public sealed class SettingsWindow : Window
{
    private const double LabelColumnWidth = 112;

    private readonly ProfileStore _store;
    private readonly ListBox _list = Theme.ListBox();
    private readonly TextBox _nameBox = Theme.TextBox();
    private readonly TextBox _iconBox = Theme.TextBox();
    private readonly TextBox _exeBox = Theme.TextBox();
    private readonly TextBox _argsBox = Theme.TextBox();
    private readonly TextBox _cwdBox = Theme.TextBox();
    private readonly Button _defaultButton = Theme.Button("既定のプロンプトにする");
    private readonly Button _applyButton = Theme.Button("適用");
    private readonly CheckBox _favoriteCheck = Theme.CheckBox("お気に入りに登録").Also(c =>
    {
        c.VerticalAlignment = VerticalAlignment.Center;
        c.Margin = new Thickness(14, 0, 0, 0);
        c.ToolTip = "タイトルバーにこのプロンプトのボタンが表示されます";
    });
    private ProfileDefinition? _editing;
    private bool _suppressToggleEvents;
    private (string, string, string, string, string) _loadedFields;

    public SettingsWindow(ProfileStore store)
    {
        _store = store;
        Title = "TileTerm - 設定";
        Width = 920;
        Height = 640;
        MinWidth = 700;
        MinHeight = 460;
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

        foreach (var box in new[] { _nameBox, _iconBox, _exeBox, _argsBox, _cwdBox })
            box.TextChanged += (_, _) => UpdateApplyState();

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { e.Handled = true; Close(); }
        };

        Refresh();

        // Open with something selected (the default prompt), like the JetBrains dialog opens
        // with its first entry selected, rather than an empty form.
        if ((_store.GetDefaultProfile() ?? _store.Profiles.FirstOrDefault()) is { } initial)
            SelectRowFor(initial);
    }

    private Grid BuildMainArea()
    {
        var main = new Grid();
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210), MinWidth = 130 });
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 380 });

        // Category sidebar — just one category today; structured so more can be added later.
        // Rows run flush edge to edge, like the JetBrains settings tree.
        var categoryList = Theme.ListBox();
        categoryList.BorderThickness = new Thickness(0);
        categoryList.Background = Theme.BgList;
        categoryList.Items.Add("プロンプト");
        categoryList.SelectedIndex = 0;
        Grid.SetColumn(categoryList, 0);
        main.Children.Add(categoryList);

        var splitter1 = Theme.Splitter(vertical: true);
        Grid.SetColumn(splitter1, 1);
        main.Children.Add(splitter1);

        var rightSide = new Grid();
        rightSide.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        rightSide.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new TextBlock
        {
            Text = "プロンプト", Foreground = Theme.Fg, FontSize = 13, FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(16, 12, 16, 10),
        };
        Grid.SetRow(header, 0);
        rightSide.Children.Add(header);

        var body = BuildBody();
        Grid.SetRow(body, 1);
        rightSide.Children.Add(body);

        Grid.SetColumn(rightSide, 2);
        main.Children.Add(rightSide);

        return main;
    }

    private Grid BuildBody()
    {
        var body = new Grid { Margin = new Thickness(16, 0, 16, 14) };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200), MinWidth = 140 });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 260 });

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

    /// <summary>The editor: each field on its own row with the label to its left and an
    /// optional small muted hint underneath (JetBrains-style), then the 既定/お気に入り controls.</summary>
    private ScrollViewer BuildForm()
    {
        var form = new Grid { Margin = new Thickness(14, 0, 0, 0) };
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LabelColumnWidth) });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        AddFormRow(form, "名前", _nameBox, null);
        AddFormRow(form, "表示アイコン", _iconBox, "絵文字や \"PS\" のような短い文字列。空欄なら名前の頭文字");
        AddFormRow(form, "実行ファイル", WithBrowseButton(_exeBox), ".exe / .cmd / .bat");
        AddFormRow(form, "引数", _argsBox, "スペース区切り。空白を含む場合は \"...\" で囲む");
        AddFormRow(form, "作業ディレクトリ", _cwdBox, "空欄ならユーザーフォルダ");

        var optionsRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        optionsRow.Children.Add(_defaultButton);
        _favoriteCheck.Checked += (_, _) => OnFavoriteToggled(true);
        _favoriteCheck.Unchecked += (_, _) => OnFavoriteToggled(false);
        optionsRow.Children.Add(_favoriteCheck);
        AddFormRow(form, "", optionsRow, null);

        _defaultButton.Click += (_, _) =>
        {
            if (_editing is not null) { _store.SetDefault(_editing); Refresh(); }
        };

        return new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 4, 0) };
    }

    private static void AddFormRow(Grid form, string label, UIElement field, string? hint)
    {
        int row = form.RowDefinitions.Count;
        form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var labelBlock = Theme.Label(label);
        labelBlock.Height = Theme.ControlHeight;
        labelBlock.VerticalAlignment = VerticalAlignment.Top;
        labelBlock.Padding = new Thickness(0, 4, 0, 0);
        Grid.SetRow(labelBlock, row);
        Grid.SetColumn(labelBlock, 0);
        form.Children.Add(labelBlock);

        var cell = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        cell.Children.Add(field);
        if (hint is not null)
        {
            var hintBlock = Theme.Hint(hint);
            hintBlock.Margin = new Thickness(1, 3, 0, 0);
            cell.Children.Add(hintBlock);
        }
        Grid.SetRow(cell, row);
        Grid.SetColumn(cell, 1);
        form.Children.Add(cell);
    }

    /// <summary>The field followed by a small folder button that opens a file picker — the
    /// JetBrains way of offering "browse" without a wide text button.</summary>
    private UIElement WithBrowseButton(TextBox box)
    {
        var browseButton = Theme.Button(Icons.Folder());
        browseButton.Padding = new Thickness(0);
        browseButton.Width = 30;
        browseButton.Margin = new Thickness(4, 0, 0, 0);
        browseButton.ToolTip = "参照...";
        AutomationProperties.SetName(browseButton, "参照...");
        browseButton.Click += (_, _) => BrowseExe(box);

        var row = new DockPanel();
        DockPanel.SetDock(browseButton, Dock.Right);
        row.Children.Add(browseButton);
        row.Children.Add(box);
        return row;
    }

    private Border BuildActionBar()
    {
        var bar = new Border
        {
            BorderBrush = Theme.BorderCol,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(16, 10, 16, 10),
        };

        var panel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };

        var okButton = Theme.PrimaryButton("OK");
        okButton.MinWidth = 76;
        okButton.IsDefault = true;
        okButton.Click += (_, _) =>
        {
            if (_applyButton.IsEnabled) ApplyEdits();
            Close();
        };
        panel.Children.Add(okButton);

        var cancelButton = Theme.Button("キャンセル");
        cancelButton.MinWidth = 76;
        cancelButton.Margin = new Thickness(8, 0, 0, 0);
        cancelButton.Click += (_, _) => Close();
        panel.Children.Add(cancelButton);

        _applyButton.MinWidth = 76;
        _applyButton.Margin = new Thickness(8, 0, 0, 0);
        _applyButton.Click += (_, _) => ApplyEdits();
        panel.Children.Add(_applyButton);

        bar.Child = panel;
        return bar;
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

    private (string, string, string, string, string) CurrentFields() =>
        (_nameBox.Text, _iconBox.Text, _exeBox.Text, _argsBox.Text, _cwdBox.Text);

    /// <summary>適用 is only meaningful while the form differs from what was loaded into it.</summary>
    private void UpdateApplyState() =>
        _applyButton.IsEnabled = _editing is not null && CurrentFields() != _loadedFields;

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

        _loadedFields = CurrentFields();
        UpdateApplyState();
    }

    private void OnFavoriteToggled(bool isFavorite)
    {
        if (_suppressToggleEvents || _editing is null) return;
        _store.SetFavorite(_editing, isFavorite);
        Refresh();
    }

    private void ApplyEdits()
    {
        if (_editing is null) return;

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
