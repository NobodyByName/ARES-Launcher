using System.Buffers.Binary;
using System.Text;

namespace Ares.Launcher.Core.Archives;

/// <summary>
/// Read-only access to a Theia-wrapped installed archive.
/// Uses the archive's sibling ".meta" file for per-page keys. Opens files read-only.
/// </summary>
public sealed class TheiaArchive
{
    private const int PageSize = 65536;
    private readonly string _path;
    private readonly byte[] _meta;
    private readonly uint[] _constant;
    private readonly uint[] _master;

    private long Size { get; }

    public TheiaArchive(string path, IReadOnlyList<uint> constantWords)
    {
        _path = path;
        Size = new FileInfo(path).Length;
        _meta = File.ReadAllBytes(path + ".meta");
        if (_meta.Length < 512 || Encoding.ASCII.GetString(_meta, 0, 8) != "metadat0"
            || BinaryPrimitives.ReadInt64LittleEndian(_meta.AsSpan(80)) != Size)
        {
            throw new InvalidDataException($"Invalid archive metadata for {Path.GetFileName(path)}");
        }

        if (constantWords.Count != 8)
        {
            throw new ArgumentException("Container constant must have 8 words");
        }

        _constant = [.. constantWords];
        byte[] seed = new byte[64];
        _meta.AsSpan(96, 32).CopyTo(seed);
        _master = TheiaCrypto.Hash64(seed);
    }

    public byte[] Read(long offset, int size)
    {
        if (offset < 0 || size < 0 || offset + size > Size)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        byte[] data = new byte[size];
        using (FileStream stream = new(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            stream.Seek(offset, SeekOrigin.Begin);
            stream.ReadExactly(data);
        }
        Span<uint> key = stackalloc uint[8];
        int at = 0;
        while (at < size)
        {
            long page = (offset + at) / PageSize;
            int within = (int)((offset + at) % PageSize);
            int n = Math.Min(size - at, PageSize - within);
            int keyOffset = checked((int)(512 + page * 64 + 32));
            if (keyOffset + 32 > _meta.Length)
            {
                throw new InvalidDataException("Archive page key outside metadata");
            }

            for (int i = 0; i < 8; i++)
            {
                key[i] = BinaryPrimitives.ReadUInt32LittleEndian(_meta.AsSpan(keyOffset + i * 4));
            }

            TheiaCrypto.DecryptPage(data.AsSpan(at, n), (uint)within, _constant, _master, key);
            at += n;
        }
        return data;
    }

    public PakFooter ReadFooter()
    {
        long start = Math.Max(0, Size - 1024);
        byte[] data = Read(start, (int)(Size - start));
        byte[] magic = [0xe1, 0x12, 0x6f, 0x5a];
        int at = data.AsSpan().LastIndexOf(magic);
        if (at < 1)
        {
            throw new InvalidDataException("Pak footer magic not found");
        }

        return new PakFooter(
            BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at + 4)),
            BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(at + 8)),
            BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(at + 16)),
            data[at - 1] != 0,
            start + at);
    }
}

public sealed record PakFooter(uint Version, long IndexOffset, long IndexSize, bool EncryptedIndex, long MagicOffset);
