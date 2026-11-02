using System.Text.RegularExpressions;

namespace Ares.Launcher.Core.Game;

/// <summary>Finds the player's existing ARC Raiders installation and Steam/Proton (read-only).</summary>
public static partial class GameInstall
{
    public const string SteamAppId = "1808500";
    private const string FolderName = "Arc Raiders";
    public const string ExecutableRelative = "PioneerGame/Binaries/Win64/PioneerGame.exe";
    public const string ConfigPakRelative = "PioneerGame/Content/Paks/pakchunk0-WindowsClient.pak";

    public static bool IsValidGameDirectory(string? dir) =>
        !string.IsNullOrWhiteSpace(dir)
        && File.Exists(Path.Combine(dir, ExecutableRelative))
        && File.Exists(Path.Combine(dir, ConfigPakRelative));

    private static IEnumerable<string> SteamRoots()
    {
        if (OperatingSystem.IsWindows())
        {
            foreach (string p in new[]
                     {
                         Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                         Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     })
            {
                yield return Path.Combine(p, "Steam");
            }

            yield break;
        }
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        yield return Path.Combine(home, ".local/share/Steam");
        yield return Path.Combine(home, ".steam/steam");
        yield return Path.Combine(home, ".var/app/com.valvesoftware.Steam/.local/share/Steam");
    }

    /// <summary>Steam library folders listed in libraryfolders.vdf (plus the Steam root itself).</summary>
    private static IEnumerable<string> SteamLibraries(string? steamRoot = null)
    {
        HashSet<string> seen = [];
        foreach (string root in steamRoot is null ? SteamRoots() : [steamRoot])
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            if (seen.Add(Path.GetFullPath(root)))
            {
                yield return root;
            }

            string vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf))
            {
                continue;
            }

            foreach (Match m in LibraryPathPattern().Matches(File.ReadAllText(vdf)))
            {
                string lib = m.Groups[1].Value.Replace(@"\\", @"\");
                if (Directory.Exists(lib) && seen.Add(Path.GetFullPath(lib)))
                {
                    yield return lib;
                }
            }
        }
    }

    public static string? FindGameDirectory(string? steamRoot = null) =>
        SteamLibraries(steamRoot)
            .Select(lib => Path.Combine(lib, "steamapps", "common", FolderName))
            .FirstOrDefault(IsValidGameDirectory);

    /// <summary>Installed Steam build id (appmanifest_1808500.acf "buildid"), or null if unknown.</summary>
    public static string? ReadInstalledBuild(string gameDirectory)
    {
        string? steamapps = Directory.GetParent(gameDirectory)?.Parent?.FullName;
        if (steamapps is null)
        {
            return null;
        }

        string manifest = Path.Combine(steamapps, $"appmanifest_{SteamAppId}.acf");
        if (!File.Exists(manifest))
        {
            return null;
        }

        Match match = BuildIdPattern().Match(File.ReadAllText(manifest));
        return match.Success ? match.Groups[1].Value : null;
    }

    public static string? FindSteamRoot() => SteamRoots().FirstOrDefault(r => Directory.Exists(Path.Combine(r, "steamapps")));

    /// <summary>The game's Steam Proton prefix (compatdata), in the library holding the game.</summary>
    public static string? FindCompatData(string gameDirectory)
    {
        string? library = Directory.GetParent(gameDirectory)?.Parent?.Parent?.FullName;
        if (library is null)
        {
            return null;
        }

        string path = Path.Combine(library, "steamapps", "compatdata", SteamAppId);
        return Directory.Exists(path) ? path : null;
    }

    /// <summary>Picks an installed Proton build, preferring the one that created the game's prefix.</summary>
    public static string? FindProton(string? steamRoot, string? compatData)
    {
        List<string> candidates = [.. SteamLibraries(steamRoot)
            .SelectMany(lib => Directory.Exists(Path.Combine(lib, "steamapps", "common"))
                ? Directory.EnumerateDirectories(Path.Combine(lib, "steamapps", "common"), "Proton*")
                : [])
            .Where(d => File.Exists(Path.Combine(d, "proton")))];
        string? versionFile = compatData is null ? null : Path.Combine(compatData, "version");
        if (versionFile is not null && File.Exists(versionFile))
        {
            string version = File.ReadAllText(versionFile).Trim();
            string? match = candidates.FirstOrDefault(d =>
                File.Exists(Path.Combine(d, "version")) && File.ReadAllText(Path.Combine(d, "version")).Trim() == version);
            if (match is not null)
            {
                return Path.Combine(match, "proton");
            }
        }
        string? preferred = candidates.FirstOrDefault(d => Path.GetFileName(d) == "Proton - Experimental")
                        ?? candidates.OrderByDescending(d => d).FirstOrDefault();
        return preferred is null ? null : Path.Combine(preferred, "proton");
    }

    [GeneratedRegex("\"path\"\\s+\"([^\"]+)\"")]
    private static partial Regex LibraryPathPattern();

    [GeneratedRegex("\"buildid\"\\s+\"(\\d+)\"")]
    private static partial Regex BuildIdPattern();
}
