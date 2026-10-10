using System.Security.Cryptography;
using System.Text.Json;
using Radiant.Licensing;

var passed = 0;
var now = DateTimeOffset.UtcNow;
using var rsa = RSA.Create(2048);
string B64(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
var key = new SigningKey("test-key", B64(rsa.ExportSubjectPublicKeyInfo()));
var payload = new LeasePayload("license-test", "installation-test", "device-test", "optimizer_premium_lifetime", now.AddDays(7), now);
SignedLease Sign(LeasePayload value)
{
    var bytes = JsonSerializer.SerializeToUtf8Bytes(value, LeaseVerifier.Json);
    return new(B64(bytes), B64(rsa.SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)), key.KeyId);
}
void Assert(bool value, string message) { if (!value) throw new Exception(message); passed++; Console.WriteLine("PASS " + message); }
void Reject(Action action, string message)
{
    try { action(); } catch (Exception error) when (error is CryptographicException or FormatException or JsonException or ArgumentException or LicenseApiException)
    { Assert(true, message); return; }
    throw new Exception("Did not reject: " + message);
}
var lease = Sign(payload);
Assert(LeaseVerifier.Verify(lease, key, "installation-test", now).LicenseId == payload.LicenseId, "valid RSA lease is accepted");
Reject(() => LeaseVerifier.Verify(lease with { Payload = Sign(payload with { DeviceId = "different" }).Payload }, key, "installation-test", now), "tampered payload rejected");
Reject(() => LeaseVerifier.Verify(lease with { Signature = lease.Signature[..^8] }, key, "installation-test", now), "truncated signature rejected");
Reject(() => LeaseVerifier.Verify(lease, key, "other-installation", now), "another installation cannot reuse lease");
Reject(() => LeaseVerifier.Verify(lease, key with { KeyId = "unknown" }, "installation-test", now), "unknown signing key rejected");
Reject(() => LeaseVerifier.Verify(Sign(payload with { ExpiresAt = now }), key, "installation-test", now), "expired lease rejected at exact boundary");
Reject(() => LeaseVerifier.Verify(Sign(payload with { IssuedAt = now.AddHours(1) }), key, "installation-test", now), "future-issued lease rejected");
Reject(() => LeaseVerifier.Verify(Sign(payload with { ExpiresAt = now.AddDays(8) }), key, "installation-test", now), "excessive offline duration rejected");
Reject(() => LeaseVerifier.Verify(Sign(payload with { ProductId = "another-product" }), key, "installation-test", now), "another product rejected");
Reject(() => LeaseVerifier.Verify(Sign(payload with { DeviceId = "" }), key, "installation-test", now), "missing device identity rejected");
Reject(() => LeaseVerifier.Decode(new string('A', 32769)), "oversized license rejected");

if (OperatingSystem.IsWindows())
{
    var data = Path.Combine(Path.GetTempPath(), "optimizer-license-test-" + Guid.NewGuid().ToString("N"));
    try
    {
        string id;
        using (var first = new LicenseClient("http://localhost:3010", data, [key]))
        { id = first.InstallationId; Assert(!first.CachedStatus().IsPremium, "fresh installation is Free"); }
        using (var second = new LicenseClient("http://localhost:3010", data, [key]))
            Assert(second.InstallationId == id, "DPAPI-protected installation persists across restart");
        var protectedBytes = File.ReadAllBytes(Path.Combine(data, "device-license.dat"));
        Assert(!System.Text.Encoding.UTF8.GetString(protectedBytes).Contains(id, StringComparison.Ordinal), "stored device data is not plaintext");
        var statePath = Path.Combine(data, "device-license.dat");
        void WriteCache(SignedLease cachedLease, SigningKey cachedKey)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new { installationId = id, serverOrigin = "http://localhost:3010",
                lease = cachedLease, key = cachedKey }, LeaseVerifier.Json);
            File.WriteAllBytes(statePath, WindowsSecretStore.Protect(bytes));
        }
        WriteCache(Sign(payload with { InstallationId = id }), key);
        using (var genuine = new LicenseClient("http://localhost:3010", data, [key]))
            Assert(genuine.CachedStatus().IsPremium, "publisher-signed cached lease works offline");
        using var attackerRsa = RSA.Create(2048);
        var attackerKey = key with { Spki = B64(attackerRsa.ExportSubjectPublicKeyInfo()) };
        var forgedPayload = JsonSerializer.SerializeToUtf8Bytes(payload with { InstallationId = id }, LeaseVerifier.Json);
        var forgedLease = new SignedLease(B64(forgedPayload), B64(attackerRsa.SignData(forgedPayload,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)), key.KeyId);
        WriteCache(forgedLease, attackerKey);
        using (var forged = new LicenseClient("http://localhost:3010", data, [key]))
            Assert(!forged.CachedStatus().IsPremium, "user-reencrypted cache cannot replace trusted publisher key");
        WriteCache(forgedLease with { KeyId = "untrusted" }, attackerKey with { KeyId = "untrusted" });
        using (var untrusted = new LicenseClient("http://localhost:3010", data, [key]))
            Assert(!untrusted.CachedStatus().IsPremium, "cached unknown key cannot become a trust root");
        using var client = new LicenseClient("http://localhost:3010", data, [key]);
        Reject(() => client.VerificationUri(new DeviceRequest("secret", "ABC12345", "https://another.example/activate-device", now.AddMinutes(5), 5)),
            "activation origin cannot be replaced");
        File.WriteAllBytes(statePath, [1, 2, 3]);
        using (var damaged = new LicenseClient("http://localhost:3010", data, [key]))
            Assert(!damaged.CachedStatus().IsPremium && damaged.CacheWarning is not null,
                "corrupt DPAPI cache falls back to Free without blocking recovery");
        File.WriteAllBytes(statePath, WindowsSecretStore.Protect(System.Text.Encoding.UTF8.GetBytes("{ broken")));
        using (var damagedJson = new LicenseClient("http://localhost:3010", data, [key]))
            Assert(!damagedJson.CachedStatus().IsPremium && damagedJson.CacheWarning is not null,
                "invalid encrypted JSON falls back to Free without blocking recovery");
        Assert(Directory.GetFiles(data, "*.unreadable-*").Length == 2, "unreadable cache is preserved for diagnosis");
    }
    finally { if (Directory.Exists(data) && Path.GetFileName(data).StartsWith("optimizer-license-test-", StringComparison.Ordinal)) Directory.Delete(data, true); }
}
Console.WriteLine($"{passed} license checks passed.");
