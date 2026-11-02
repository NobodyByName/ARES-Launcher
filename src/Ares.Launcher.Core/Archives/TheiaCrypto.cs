using System.Buffers.Binary;
using System.Numerics;

namespace Ares.Launcher.Core.Archives;

/// <summary>
/// Read-only decryption primitives for the game's "Theia" archive wrapper.
/// The compression function follows the BLAKE3 specification. Nothing here writes game files.
/// </summary>
internal static class TheiaCrypto
{
    private static readonly uint[] s_iv =
    [
        0x6a09e667, 0xbb67ae85, 0x3c6ef372, 0xa54ff53a, 0x510e527f, 0x9b05688c, 0x1f83d9ab, 0x5be0cd19,
    ];

    private static readonly byte[] s_permutation = [2, 6, 3, 10, 7, 0, 4, 13, 1, 11, 12, 5, 9, 14, 15, 8];

    private static void Mix(Span<uint> v, int a, int b, int c, int d, uint x, uint y)
    {
        v[a] += v[b] + x;
        v[d] = BitOperations.RotateRight(v[d] ^ v[a], 16);
        v[c] += v[d];
        v[b] = BitOperations.RotateRight(v[b] ^ v[c], 12);
        v[a] += v[b] + y;
        v[d] = BitOperations.RotateRight(v[d] ^ v[a], 8);
        v[c] += v[d];
        v[b] = BitOperations.RotateRight(v[b] ^ v[c], 7);
    }

    /// <summary>BLAKE3 compression: 8-word chaining value, 16-word block -> 16-word output.</summary>
    private static void Compress(ReadOnlySpan<uint> cv, ReadOnlySpan<uint> block, ulong counter, uint length, uint flags, Span<uint> output)
    {
        Span<uint> v = stackalloc uint[16];
        Span<uint> m = stackalloc uint[16];
        Span<uint> next = stackalloc uint[16];
        cv[..8].CopyTo(v);
        s_iv.AsSpan(0, 4).CopyTo(v[8..]);
        block[..16].CopyTo(m);
        v[12] = (uint)counter;
        v[13] = (uint)(counter >> 32);
        v[14] = length;
        v[15] = flags;
        for (int round = 0; round < 7; round++)
        {
            Mix(v, 0, 4, 8, 12, m[0], m[1]);
            Mix(v, 1, 5, 9, 13, m[2], m[3]);
            Mix(v, 2, 6, 10, 14, m[4], m[5]);
            Mix(v, 3, 7, 11, 15, m[6], m[7]);
            Mix(v, 0, 5, 10, 15, m[8], m[9]);
            Mix(v, 1, 6, 11, 12, m[10], m[11]);
            Mix(v, 2, 7, 8, 13, m[12], m[13]);
            Mix(v, 3, 4, 9, 14, m[14], m[15]);
            for (int i = 0; i < 16; i++)
            {
                next[i] = m[s_permutation[i]];
            }

            next.CopyTo(m);
        }
        for (int i = 0; i < 8; i++)
        {
            output[i] = v[i] ^ v[i + 8];
            output[i + 8] = v[i + 8] ^ cv[i];
        }
    }

    /// <summary>Single-block BLAKE3 hash of exactly 64 bytes (first 8 output words).</summary>
    public static uint[] Hash64(ReadOnlySpan<byte> input)
    {
        Span<uint> words = stackalloc uint[16];
        for (int i = 0; i < 16; i++)
        {
            words[i] = BinaryPrimitives.ReadUInt32LittleEndian(input[(i * 4)..]);
        }

        Span<uint> digest = stackalloc uint[16];
        Compress(s_iv, words, 0, 64, 11, digest);
        return [.. digest[..8]];
    }

    /// <summary>Decrypts bytes of one 64 KiB page in place (subtractive key stream).</summary>
    public static void DecryptPage(Span<byte> data, uint pageOffset, ReadOnlySpan<uint> constant, ReadOnlySpan<uint> master, ReadOnlySpan<uint> pageKey)
    {
        Span<uint> combined = stackalloc uint[16];
        master[..8].CopyTo(combined);
        pageKey[..8].CopyTo(combined[8..]);
        Span<uint> state = stackalloc uint[16];
        Compress(constant, combined, 0, 64, 17, state);
        Span<uint> zeros = stackalloc uint[16];
        Span<uint> stream = stackalloc uint[16];
        Span<byte> streamBytes = stackalloc byte[64];
        int at = 0;
        while (at < data.Length)
        {
            uint position = pageOffset + (uint)at;
            int skip = (int)(position % 64);
            Compress(state, zeros, position / 64, 1, 26, stream);
            for (int i = 0; i < 16; i++)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(streamBytes[(i * 4)..], stream[i]);
            }

            int n = Math.Min(64 - skip, data.Length - at);
            for (int i = 0; i < n; i++)
            {
                data[at + i] = (byte)(data[at + i] - streamBytes[skip + i]);
            }

            at += n;
        }
    }

    /// <summary>BLAKE3 reference vector self-test.</summary>
    public static void SelfTest()
    {
        Span<uint> output = stackalloc uint[16];
        Compress(s_iv, new uint[16], 0, 0, 11, output);
        Span<byte> bytes = stackalloc byte[32];
        for (int i = 0; i < 8; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes[(i * 4)..], output[i]);
        }

        if (Convert.ToHexStringLower(bytes) != "af1349b9f5f9a1a6a0404dea36dcc9499bcb25c9adc112b7cc9a93cae41f3262")
        {
            throw new InvalidOperationException("BLAKE3 reference vector failed");
        }
    }
}
