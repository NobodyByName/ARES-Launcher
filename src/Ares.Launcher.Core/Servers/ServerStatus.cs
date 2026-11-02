using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Ares.Launcher.Core.Profiles;

namespace Ares.Launcher.Core.Servers;

/// <summary>Public status document served by a private server at <see cref="ServerStatusClient.Path"/>.</summary>
public sealed record ServerStatusDocument
{
    [JsonPropertyName("protocol")] public int Protocol { get; init; }
    [JsonPropertyName("name")] public string? Name { get; init; }
    [JsonPropertyName("motd")] public string? Motd { get; init; }
    [JsonPropertyName("game_build")] public string? GameBuild { get; init; }
    [JsonPropertyName("raids")] public RaidCounts? Raids { get; init; }
    [JsonPropertyName("players_in_raids")] public int PlayersInRaids { get; init; }
    [JsonPropertyName("matchmaking")] public bool Matchmaking { get; init; }

    /// <summary>Concurrent raid sessions: a server hosts many independent raids, not one shared world.</summary>
    public sealed record RaidCounts
    {
        [JsonPropertyName("active")] public int Active { get; init; }
        [JsonPropertyName("capacity")] public int Capacity { get; init; }
    }
}

public enum ServerState { Unknown, Checking, Online, Offline }

public sealed record ServerStatus(ServerState State, ServerStatusDocument? Document = null, int? LatencyMs = null, string? Error = null)
{
    public static readonly ServerStatus Unknown = new(ServerState.Unknown);

    /// <summary>True when the server reports a different game build than the installed one.</summary>
    public bool BuildMismatch(string? installedBuild) =>
        State == ServerState.Online && installedBuild is not null && Document?.GameBuild is { Length: > 0 } b && b != installedBuild;
}

/// <summary>Minecraft-style server list ping over the server's public HTTP status endpoint.</summary>
public sealed class ServerStatusClient : IDisposable
{
    public const string Path = "/ares/v1/status";
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(4) };

    public async Task<ServerStatus> PingAsync(ServerProfile profile, CancellationToken cancel = default)
    {
        Stopwatch watch = Stopwatch.StartNew();
        try
        {
            ServerStatusDocument? document = await _http.GetFromJsonAsync<ServerStatusDocument>(profile.Origin + Path, cancel);
            watch.Stop();
            if (document is null || document.Protocol < 1)
            {
                return new ServerStatus(ServerState.Offline, Error: "Not an ARES-Host.");
            }

            return new ServerStatus(ServerState.Online, document, (int)watch.ElapsedMilliseconds);
        }
        catch (HttpRequestException e) when (e.StatusCode is not null)
        {
            return new ServerStatus(ServerState.Offline, Error: $"Server answered {(int)e.StatusCode} (status page not supported).");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            return new ServerStatus(ServerState.Offline, Error: e is TaskCanceledException ? "No answer (timed out)." : "Unreachable.");
        }
    }

    public void Dispose() => _http.Dispose();
}
