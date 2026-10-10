using System.Security.Cryptography;
using System.Text.Json;

namespace Radiant.Licensing;

public static class LeaseVerifier
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static byte[] Decode(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 32768) throw new FormatException("Invalid license data size.");
        var padded = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(padded.PadRight((padded.Length + 3) / 4 * 4, '='));
    }

    public static LeasePayload Verify(SignedLease lease, SigningKey key, string installationId,
        DateTimeOffset now)
    {
        if (!string.Equals(lease.KeyId, key.KeyId, StringComparison.Ordinal))
            throw new CryptographicException("Unknown license signing key.");
        var payload = Decode(lease.Payload);
        using var rsa = RSA.Create();
        rsa.ImportSubjectPublicKeyInfo(Decode(key.Spki), out _);
        if (rsa.KeySize < 2048 || !rsa.VerifyData(payload, Decode(lease.Signature),
                HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
            throw new CryptographicException("License signature could not be verified.");
        var parsed = JsonSerializer.Deserialize<LeasePayload>(payload, Json)
            ?? throw new FormatException("License payload is empty.");
        if (parsed.InstallationId != installationId || parsed.ProductId != "optimizer_premium_lifetime"
            || string.IsNullOrWhiteSpace(parsed.DeviceId) || string.IsNullOrWhiteSpace(parsed.LicenseId))
            throw new CryptographicException("License does not belong to this installation.");
        if (parsed.ExpiresAt <= now || parsed.IssuedAt > now.AddMinutes(5)
            || parsed.ExpiresAt <= parsed.IssuedAt
            || parsed.ExpiresAt - parsed.IssuedAt > TimeSpan.FromDays(7).Add(TimeSpan.FromMinutes(1)))
            throw new CryptographicException("License has expired or the computer clock is incorrect.");
        return parsed;
    }
}
