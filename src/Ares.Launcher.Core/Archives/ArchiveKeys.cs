using System.Globalization;

namespace Ares.Launcher.Core.Archives;

/// <summary>
/// The game's archive keys: the pak key and the container key words. They are not part of ARES:
/// the launcher gets them from the chosen ARES-Host or from Settings.
/// </summary>
/// <param name="PakKey">The 32-byte pak key.</param>
/// <param name="ContainerKey">The eight container key words.</param>
public sealed record ArchiveKeys(byte[] PakKey, IReadOnlyList<uint> ContainerKey)
{
    private const int PakKeyBytes = 32;
    private const int ContainerKeyWords = 8;
    private const string PakKeyName = "pak-key";
    private const string ContainerKeyName = "container-key";

    /// <summary>
    /// Parses the two keys: the pak key as 64 hex digits, the container key as eight hex words separated by
    /// commas or spaces. <c>0x</c> prefixes are optional.
    /// </summary>
    /// <exception cref="FormatException">A key is malformed.</exception>
    public static ArchiveKeys Parse(string pakKey, string containerKey)
    {
        return new ArchiveKeys(ParsePakKey(pakKey), ParseContainerKey(containerKey));
    }

    /// <summary>
    /// Parses a keys file (the format ARES-Host operators use): a <c>pak-key = …</c> line and a
    /// <c>container-key = …</c> line; <c>#</c> starts a comment.
    /// </summary>
    /// <exception cref="FormatException">The text is not a valid keys file.</exception>
    public static ArchiveKeys ParseFile(string text)
    {
        string? pakKey = null;
        string? containerKey = null;
        foreach (string rawLine in text.Split('\n'))
        {
            string line = rawLine.Split('#')[0].Trim();
            if (line.Length == 0)
            {
                continue;
            }

            int separator = line.IndexOf('=', StringComparison.Ordinal);
            if (separator < 0)
            {
                throw new FormatException($"Expected '{PakKeyName} = …' or '{ContainerKeyName} = …', got '{line}'");
            }

            string name = line[..separator].Trim();
            string value = line[(separator + 1)..].Trim();
            switch (name)
            {
                case PakKeyName when pakKey is null:
                    pakKey = value;
                    break;
                case ContainerKeyName when containerKey is null:
                    containerKey = value;
                    break;
                case PakKeyName or ContainerKeyName:
                    throw new FormatException($"'{name}' is given twice");
                default:
                    throw new FormatException($"Unknown key '{name}'");
            }
        }

        return Parse(pakKey ?? throw new FormatException($"'{PakKeyName}' is missing"),
            containerKey ?? throw new FormatException($"'{ContainerKeyName}' is missing"));
    }

    private static byte[] ParsePakKey(string value)
    {
        string hex = WithoutHexPrefix(value.Replace(" ", "", StringComparison.Ordinal).Trim());
        if (hex.Length != PakKeyBytes * 2 || !hex.All(char.IsAsciiHexDigit))
        {
            throw new FormatException($"The pak key must be {PakKeyBytes * 2} hex digits.");
        }

        return Convert.FromHexString(hex);
    }

    private static uint[] ParseContainerKey(string value)
    {
        string[] words = value.Split([',', ' ', '\t', '\r', '\n', '[', ']', '{', '}'], StringSplitOptions.RemoveEmptyEntries);
        if (words.Length != ContainerKeyWords)
        {
            throw new FormatException($"The container key must have {ContainerKeyWords} words.");
        }

        return
        [
            .. words.Select(word => WithoutHexPrefix(word) is { Length: >= 1 and <= 8 } hex && hex.All(char.IsAsciiHexDigit)
                ? uint.Parse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture)
                : throw new FormatException($"Container key word '{word}' is not a 32-bit hex number.")),
        ];
    }

    private static string WithoutHexPrefix(string value)
    {
        return value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value[2..] : value;
    }
}
