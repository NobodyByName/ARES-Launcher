namespace Ares.Launcher.Core.Game;

/// <summary>The installation the launcher uses, from <see cref="GameInstall.Resolve"/>; null where none was found.</summary>
/// <param name="GameDirectory">The game folder, always a valid installation.</param>
/// <param name="SteamDirectory">The client folder passed to Proton.</param>
/// <param name="CompatDataDirectory">The game's Proton prefix, in the library holding the game.</param>
/// <param name="ProtonPath">The Proton script that starts the game.</param>
public sealed record ResolvedGameInstall(
    string? GameDirectory,
    string? SteamDirectory,
    string? CompatDataDirectory,
    string? ProtonPath);
