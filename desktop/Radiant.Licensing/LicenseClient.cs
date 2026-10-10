using System.Net;
using System.ComponentModel;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

namespace Radiant.Licensing;

public sealed class LicenseClient : IDisposable
{
    private readonly HttpClient http;
    private readonly string statePath;
    private readonly string origin;
    private readonly IReadOnlyDictionary<string, SigningKey> trustedKeys;
    private readonly SemaphoreSlim operations = new(1, 1);
    private StoredLicense state;
    public string InstallationId => state.InstallationId;
    public Uri ServerUri => http.BaseAddress!;
    public string? CacheWarning { get; private set; }

    public LicenseClient(string serverUrl, string dataDirectory, IReadOnlyCollection<SigningKey> trustedKeys)
    {
        this.trustedKeys = trustedKeys.ToDictionary(key => key.KeyId, StringComparer.Ordinal);
        var server = new Uri(serverUrl, UriKind.Absolute);
        if (server.Scheme != "https" && !(server.Scheme == "http" && server.IsLoopback))
            throw new ArgumentException("The server must use HTTPS (HTTP is allowed only on this computer).");
        if (server.AbsolutePath != "/" || !string.IsNullOrEmpty(server.Query)
            || !string.IsNullOrEmpty(server.Fragment) || !string.IsNullOrEmpty(server.UserInfo))
            throw new ArgumentException("The server URL must contain only the trusted origin.");
        http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        { BaseAddress = server, Timeout = TimeSpan.FromSeconds(15) };
        origin = server.GetLeftPart(UriPartial.Authority);
        Directory.CreateDirectory(dataDirectory);
        statePath = Path.Combine(dataDirectory, "device-license.dat");
        state = new StoredLicense(Guid.NewGuid().ToString(), origin);
        try
        {
            if (File.Exists(statePath))
            {
                if (new FileInfo(statePath).Length > 131072) throw new FormatException("License cache exceeds its size limit.");
                var plain = WindowsSecretStore.Unprotect(File.ReadAllBytes(statePath));
                try
                {
                    var saved = JsonSerializer.Deserialize<StoredLicense>(plain, LeaseVerifier.Json);
                    if (saved is null || !Guid.TryParse(saved.InstallationId, out _)) throw new FormatException("Invalid installation identity.");
                    if (saved.ServerOrigin == origin) state = saved;
                }
                finally { CryptographicOperations.ZeroMemory(plain); }
            }
        }
        catch (Exception error) when (error is Win32Exception or CryptographicException or JsonException or FormatException
            or IOException or UnauthorizedAccessException)
        {
            CacheWarning = "License cache could not be read. Free tools and recovery remain available; reactivate this device.";
            try { File.Move(statePath, statePath + ".unreadable-" + Guid.NewGuid().ToString("N")); }
            catch (Exception moveError) when (moveError is IOException or UnauthorizedAccessException) { }
        }
        try { Save(); }
        catch (Exception error) when (error is Win32Exception or IOException or UnauthorizedAccessException)
        { CacheWarning = "License cache cannot be saved. Free tools and recovery remain available."; }
    }

    public LicenseStatus CachedStatus()
    {
        try
        {
            if (state.Lease is null) return new(false, "Free edition");
            var verified = LeaseVerifier.Verify(state.Lease, GetKey(state.Lease.KeyId), InstallationId, DateTimeOffset.UtcNow);
            return new(true, "Premium · verified", verified.ExpiresAt);
        }
        catch (Exception error) when (error is CryptographicException or FormatException or JsonException)
        { return new(false, error.Message); }
    }

    public async Task<LicenseStatus> RefreshAsync(CancellationToken cancellationToken = default)
    {
        await operations.WaitAsync(cancellationToken);
        try { return await RefreshCoreAsync(cancellationToken); }
        finally { operations.Release(); }
    }

    private async Task<LicenseStatus> RefreshCoreAsync(CancellationToken cancellationToken)
    {
        if (state.DeviceToken is null) return CachedStatus();
        try
        {
            var response = await RequestAsync<RefreshResponse>(HttpMethod.Post, "/api/desktop/refresh",
                new { }, state.DeviceToken, cancellationToken);
            var key = GetKey(response.Lease.KeyId);
            LeaseVerifier.Verify(response.Lease, key, InstallationId, DateTimeOffset.UtcNow);
            state = state with { Lease = response.Lease };
            Save();
            return CachedStatus();
        }
        catch (LicenseApiException error) when (error.StatusCode is 400 or 401 or 403 or 404 or 409 or 410)
        {
            state = state with { DeviceToken = null, Lease = null };
            Save();
            return new(false, error.Message);
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or LicenseApiException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cached = CachedStatus();
            return cached with { Message = cached.IsPremium
                ? "Offline · using your verified license until expiry"
                : "Connect to your license server to refresh activation" };
        }
    }

    public Task<DeviceRequest> StartAsync(CancellationToken cancellationToken = default) =>
        RequestAsync<DeviceRequest>(HttpMethod.Post, "/api/desktop/start",
            new { deviceName = Environment.MachineName, installationId = InstallationId }, null, cancellationToken);

    public async Task<LicenseStatus?> PollAsync(string deviceCode, CancellationToken cancellationToken = default)
    {
        await operations.WaitAsync(cancellationToken);
        try { return await PollCoreAsync(deviceCode, cancellationToken); }
        finally { operations.Release(); }
    }

    private async Task<LicenseStatus?> PollCoreAsync(string deviceCode, CancellationToken cancellationToken)
    {
        var response = await RequestAsync<PollResponse>(HttpMethod.Post, "/api/desktop/poll",
            new { deviceCode }, null, cancellationToken);
        if (response.Status == "pending") return null;
        if (response.Status == "expired") throw new LicenseApiException("Activation request expired. Start again.");
        if (response.Status != "approved" || response.DeviceToken is null || response.Lease is null)
            throw new LicenseApiException("Invalid activation response.");
        // Persist the one-time token so a supported-key update can recover via refresh.
        state = state with { DeviceToken = response.DeviceToken, Lease = null };
        Save();
        var key = GetKey(response.Lease.KeyId);
        LeaseVerifier.Verify(response.Lease, key, InstallationId, DateTimeOffset.UtcNow);
        state = state with { DeviceToken = response.DeviceToken, Lease = response.Lease };
        Save();
        return CachedStatus();
    }

    public Uri VerificationUri(DeviceRequest request)
    {
        var uri = new Uri(ServerUri, request.VerificationUri);
        if (uri.GetLeftPart(UriPartial.Authority) != origin || uri.AbsolutePath != "/activate-device")
            throw new LicenseApiException("Unexpected activation destination.");
        // The browser receives only the short user code, never the polling/device token.
        var builder = new UriBuilder(uri) { Query = "userCode=" + Uri.EscapeDataString(request.UserCode) };
        return builder.Uri;
    }

    private SigningKey GetKey(string keyId)
    {
        // Trust comes from the publisher's build, never from mutable cached state or a response.
        return !string.IsNullOrEmpty(keyId) && trustedKeys.TryGetValue(keyId, out var key) ? key
            : throw new CryptographicException("Unknown license signing key. Install a trusted client update.");
    }

    private async Task<T> RequestAsync<T>(HttpMethod method, string path, object? body, string? token,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonContent.Create(body, options: LeaseVerifier.Json);
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new LicenseApiException($"License server returned {(int)response.StatusCode}.", (int)response.StatusCode);
        var envelope = await response.Content.ReadFromJsonAsync<Envelope<T>>(LeaseVerifier.Json, cancellationToken);
        if (envelope is null || envelope.Code != 0 || envelope.Data is null)
            throw new LicenseApiException(envelope?.Message ?? "Empty response from license server.", 400);
        return envelope.Data;
    }

    private void Save()
    {
        var plain = JsonSerializer.SerializeToUtf8Bytes(state, LeaseVerifier.Json);
        try
        {
            var protectedBytes = WindowsSecretStore.Protect(plain);
            var temporary = statePath + ".tmp";
            File.WriteAllBytes(temporary, protectedBytes);
            File.Move(temporary, statePath, overwrite: true);
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    public void Dispose() => http.Dispose();
}
