using System.Buffers.Binary;
using System.Text;

namespace Ares.Launcher.Core.Config;

/// <summary>
/// Reads the game's compiled config cache (PioneerGame/Config/BinaryConfig.ini) and writes the
/// separate bootstrap file for <c>-textconfig -IniBootstrap=&lt;file&gt;</c>. Only the two
/// [EmbarkOnline] connection origins change; every other value is preserved and re-verified.
/// </summary>
public static class BinaryConfig
{
    private static readonly string[] s_originLayers = ["memory", "combined", "final"];

    private static readonly string[] s_knownFiles =
        ["Engine", "Game", "Input", "DeviceProfiles", "GameUserSettings", "Scalability", "RuntimeOptions", "InstallBundle", "Hardware", "GameplayTags"];

    /// <summary>The bootstrap reader's fourteen global INI names, appended after the cache.</summary>
    private static readonly string[] s_globals =
        ["", "", "", "", "", "Compat", "Lightmass", "Scalability", "Hardware", "Input", "Game", "GameUserSettings", "RuntimeOptions", "Engine"];

    public sealed record Value(string Branch, string Layer, string Section, string Key, string Text, int Start, int End)
    {
        public bool IsOrigin => Branch == "Engine" && Section == "EmbarkOnline" && Key is "Endpoint" or "OpenApiUrl";
    }

    public sealed class ParseResult
    {
        public List<Value> Values { get; } = [];
        public int End { get; internal set; }
    }

    public static ParseResult Parse(byte[] data)
    {
        if (data.Length > 8 * 1024 * 1024)
        {
            throw new InvalidDataException("Config archive exceeds the supported size");
        }

        Reader r = new(data);
        ParseResult result = new();
        int custom = r.Count();
        for (int i = 0; i < custom; i++)
        {
            r.Branch(r.String(), result.Values);
        }

        foreach (string name in s_knownFiles)
        {
            r.Branch(name, result.Values);
        }

        r.Boolean();
        r.Boolean();
        byte type = r.Take(1)[0];
        if (type is not (0 or 1))
        {
            throw new InvalidDataException("Unknown config cache type");
        }

        r.String();
        int sets = r.Count();
        for (int i = 0; i < sets; i++)
        {
            r.String();
            int n = r.Count();
            for (int j = 0; j < n; j++)
            {
                r.String();
            }
        }
        if (r.Boolean() != 0)
        {
            int n = r.Count();
            for (int j = 0; j < n; j++)
            {
                r.String();
            }
        }
        result.End = r.Position;
        return result;
    }

    /// <summary>Builds the bootstrap file with the auth origin set to <paramref name="authOrigin"/> and the API origin set to <paramref name="apiOrigin"/> (e.g. http://host:port).</summary>
    public static byte[] BuildBootstrap(byte[] source, string authOrigin, string apiOrigin)
    {
        Dictionary<string, string> origins = new()
        {
            ["Endpoint"] = authOrigin.TrimEnd('/'),
            ["OpenApiUrl"] = apiOrigin.TrimEnd('/'),
        };
        ParseResult parsed = Parse(source);
        if (source.Length - parsed.End != 4 || BinaryPrimitives.ReadInt32LittleEndian(source.AsSpan(parsed.End)) != 0)
        {
            throw new InvalidDataException("Unexpected compiled-config trailer");
        }

        List<Value> selected = [.. parsed.Values.Where(v => v.IsOrigin)];
        HashSet<(string l, string k)> expected = [.. s_originLayers.SelectMany(l => origins.Keys.Select(k => (l, k)))];
        if (selected.Count != 6 || !selected.Select(v => (v.Layer, v.Key)).ToHashSet().SetEquals(expected))
        {
            throw new InvalidDataException("Unexpected connection-key layout in this game version");
        }

        byte[] output = [.. source.AsSpan(0, parsed.End)];
        foreach (Value value in selected.OrderByDescending(v => v.Start))
        {
            byte[] replacement = EncodeString(origins[value.Key]);
            output = [.. output.AsSpan(0, value.Start), .. replacement, .. output.AsSpan(value.End)];
        }

        // Reject any unexpected layout effect; every unrelated value must be unchanged.
        ParseResult check = Parse([.. output, 0, 0, 0, 0]);
        if (check.End != output.Length || check.Values.Count != parsed.Values.Count)
        {
            throw new InvalidDataException("Generated config did not round-trip");
        }

        for (int i = 0; i < parsed.Values.Count; i++)
        {
            (Value old, Value now) = (parsed.Values[i], check.Values[i]);
            if ((old.Branch, old.Layer, old.Section, old.Key) != (now.Branch, now.Layer, now.Section, now.Key))
            {
                throw new InvalidDataException("Generated config changed a configuration key");
            }

            if (now.Text != (old.IsOrigin ? origins[old.Key] : old.Text))
            {
                throw new InvalidDataException("Generated config changed an unrelated value");
            }
        }
        using MemoryStream stream = new();
        stream.Write(output);
        foreach (string name in s_globals)
        {
            stream.Write(EncodeString(name));
        }

        return stream.ToArray();
    }

    public static byte[] EncodeString(string text)
    {
        byte[] raw = Encoding.UTF8.GetBytes(text + "\0");
        byte[] result = new byte[4 + raw.Length];
        BinaryPrimitives.WriteInt32LittleEndian(result, raw.Length);
        raw.CopyTo(result, 4);
        return result;
    }

    private sealed class Reader(byte[] data)
    {
        public int Position { get; private set; }

        public byte[] Take(int size)
        {
            if (size < 0 || size > data.Length - Position)
            {
                throw new InvalidDataException($"Config archive bounds at offset {Position}");
            }

            byte[] result = [.. data.AsSpan(Position, size)];
            Position += size;
            return result;
        }

        private int Integer() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));

        public int Count()
        {
            int n = Integer();
            return n is < 0 or > 100000 ? throw new InvalidDataException($"Invalid count at offset {Position - 4}") : n;
        }

        public int Boolean()
        {
            int n = Integer();
            return n is not (0 or 1) ? throw new InvalidDataException($"Invalid boolean at offset {Position - 4}") : n;
        }

        public string String()
        {
            int n = Integer();
            if (n is <= -1000000 or >= 1000000)
            {
                throw new InvalidDataException($"Invalid string size at offset {Position - 4}");
            }

            byte[] raw = Take(Math.Abs(n) * (n < 0 ? 2 : 1));
            if (raw.Length > 0 && (n < 0 ? raw[^1] != 0 || raw[^2] != 0 : raw[^1] != 0))
            {
                throw new InvalidDataException($"Missing string terminator at offset {Position}");
            }

            return (n < 0 ? Encoding.Unicode.GetString(raw) : Encoding.UTF8.GetString(raw)).TrimEnd('\0');
        }

        private void File(string branch, string layer, List<Value> values)
        {
            int sections = Count();
            for (int s = 0; s < sections; s++)
            {
                string section = String();
                int keys = Count();
                for (int k = 0; k < keys; k++)
                {
                    string key = String();
                    int start = Position;
                    string text = String();
                    values.Add(new Value(branch, layer, section, key, text, start, Position));
                }
                int extra = Count();
                for (int e = 0; e < extra; e++)
                { String(); String(); }
            }
            Boolean();
            Boolean();
            Boolean();
            String();
            String();
            int groups = Count();
            for (int g = 0; g < groups; g++)
            {
                String();
                int n = Count();
                for (int j = 0; j < n; j++)
                { String(); String(); }
            }
        }

        public void Branch(string name, List<Value> values)
        {
            Boolean();
            File(name, "memory", values);
            int hierarchy = Count();
            for (int h = 0; h < hierarchy; h++)
            {
                Integer();
                Take(Count()); // UE5.7 hierarchy paths: length-prefixed UTF-8 without a NUL
            }
            Integer(); // hierarchy key generation
            File(name, "combined", values);
            File(name, "final", values);
            String();
            String();
        }
    }
}
