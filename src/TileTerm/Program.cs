using System;
using System.Windows.Threading;
using Velopack;

namespace TileTerm;

/// <summary>
/// The real entry point (replacing the one WPF generates from App.xaml, see the csproj).
///
/// <see cref="VelopackApp.Run"/> has to be the very first thing that happens: when the installer,
/// the updater or the uninstaller launches this exe with a special argument (after install, after an
/// update, before uninstall), Velopack handles it here and exits without ever starting the UI. In a
/// normal launch it returns immediately, and it is harmless when the app was not installed by
/// Velopack at all (a build run from Visual Studio, or plain <c>dotnet run</c>).
///
/// Then <see cref="SingleInstance"/>: if TileTerm is already running, that process opens a new window
/// and this one exits without showing anything.
/// </summary>
public static class Program
{
    [STAThread]
    public static void Main()
    {
        VelopackApp.Build().Run();

        using var instance = SingleInstance.Start();
        if (instance is null) return;

        var app = new App();
        app.InitializeComponent();
        instance.Listen(Dispatcher.CurrentDispatcher, App.OpenNewWindow);
        app.Run();
    }
}
