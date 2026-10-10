namespace Radiant.Licensing;

public sealed record DeviceRequest(string DeviceCode, string UserCode, string VerificationUri,
    DateTimeOffset ExpiresAt, int IntervalSeconds);
public sealed record SignedLease(string Payload, string Signature, string KeyId);
public sealed record LeasePayload(string LicenseId, string InstallationId, string DeviceId,
    string ProductId, DateTimeOffset ExpiresAt, DateTimeOffset IssuedAt);
public sealed record SigningKey(string KeyId, string Spki);
internal sealed record KeySet(SigningKey[] Keys);
internal sealed record PollResponse(string Status, string? DeviceToken, SignedLease? Lease);
internal sealed record RefreshResponse(SignedLease Lease);
internal sealed record Envelope<T>(int Code, string Message, T? Data);
internal sealed record StoredLicense(string InstallationId, string ServerOrigin,
    string? DeviceToken = null, SignedLease? Lease = null);
public sealed record LicenseStatus(bool IsPremium, string Message, DateTimeOffset? ExpiresAt = null);

public sealed class LicenseApiException(string message, int statusCode = 0) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
