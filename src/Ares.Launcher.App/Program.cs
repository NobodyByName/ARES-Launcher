using Ares.Launcher.Core;
using Ares.Launcher.Core.Accounts;
using Ares.Launcher.Core.Game;
using Ares.Launcher.Core.Profiles;
using Ares.Launcher.Core.Servers;
using Microsoft.Extensions.DependencyInjection;
using Photino.Blazor;

namespace Ares.Launcher.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        PhotinoBlazorAppBuilder builder = PhotinoBlazorAppBuilder.CreateDefault(args);
        builder.Services.AddSingleton(new LauncherPaths());
        builder.Services.AddSingleton<ProfileStore>();
        builder.Services.AddSingleton<GameStarter>();
        builder.Services.AddSingleton<ServerStatusClient>();
        builder.Services.AddSingleton<ArchiveKeysClient>();
        builder.Services.AddSingleton<SignInClient>();
        builder.RootComponents.Add<App>("app");

        PhotinoBlazorApp app = builder.Build();
        app.MainWindow
            .SetTitle("ARES-Launcher")
            .SetSize(520, 760)
            .SetMinSize(400, 560)
            .Center();
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            app.MainWindow.ShowMessage("Error", e.ExceptionObject.ToString());
        app.Run();
    }
}
