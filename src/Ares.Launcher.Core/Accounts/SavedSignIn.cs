using System.Text.Json.Serialization;

namespace Ares.Launcher.Core.Accounts;

/// <summary>
/// What "Remember me" keeps for one server: the account name, the server's salt and iteration count, and the stretched
/// password key (<see cref="SrpClient.Stretch"/>). It is not the password and only signs in to this account on this server
/// until the password changes, but anyone who copies it can do that, so it is stored readable by the current user only.
/// </summary>
public sealed record SavedSignIn(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("salt")] string Salt,
    [property: JsonPropertyName("iterations")] int Iterations,
    [property: JsonPropertyName("key")] string Key)
{
    /// <summary>Whether this sign-in belongs to <paramref name="name"/> (account names ignore case).</summary>
    public bool Matches(string name) => string.Equals(Name, name.Trim(), StringComparison.OrdinalIgnoreCase);
}
