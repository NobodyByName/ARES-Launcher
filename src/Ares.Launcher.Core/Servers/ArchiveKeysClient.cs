using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ares.Launcher.Core.Archives;
using Ares.Launcher.Core.Profiles;

namespace Ares.Launcher.Core.Servers;

/// <summary>Archive keys document served by an ARES-Host at <see cref="ArchiveKeysClient.Path"/>.</summary>
public sealed record ArchiveKeysDocument
{
    [JsonPropertyName("game_build")] public string? GameBuild { get; init; }
    [JsonPropertyName("pak_key")] public string? PakKey { get; init; }
    [JsonPropertyName("container_key")] public IReadOnlyList<string>? ContainerKey { get; init; }
}

/// <summary>Fetches the game's archive keys from the chosen server.</summary>
public sealed class ArchiveKeysClient : IDisposable
{
    public const string Path = "/ares/v1/archive-keys";
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10), MaxResponseContentBufferSize = 65536 };

    /// <exception cref="InvalidOperationException">The server does not provide valid keys.</exception>
    public async Task<ArchiveKeys> FetchAsync(ServerProfile profile, CancellationToken cancel = default)
    {
        ArchiveKeysDocument? document;
        try
        {
            document = await _http.GetFromJsonAsync<ArchiveKeysDocument>(profile.Origin + Path, cancel);
        }
        catch (HttpRequestException e) when (e.StatusCode is HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException(
                "This server does not provide the game's archive keys. Ask its operator, or enter the keys in Settings.", e);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            throw new InvalidOperationException("Could not get the game's archive keys from this server.", e);
        }

        try
        {
            return document is { PakKey: { } pakKey, ContainerKey: { } containerKey }
                ? ArchiveKeys.Parse(pakKey, string.Join(',', containerKey))
                : throw new FormatException("Incomplete archive keys.");
        }
        catch (FormatException e)
        {
            throw new InvalidOperationException("This server sent invalid archive keys.", e);
        }
    }

    public void Dispose() => _http.Dispose();
}
