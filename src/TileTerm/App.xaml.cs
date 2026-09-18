using System.Windows;

namespace TileTerm;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Before base.OnStartup, which creates the main window from StartupUri: the window's
        // XAML refers to the theme brushes through DynamicResource, so they must already exist.
        ThemeManager.Initialize(this);
        base.OnStartup(e);
    }
}
