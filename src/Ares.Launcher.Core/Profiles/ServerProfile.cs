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
