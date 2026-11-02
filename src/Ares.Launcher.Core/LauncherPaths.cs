using System.Text.Json;

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

/// <summary>User-adjustable launcher settings (auto-detected values when left empty).</summary>
public sealed record LauncherSettings
{
    private static readonly JsonSerializerOptions s_indented = new() { WriteIndented = true };

    public string? GameDirectory { get; init; }
    public string? ProtonPath { get; init; }
    public string? SteamDirectory { get; init; }

    /// <summary>Archive pak key; with <see cref="ContainerKey"/>, used instead of the server's keys.</summary>
    public string? PakKey { get; init; }

    /// <summary>Archive container key; with <see cref="PakKey"/>, used instead of the server's keys.</summary>
    public string? ContainerKey { get; init; }

    public static LauncherSettings Load(LauncherPaths paths) =>
        File.Exists(paths.SettingsFile)
            ? JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(paths.SettingsFile)) ?? new LauncherSettings()
            : new LauncherSettings();

    public void Save(LauncherPaths paths)
    {
        Directory.CreateDirectory(paths.DataDirectory);
        File.WriteAllText(paths.SettingsFile, JsonSerializer.Serialize(this, s_indented));
    }
}
