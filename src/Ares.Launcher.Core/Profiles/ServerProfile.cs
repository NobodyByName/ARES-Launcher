using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Ares.Launcher.Core.Accounts;

namespace Ares.Launcher.Core.Profiles;

public enum SettingsMode
{
    /// <summary>Use the game's normal user folder: options and key bindings are shared with official play.</summary>
    Shared,
    /// <summary>Separate settings for this profile, seeded from the official settings (not implemented yet).</summary>
    OfficialCopy,
    /// <summary>Separate settings for this profile, starting from game defaults (not implemented yet).</summary>
    Default,
}

/// <summary>One private server the player can launch into. The address ends up in the generated bootstrap config.</summary>
public sealed partial record ServerProfile
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Host { get; init; }
    public int Port { get; init; } = DefaultPort;
    public SettingsMode Settings { get; init; } = SettingsMode.Shared;
    public DateTimeOffset? LastPlayed { get; init; }

    /// <summary>The account last signed in with on this server; the password itself is never stored (see <see cref="SavedSignIn"/>).</summary>
    public string? AccountName { get; init; }

    public const int DefaultPort = 18080;

    [JsonIgnore] public string Origin => $"http://{(Host.Contains(':') ? $"[{Host}]" : Host)}:{Port}";

    /// <summary>Parses "host", "host:port", "[ipv6]:port" or "http://host:port". Default host is 127.0.0.1.</summary>
    public static (string Host, int Port) ParseAddress(string? text)
    {
        text = (text ?? "").Trim();
        if (text.Length == 0)
        {
            return ("127.0.0.1", DefaultPort);
        }

        if (!text.Contains("://"))
        {
            text = "http://" + text;
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out Uri? uri) || uri.Scheme != "http"
            || (uri.AbsolutePath != "/" && uri.AbsolutePath != "") || uri.Query.Length > 0 || uri.UserInfo.Length > 0)
        {
            throw new FormatException("Enter a server address like 192.0.2.10, play.example.com or host:port.");
        }

        string host = uri.IdnHost.Trim('[', ']');
        return HostPattern().IsMatch(host)
            ? (host, uri.IsDefaultPort && !text.Contains(":" + uri.Port) ? DefaultPort : uri.Port)
            : throw new FormatException("Invalid server host name.");
    }

    [GeneratedRegex(@"^[A-Za-z0-9.\-:]{1,253}$")]
    private static partial Regex HostPattern();
}

/// <summary>Stores profiles as one JSON file plus a folder per profile (generated config lives there).</summary>
public sealed partial class ProfileStore(LauncherPaths paths)
{
    private static readonly JsonSerializerOptions s_json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _file = Path.Combine(paths.DataDirectory, "profiles.json");

    private string ProfilesDirectory { get; } = Path.Combine(paths.DataDirectory, "profiles");

    public List<ServerProfile> Load()
    {
        if (!File.Exists(_file))
        {
            return [];
        }

        return JsonSerializer.Deserialize<List<ServerProfile>>(File.ReadAllText(_file), s_json) ?? [];
    }

    private void Save(List<ServerProfile> profiles)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        string temp = _file + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(profiles, s_json));
        File.Move(temp, _file, overwrite: true);
    }

    public string DirectoryFor(ServerProfile profile) => Path.Combine(ProfilesDirectory, profile.Id);

    private string SignInFile(ServerProfile profile) => Path.Combine(DirectoryFor(profile), "sign-in.json");

    /// <summary>The "Remember me" sign-in for this server, or null when there is none or the file is unreadable.</summary>
    public SavedSignIn? LoadSignIn(ServerProfile profile)
    {
        if (!File.Exists(SignInFile(profile)))
        {
            return null;
        }

        try
        {
            SavedSignIn? saved = JsonSerializer.Deserialize<SavedSignIn>(File.ReadAllText(SignInFile(profile)));
            return saved is { Name.Length: > 0, Salt.Length: > 0, Iterations: > 0 }
                && Convert.TryFromBase64String(saved.Key, new byte[SrpClient.StretchedBytes], out int length) && length == SrpClient.StretchedBytes
                ? saved
                : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Saves the sign-in so this server's Play works without the password (readable by the current user only).</summary>
    public void SaveSignIn(ServerProfile profile, SavedSignIn saved)
    {
        string file = SignInFile(profile);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        string temp = file + ".tmp";
        File.Delete(temp);
        FileStreamOptions options = new() { Mode = FileMode.CreateNew, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        using (StreamWriter writer = new(temp, options))
        {
            writer.Write(JsonSerializer.Serialize(saved));
        }

        File.Move(temp, file, overwrite: true);
    }

    public void ForgetSignIn(ServerProfile profile)
    {
        if (File.Exists(SignInFile(profile)))
        {
            File.Delete(SignInFile(profile));
        }
    }

    public ServerProfile Add(string name, string address, SettingsMode settings)
    {
        (string? host, int port) = ServerProfile.ParseAddress(address);
        List<ServerProfile> profiles = Load();
        name = string.IsNullOrWhiteSpace(name) ? $"{host}:{port}" : name.Trim();
        string baseId = NonIdCharacters().Replace(name.ToLowerInvariant(), "-").Trim('-');
        if (baseId.Length == 0)
        {
            baseId = "server";
        }

        string id = baseId;
        for (int i = 2; profiles.Any(p => p.Id == id); i++)
        {
            id = $"{baseId}-{i}";
        }

        ServerProfile profile = new()
        {
            Id = id,
            Name = name,
            Host = host,
            Port = port,
            Settings = settings,
        };
        profiles.Add(profile);
        Save(profiles);
        Directory.CreateDirectory(DirectoryFor(profile));
        return profile;
    }

    public void Update(ServerProfile profile)
    {
        List<ServerProfile> profiles = Load();
        int index = profiles.FindIndex(p => p.Id == profile.Id);
        if (index < 0)
        {
            throw new KeyNotFoundException(profile.Id);
        }

        profiles[index] = profile;
        Save(profiles);
    }

    /// <summary>Removes the profile and only its own folder; nothing else is touched.</summary>
    public void Remove(string id)
    {
        List<ServerProfile> profiles = Load();
        ServerProfile? profile = profiles.FirstOrDefault(p => p.Id == id);
        if (profile is null)
        {
            return;
        }

        profiles.Remove(profile);
        Save(profiles);
        string dir = DirectoryFor(profile);
        if (Directory.Exists(dir) && Path.GetFullPath(dir).StartsWith(Path.GetFullPath(ProfilesDirectory)))
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonIdCharacters();
}
