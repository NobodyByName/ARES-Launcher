using System.Text.Json;

namespace Ares.Launcher.Core;

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
