using System.Reflection;

namespace Ares.Launcher.Core;

/// <summary>The launcher's release version: <c>YYYYMMDDNN</c>, set as <c>AresVersion</c> in <c>Directory.Build.props</c>.</summary>
public static class LauncherVersion
{
    /// <summary>This build's version.</summary>
    public static string Current { get; } =
        typeof(LauncherVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
}
