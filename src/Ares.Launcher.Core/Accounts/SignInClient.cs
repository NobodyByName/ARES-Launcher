using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ares.Launcher.Core.Profiles;

namespace Ares.Launcher.Core.Accounts;

/// <summary>A successful sign-in: the account name as stored on the server and the launch key for one game start.</summary>
public sealed record SignInResult(string Name, string LaunchKey);

/// <summary>Reply of <c>POST /ares/v1/sign-in/start</c>.</summary>
public sealed record SignInStartResponse
{
    [JsonPropertyName("session")] public string? Session { get; init; }
    [JsonPropertyName("salt")] public string? Salt { get; init; }
    [JsonPropertyName("iterations")] public int Iterations { get; init; }
    [JsonPropertyName("b")] public string? ServerPublic { get; init; }
}

/// <summary>Reply of <c>POST /ares/v1/sign-in/finish</c>.</summary>
public sealed record SignInFinishResponse
{
    [JsonPropertyName("proof")] public string? Proof { get; init; }
    [JsonPropertyName("name")] public string? Name { get; init; }
    [JsonPropertyName("launch_key")] public string? LaunchKey { get; init; }
}

/// <summary>Signs in to an ARES-Host account and obtains a launch key (sign-in protocol 1).</summary>
public sealed class SignInClient : IDisposable
{
    private const string StartPath = "/ares/v1/sign-in/start";
    private const string FinishPath = "/ares/v1/sign-in/finish";

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15), MaxResponseContentBufferSize = 65536 };

    /// <exception cref="InvalidOperationException">Sign-in failed; the message is meant for the player.</exception>
    public async Task<SignInResult> SignInAsync(ServerProfile profile, string name, string password, CancellationToken cancel = default)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrEmpty(password))
        {
            throw new InvalidOperationException("Enter your account name and password.");
        }

        name = name.Trim();
        SrpClient srp = new();
        try
        {
            using HttpResponseMessage started = await PostAsync(profile.Origin + StartPath, new { name, a = Convert.ToBase64String(srp.PublicValue) }, cancel);
            SignInStartResponse? challenge = await started.Content.ReadFromJsonAsync<SignInStartResponse>(cancel);
            if (challenge is not { Session: { } session, Salt: { } salt, ServerPublic: { } serverPublic })
            {
                throw new JsonException("Incomplete sign-in challenge");
            }

            (byte[] proof, byte[] expected) = await Task.Run(() => srp.Prove(name, password, Convert.FromBase64String(salt),
                challenge.Iterations, Convert.FromBase64String(serverPublic)), cancel);
            using HttpResponseMessage completed = await PostAsync(profile.Origin + FinishPath, new { session, proof = Convert.ToBase64String(proof) }, cancel);
            SignInFinishResponse? finished = await completed.Content.ReadFromJsonAsync<SignInFinishResponse>(cancel);
            if (finished is not { Proof: { } serverProof, Name: { } accountName }
                || !CryptographicOperations.FixedTimeEquals(Convert.FromBase64String(serverProof), expected))
            {
                throw new InvalidOperationException("The server could not prove it knows your account. Check the server address.");
            }

            return finished.LaunchKey is { Length: 44 } key
                ? new SignInResult(accountName, key)
                : throw new InvalidOperationException("The server sent an invalid launch key.");
        }
        catch (Exception e) when (e is FormatException or InvalidDataException or JsonException)
        {
            throw new InvalidOperationException("The server sent an invalid sign-in reply.", e);
        }
    }

    /// <summary>Posts <paramref name="body"/>; returns the reply when it is 200 OK.</summary>
    private async Task<HttpResponseMessage> PostAsync(string url, object body, CancellationToken cancel)
    {
        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsJsonAsync(url, body, cancel);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            throw new InvalidOperationException("Could not reach the server to sign in.", e);
        }

        if (response.StatusCode == HttpStatusCode.OK)
        {
            return response;
        }

        using (response)
        {
            throw response.StatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.BadRequest => new InvalidOperationException("Wrong account name or password."),
                HttpStatusCode.Forbidden => new InvalidOperationException("This account is banned on this server."),
                HttpStatusCode.NotFound or HttpStatusCode.NotImplemented => new InvalidOperationException("This server does not support account sign-in."),
                _ => new InvalidOperationException($"Sign-in failed (HTTP {(int)response.StatusCode})."),
            };
        }
    }

    public void Dispose() => _http.Dispose();
}
