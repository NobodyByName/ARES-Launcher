using System.Diagnostics;
using Ares.Launcher.Core.Archives;
using Ares.Launcher.Core.Config;
using Ares.Launcher.Core.Profiles;
using Ares.Launcher.Core.Servers;

namespace Ares.Launcher.Core.Game;

/// <summary>
/// Builds a profile's bootstrap config from the player's own installation and starts the original
/// game executable with <c>-textconfig -IniBootstrap=&lt;file&gt;</c>. Nothing in the game install,
/// Steam settings or process memory is changed.
/// </summary>
public sealed class GameStarter(ProfileStore store, ArchiveKeysClient archiveKeys)
{
    private const string ConfigFileName = "server.bin";

    /// <summary>Writes the profile's server.bin (regenerated every launch, so game updates are picked up).</summary>
    private string PrepareConfig(ServerProfile profile, string gameDirectory, ArchiveKeys keys)
    {
        if (!GameInstall.IsValidGameDirectory(gameDirectory))
        {
            throw new InvalidOperationException("ARC Raiders installation not found.");
        }

        PakArchive pak;
        try
        {
            pak = new PakArchive(Path.Combine(gameDirectory, GameInstall.ConfigPakRelative), keys);
        }
        catch (Exception e) when (e is InvalidDataException or ArgumentOutOfRangeException)
        {
            throw new InvalidOperationException(
                "The archive keys do not open your game files: the game may have updated, or the keys are wrong.", e);
        }

        byte[] source = pak.Extract("../../../PioneerGame/Config/BinaryConfig.ini");
        byte[] bin = BinaryConfig.BuildBootstrap(source, profile.Origin, profile.Origin);
        string dir = store.DirectoryFor(profile);
        Directory.CreateDirectory(dir);
        string target = Path.Combine(dir, ConfigFileName);
        string temp = target + ".tmp";
        File.WriteAllBytes(temp, bin);
        File.Move(temp, target, overwrite: true);
        return target;
    }

    /// <summary>The keys from Settings when both are set, otherwise the server's.</summary>
    private async Task<ArchiveKeys> KeysForAsync(ServerProfile profile, LauncherSettings settings, CancellationToken cancel)
    {
        if (settings is not { PakKey: { Length: > 0 } pakKey, ContainerKey: { Length: > 0 } containerKey })
        {
            return await archiveKeys.FetchAsync(profile, cancel);
        }

        try
        {
            return ArchiveKeys.Parse(pakKey, containerKey);
        }
        catch (FormatException e)
        {
            throw new InvalidOperationException($"The archive keys in Settings are invalid: {e.Message}", e);
        }
    }

    public async Task<Process> StartAsync(ServerProfile profile, LauncherSettings settings, CancellationToken cancel = default)
    {
        if (profile.Settings != SettingsMode.Shared)
        {
            throw new NotSupportedException("Separate settings per profile are not implemented yet; use Shared.");
        }

        string gameDir = settings.GameDirectory is { Length: > 0 } g ? g : GameInstall.FindGameDirectory(settings.SteamDirectory)
            ?? throw new InvalidOperationException("ARC Raiders installation not found. Set the game folder in Settings.");
        ArchiveKeys keys = await KeysForAsync(profile, settings, cancel);
        string config = PrepareConfig(profile, gameDir, keys);
        string exe = Path.Combine(gameDir, GameInstall.ExecutableRelative);
        ProcessStartInfo info;
        if (OperatingSystem.IsWindows())
        {
            info = new ProcessStartInfo(exe) { WorkingDirectory = Path.GetDirectoryName(exe)! };
            info.ArgumentList.Add("-textconfig");
            info.ArgumentList.Add($"-IniBootstrap={config}");
        }
        else
        {
            string steam = settings.SteamDirectory ?? GameInstall.FindSteamRoot()
                ?? throw new InvalidOperationException("Steam installation not found.");
            string compat = GameInstall.FindCompatData(gameDir)
                ?? throw new InvalidOperationException("The game's Proton prefix was not found. Launch ARC Raiders once from Steam first.");
            string proton = settings.ProtonPath is { Length: > 0 } p ? p : GameInstall.FindProton(steam, compat)
                ?? throw new InvalidOperationException("No Proton installation found. Set it in Settings.");
            info = new ProcessStartInfo(proton) { WorkingDirectory = Path.GetDirectoryName(exe)! };
            info.ArgumentList.Add("run");
            info.ArgumentList.Add(exe);
            info.ArgumentList.Add("-textconfig");
            info.ArgumentList.Add($"-IniBootstrap=Z:{config.Replace('/', '\\')}");
            info.Environment["STEAM_COMPAT_DATA_PATH"] = compat;
            info.Environment["STEAM_COMPAT_CLIENT_INSTALL_PATH"] = steam;
            // Never inherit injection-style variables into the game process.
            foreach (string v in new[] { "LD_PRELOAD", "LD_AUDIT", "WINEDLLOVERRIDES", "WINEDLLPATH" })
            {
                info.Environment.Remove(v);
            }
        }
        info.Environment["SteamAppId"] = GameInstall.SteamAppId;
        info.Environment["SteamGameId"] = GameInstall.SteamAppId;
        info.UseShellExecute = false;
        Process process = Process.Start(info) ?? throw new InvalidOperationException("The game failed to start.");
        store.Update(profile with { LastPlayed = DateTimeOffset.UtcNow });
        return process;
    }
}
