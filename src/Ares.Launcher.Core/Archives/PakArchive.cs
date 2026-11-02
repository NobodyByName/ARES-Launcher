using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace Ares.Launcher.Core.Archives;

/// <summary>
/// Read-only pak index and uncompressed-entry extraction. Never writes game files.
/// </summary>
public sealed class PakArchive
{
    private readonly TheiaArchive _reader;
    private readonly byte[] _key;
    private readonly ulong[] _roundKeys;
    private readonly byte[] _encoded;
    private readonly Dictionary<string, int> _files = new(StringComparer.Ordinal);

    private string MountPoint { get; }

    public PakArchive(string path, ArchiveKeys keys)
    {
        _reader = new TheiaArchive(path, keys.ContainerKey);
        _key = keys.PakKey;
        _roundKeys = ExpandKey(_key);
        PakFooter footer = _reader.ReadFooter();
        byte[] primary = ReadIndex(footer, footer.IndexOffset, footer.IndexSize, _reader.Read(footer.MagicOffset + 24, 20));
        Cursor c = new(primary);
        MountPoint = c.String();
        int count = c.Int32();
        c.Skip(8);
        byte[]? directory = null;
        foreach (string name in new[] { "path_hash", "directory" })
        {
            if (c.UInt32() != 1)
            {
                throw new InvalidDataException("Required secondary pak index is absent");
            }

            long offset = c.Int64();
            long size = c.Int64();
            byte[] digest = c.Bytes(20);
            if (name == "directory")
            {
                directory = ReadIndex(footer, offset, size, digest);
            }
        }
        _encoded = c.Bytes(c.Int32());
        Cursor d = new(directory!);
        int dirs = d.Int32();
        for (int i = 0; i < dirs; i++)
        {
            string parent = d.String();
            int files = d.Int32();
            for (int j = 0; j < files; j++)
            {
                string file = d.String();
                int location = d.Int32();
                if (location != int.MinValue)
                {
                    _files[MountPoint + parent + file] = location;
                }
            }
        }
        if (_files.Count != count)
        {
            throw new InvalidDataException("Pak file count mismatch");
        }
    }

    private static ulong[] ExpandKey(byte[] key)
    {
        ulong[] words = new ulong[4];
        for (int i = 0; i < 4; i++)
        {
            words[i] = BinaryPrimitives.ReadUInt64LittleEndian(key.AsSpan(i * 8));
        }

        List<ulong> rk = [words[0]];
        List<ulong> left = [words[1], words[2], words[3]];
        for (int i = 0; i < 33; i++)
        {
            left.Add((rk[i] + BitOperations.RotateRight(left[i], 8)) ^ (ulong)i);
            rk.Add(BitOperations.RotateLeft(rk[i], 3) ^ left[i + 3]);
        }
        return [.. rk];
    }

    private byte[] DecryptIndex(byte[] raw)
    {
        if (raw.Length % 16 != 0)
        {
            throw new InvalidDataException("Encrypted index is not block aligned");
        }

        for (int at = 0; at < raw.Length; at += 16)
        {
            ulong right = BinaryPrimitives.ReadUInt64LittleEndian(raw.AsSpan(at));
            ulong left = BinaryPrimitives.ReadUInt64LittleEndian(raw.AsSpan(at + 8));
            for (int k = _roundKeys.Length - 1; k >= 0; k--)
            {
                right = BitOperations.RotateRight(right ^ left, 3);
                left = BitOperations.RotateLeft((left ^ _roundKeys[k]) - right, 8);
            }

            BinaryPrimitives.WriteUInt64LittleEndian(raw.AsSpan(at), right);
            BinaryPrimitives.WriteUInt64LittleEndian(raw.AsSpan(at + 8), left);
        }

        return AesDecrypt(raw);
    }

    private byte[] ReadIndex(PakFooter footer, long offset, long size, byte[] digest)
    {
        byte[] raw = _reader.Read(offset, checked((int)size));
        if (footer.EncryptedIndex)
        {
            raw = DecryptIndex(raw);
        }

        return SHA1.HashData(raw).AsSpan().SequenceEqual(digest)
            ? raw
            : throw new InvalidDataException("Pak index SHA-1 mismatch");
    }

    private byte[] AesDecrypt(byte[] data)
    {
        using Aes aes = Aes.Create();
        aes.Key = _key;
        return aes.DecryptEcb(data, PaddingMode.None);
    }

    /// <summary>Extracts an uncompressed (optionally encrypted) entry by full path.</summary>
    public byte[] Extract(string path)
    {
        if (!_files.TryGetValue(path, out int location))
        {
            throw new FileNotFoundException("Entry not in pak", path);
        }

        if (location < 0)
        {
            throw new InvalidDataException("Non-encoded pak entries are unsupported");
        }

        Cursor c = new(_encoded, location);
        uint bits = c.UInt32();
        if ((bits & 63) == 63)
        {
            c.UInt32(); // explicit block size (unused for uncompressed entries)
        }

        uint method = (bits >> 23) & 63;
        long offset = (bits & (1u << 31)) != 0 ? c.UInt32() : (long)c.UInt64();
        long size = (bits & (1u << 30)) != 0 ? c.UInt32() : (long)c.UInt64();
        if (method != 0)
        {
            throw new NotSupportedException("Compressed pak entries are not implemented");
        }

        bool encrypted = (bits & (1u << 22)) != 0;
        const int Header = 53;
        long amount = encrypted ? (size + 15) & ~15L : size;
        byte[] raw = _reader.Read(offset + Header, checked((int)amount));
        byte[] plain = encrypted ? AesDecrypt(raw) : raw;
        return [.. plain.AsSpan(0, checked((int)size))];
    }

    private sealed class Cursor(byte[] data, int offset = 0)
    {
        private int _at = offset;

        public byte[] Bytes(int n)
        {
            if (n < 0 || _at + n > data.Length)
            {
                throw new InvalidDataException("Pak index range invalid");
            }

            byte[] result = [.. data.AsSpan(_at, n)];
            _at += n;
            return result;
        }

        public void Skip(int n) => Bytes(n);
        public int Int32() => BinaryPrimitives.ReadInt32LittleEndian(Bytes(4));
        public uint UInt32() => BinaryPrimitives.ReadUInt32LittleEndian(Bytes(4));
        public long Int64() => BinaryPrimitives.ReadInt64LittleEndian(Bytes(8));
        public ulong UInt64() => BinaryPrimitives.ReadUInt64LittleEndian(Bytes(8));

        public string String()
        {
            int n = Int32();
            if (Math.Abs(n) > 16384)
            {
                throw new InvalidDataException("Pak index string too long");
            }

            byte[] raw = Bytes(Math.Abs(n) * (n < 0 ? 2 : 1));
            return (n < 0 ? Encoding.Unicode.GetString(raw) : Encoding.UTF8.GetString(raw)).TrimEnd('\0');
        }
    }
}
