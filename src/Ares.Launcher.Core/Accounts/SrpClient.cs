using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace Ares.Launcher.Core.Accounts;

/// <summary>
/// Client side of the ARES-Host SRP-6a sign-in (RFC 5054 2048-bit group, SHA-256, private value
/// <c>x = H(salt | PBKDF2-SHA512(password, salt, iterations))</c>). The password never leaves this computer; the server proves it
/// knows the account's verifier too.
/// </summary>
public sealed class SrpClient
{
    /// <summary>Length of N, A and B in bytes.</summary>
    private const int ModulusBytes = 256;

    /// <summary>Most PBKDF2 iterations accepted from a server, so a server cannot stall the launcher.</summary>
    private const int MaximumIterations = 2000000;

    /// <summary>Length of the PBKDF2 output in bytes.</summary>
    public const int StretchedBytes = 64;

    private static readonly BigInteger s_n = BigInteger.Parse(
        "0AC6BDB41324A9A9BF166DE5E1389582FAF72B6651987EE07FC3192943DB56050A37329CBB4A099ED8193E0757767A13DD52312AB4B03310D" +
        "CD7F48A9DA04FD50E8083969EDB767B0CF6095179A163AB3661A05FBD5FAAAE82918A9962F0B93B855F97993EC975EEAA80D740ADBF4FF747359D0" +
        "41D5C33EA71D281E446B14773BCA97B43A23FB801676BD207A436C6481F1D2B9078717461A5B9D32E688F87748544523B524B0D57D5EA77A2775D2EC" +
        "FA032CFBDBF52FB3786160279004E57AE6AF874E7303CE53299CCC041C7BC308D82A5698F3A8D0C38271AE35F8E9DBFBB694B5C803D89F7AE435DE23" +
        "6D525F54759B65E372FCD68EF20FA7111F9E4AFF73", NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    private static readonly BigInteger s_g = 2;

    private static readonly BigInteger s_k = Number(Hash(Pad(s_n), Pad(s_g)));

    private readonly BigInteger _secret;

    /// <summary>Starts a sign-in with a fresh random secret.</summary>
    public SrpClient()
        : this(Number(RandomNumberGenerator.GetBytes(32)))
    {
    }

    /// <summary>Starts a sign-in with a given secret (for known-answer checks).</summary>
    public SrpClient(BigInteger secret)
    {
        _secret = secret;
        PublicValue = Pad(BigInteger.ModPow(s_g, secret, s_n));
    }

    /// <summary>The value A sent to the server.</summary>
    public byte[] PublicValue { get; }

    /// <summary>The account name as the server identifies it: lower case.</summary>
    private static string NameKey(string name) => name.ToLowerInvariant();

    /// <summary>
    /// Computes the proof M1 for the server's challenge and the server proof M2 to expect back.
    /// </summary>
    /// <exception cref="InvalidDataException">The challenge is invalid.</exception>
    public (byte[] Proof, byte[] ExpectedServerProof) Prove(string name, string password, byte[] salt, int iterations, byte[] serverPublic)
    {
        CheckChallenge(salt, iterations);
        return Prove(name, Stretch(password, salt, iterations), salt, serverPublic);
    }

    /// <summary>
    /// Computes the proofs from a key made earlier by <see cref="Stretch"/> with the same salt and iterations.
    /// </summary>
    /// <exception cref="InvalidDataException">The challenge is invalid.</exception>
    public (byte[] Proof, byte[] ExpectedServerProof) Prove(string name, byte[] stretched, byte[] salt, byte[] serverPublic)
    {
        BigInteger b = Number(serverPublic);
        if (serverPublic.Length != ModulusBytes || b % s_n == 0 || stretched.Length != StretchedBytes)
        {
            throw new InvalidDataException("The server sent an invalid sign-in challenge.");
        }

        CheckChallenge(salt, 1);
        string nameKey = NameKey(name);
        BigInteger x = Number(Hash(salt, stretched));
        BigInteger u = Number(Hash(PublicValue, serverPublic));
        if (u == 0)
        {
            throw new InvalidDataException("The server sent an invalid sign-in challenge.");
        }

        BigInteger basis = (((b - (s_k * BigInteger.ModPow(s_g, x, s_n))) % s_n) + s_n) % s_n;
        byte[] key = Hash(Pad(BigInteger.ModPow(basis, _secret + (u * x), s_n)));
        byte[] group = Hash(Pad(s_n));
        byte[] generator = Hash(Pad(s_g));
        for (int i = 0; i < group.Length; i++)
        {
            group[i] ^= generator[i];
        }

        byte[] proof = Hash(group, Hash(Encoding.UTF8.GetBytes(nameKey)), salt, PublicValue, serverPublic, key);
        return (proof, Hash(PublicValue, proof, key));
    }

    /// <summary>The verifier a server stores for this password (for known-answer checks).</summary>
    public static byte[] Verifier(string password, byte[] salt, int iterations) =>
        Pad(BigInteger.ModPow(s_g, Number(Hash(salt, Stretch(password, salt, iterations))), s_n));

    /// <summary>
    /// The slow PBKDF2 step of <c>x</c>. It signs in to this one account (until its password changes) without revealing the password.
    /// </summary>
    /// <exception cref="InvalidDataException">The salt or iteration count is out of range.</exception>
    public static byte[] Stretch(string password, byte[] salt, int iterations)
    {
        CheckChallenge(salt, iterations);
        return Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA512, StretchedBytes);
    }

    private static void CheckChallenge(byte[] salt, int iterations)
    {
        if (iterations is < 1 or > MaximumIterations || salt.Length is < 16 or > 64)
        {
            throw new InvalidDataException("The server sent an invalid sign-in challenge.");
        }
    }

    private static byte[] Pad(BigInteger value)
    {
        byte[] bytes = value.ToByteArray(isUnsigned: true, isBigEndian: true);
        byte[] padded = new byte[ModulusBytes];
        bytes.CopyTo(padded, ModulusBytes - bytes.Length);
        return padded;
    }

    private static BigInteger Number(ReadOnlySpan<byte> bytes) => new(bytes, isUnsigned: true, isBigEndian: true);

    private static byte[] Hash(params ReadOnlySpan<byte[]> parts)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (byte[] part in parts)
        {
            hash.AppendData(part);
        }

        return hash.GetHashAndReset();
    }
}
