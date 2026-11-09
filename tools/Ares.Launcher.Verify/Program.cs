// Verifies the launcher core against a stock install of the supported game build.
// Usage: Ares.Launcher.Verify --keys <archive keys file> [<game directory>]
// The keys file has the format ARES-Host operators use; the game directory defaults to the
// auto-detected Steam install.
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using Ares.Launcher.Core;
using Ares.Launcher.Core.Archives;
using Ares.Launcher.Core.Config;
using Ares.Launcher.Core.Game;
using Ares.Launcher.Core.Profiles;

const string SupportedBuild = "26439197";
const string ConfigSha256 = "3b4aec6cbeadd8caf890dc10a56e8f4326303249fc05c52d06747dd97a480793";
const string LoopbackBootstrapSha256 = "0e0b59bec0817fe291d3f146a0fc0e8dcc9e2614dbf8a91a2e94ea9e8190e7d0";

if (args.Length is < 2 or > 3 || args[0] != "--keys")
{
    Fail("Usage: Ares.Launcher.Verify --keys <archive keys file> [<game directory>]");
}

ArchiveKeys keys = ReadKeys(args[1]);

string? game = args.Length > 2 ? args[2] : GameInstall.FindGameDirectory();
if (game is null)
{
    Fail($"Game not found. Supply the directory of a stock ARC Raiders install, Steam build {SupportedBuild}.");
}

if (!GameInstall.IsValidGameDirectory(game))
{
    Fail($"Not an ARC Raiders install: {game}");
}

string? build = GameInstall.ReadInstalledBuild(game);
if (build != SupportedBuild)
{
    Fail($"Installed Steam build is {build ?? "unknown"}; the verifier needs supported build {SupportedBuild}.");
}

TheiaCrypto.SelfTest();
Check($"launcher version {LauncherVersion.Current} is YYYYMMDDNN", LauncherVersion.Current is { Length: 10 } version && version.All(char.IsAsciiDigit));
CheckKeyFormats();
PakArchive pak = OpenPak(Path.Combine(game, GameInstall.ConfigPakRelative), keys);
byte[] config = pak.Extract("../../../PioneerGame/Config/BinaryConfig.ini");
Check("extracted BinaryConfig.ini matches the stock build", Sha256(config) == ConfigSha256);
string loopback = $"http://127.0.0.1:{ServerProfile.DefaultPort}";
byte[] bin = BinaryConfig.BuildBootstrap(config, loopback, loopback);
Check("loopback server.bin matches the known result", Sha256(bin) == LoopbackBootstrapSha256);
string launchKey = Convert.ToBase64String(Enumerable.Range(0, 32).Select(i => (byte)(255 - i)).ToArray());
BinaryConfig.ParseResult keyed = BinaryConfig.Parse(BinaryConfig.BuildBootstrap(config, loopback, loopback, launchKey));
Check("launch key replaces the client secret in all three layers",
    keyed.Values.Where(v => v is { Branch: "Engine", Section: "EmbarkOnline", Key: "ClientSecret" }).Select(v => v.Text).ToList() is [_, _, _] secrets
    && secrets.All(text => text == launchKey));
foreach ((string? text, string? host, int port) in new[] { ("", "127.0.0.1", ServerProfile.DefaultPort), ("192.0.2.24", "192.0.2.24", ServerProfile.DefaultPort),
             ("play.example.com:7000", "play.example.com", 7000), ("http://198.51.100.5:18081/", "198.51.100.5", 18081), ("[::1]:9000", "::1", 9000) })
{
    Check($"address '{text}' -> {host}:{port}", ServerProfile.ParseAddress(text) == (host, port));
}

foreach (string bad in new[] { "https://example.invalid", "example.invalid/path", "user@host" })
{
    bool rejected = false;
    try
    { ServerProfile.ParseAddress(bad); }
    catch (FormatException) { rejected = true; }
    Check($"address '{bad}' rejected", rejected);
}
Console.WriteLine("all checks passed");
return;

static string Sha256(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));

static PakArchive OpenPak(string path, ArchiveKeys keys)
{
    try
    {
        return new PakArchive(path, keys);
    }
    catch (Exception e) when (e is InvalidDataException or ArgumentOutOfRangeException)
    {
        Fail($"The archive keys do not open the game files ({e.Message}).");
        throw;
    }
}

static ArchiveKeys ReadKeys(string path)
{
    try
    {
        return ArchiveKeys.ParseFile(File.ReadAllText(path));
    }
    catch (Exception e) when (e is IOException or FormatException or UnauthorizedAccessException)
    {
        Fail($"Cannot read the archive keys: {e.Message}");
        throw;
    }
}

// Key parsing with dummy keys: pak key bytes 00..1F, container key words 1..8.
static void CheckKeyFormats()
{
    const string PakKey = "0x000102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1F";
    ArchiveKeys parsed = ArchiveKeys.ParseFile($"# comment\npak-key = {PakKey}\r\ncontainer-key = 0x1, 0x00000002, 3, 4, 5, 6, 7, 8\n");
    Check("keys file parses", parsed.PakKey.SequenceEqual(Enumerable.Range(0, 32).Select(i => (byte)i))
        && parsed.ContainerKey.SequenceEqual([1u, 2u, 3u, 4u, 5u, 6u, 7u, 8u]));
    Check("settings keys parse", ArchiveKeys.Parse(PakKey[2..].ToLowerInvariant(), "[1 2 3 4 5 6 7 8]").ContainerKey[7] == 8);
    foreach ((string pak, string container) in new[] { ("00", "1,2,3,4,5,6,7,8"), (PakKey, "1,2,3"), (PakKey, "1,2,3,4,5,6,7,100000000"), (PakKey[..^1] + "G", "1,2,3,4,5,6,7,8") })
    {
        bool rejected = false;
        try
        { ArchiveKeys.Parse(pak, container); }
        catch (FormatException) { rejected = true; }
        Check($"keys '{pak[..Math.Min(6, pak.Length)]}…' / '{container}' rejected", rejected);
    }
}

[DoesNotReturn]
static void Fail(string message)
{
    Console.Error.WriteLine(message);
    Environment.Exit(1);
}

static void Check(string what, bool ok)
{
    Console.WriteLine((ok ? "ok   " : "FAIL ") + what);
    if (!ok)
    {
        Environment.Exit(1);
    }
}
