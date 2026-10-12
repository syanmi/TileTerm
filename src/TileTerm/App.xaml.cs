using System;
using System.Linq;
using System.Windows;

namespace TileTerm;

/// <summary>
/// Interaction logic for App.xaml
///
/// All of TileTerm's windows live in this one process (see <see cref="SingleInstance"/>); the app ends
/// when the last of them closes (the default <see cref="ShutdownMode.OnLastWindowClose"/>).
/// </summary>
public partial class App : Application
{
    /// <summary>Set while every window is being closed to apply an update: the windows then skip their
    /// "close all tiles?" question, which the update's own confirmation already covered.</summary>
    internal static bool ClosingForUpdate { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        // Before base.OnStartup, which creates the main window from StartupUri: the window's
        // XAML refers to the theme brushes through DynamicResource, so they must already exist.
        ThemeManager.Initialize(this);
        base.OnStartup(e);
    }

    /// <summary>Opens another main window — what launching TileTerm again does while it is already
    /// running. Returns false when the app is already shutting down.</summary>
    internal static bool OpenNewWindow()
    {
        if (Current is not App || Current.Dispatcher.HasShutdownStarted || ClosingForUpdate) return false;

        try
        {
            var window = new MainWindow();
            window.Show();
            window.Activate();
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;   // the last window closed a moment ago and the app is on its way out
        }
    }

    /// <summary>Closes every main window without asking (the caller already did), so the process
    /// exits and the waiting updater can install the new version.</summary>
    internal static void CloseAllForUpdate()
    {
        ClosingForUpdate = true;
        foreach (var window in Current.Windows.OfType<MainWindow>().ToList())
            window.Close();
    }
}
