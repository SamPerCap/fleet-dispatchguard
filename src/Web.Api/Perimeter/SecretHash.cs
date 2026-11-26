using System.Security.Cryptography;
using System.Text;

namespace DispatchGuard.API.Perimeter;

/// <summary>
/// A plain comparison stops at the first different character, so its duration reveals how much of the secret was guessed. Hashing
/// first also hides the length, because both sides always measure the same.
/// </summary>
internal sealed class SecretHash
{
    private readonly byte[] _hash;

    private SecretHash(string secret) => _hash = HashOf(secret);

    /// <summary>Null without a secret: an empty one would let anyone in.</summary>
    public static SecretHash? Of(string? secret) => string.IsNullOrWhiteSpace(secret) ? null : new SecretHash(secret);

    public bool Matches(string received) => CryptographicOperations.FixedTimeEquals(HashOf(received), _hash);

    private static byte[] HashOf(string secret) => SHA256.HashData(Encoding.UTF8.GetBytes(secret));
}
