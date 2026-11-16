namespace Ares.Launcher.Core;

/// <summary>Where the launcher keeps its own data. Never inside the game installation.</summary>
public sealed class LauncherPaths
{
    public string DataDirectory { get; }
    public string SettingsFile => Path.Combine(DataDirectory, "settings.json");

    public LauncherPaths(string? overrideDirectory = null)
    {
        if (overrideDirectory is not null)
        {
            DataDirectory = overrideDirectory;
            return;
        }
        string root = OperatingSystem.IsWindows()
            ? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
            : Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } xdg && Path.IsPathRooted(xdg)
                ? xdg
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        DataDirectory = Path.Combine(root, "ARES-Launcher");
    }
}
