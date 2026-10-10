using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Radiant.Licensing;

if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("The HTTP integration test exercises Windows DPAPI.");
string Required(string name) => Environment.GetEnvironmentVariable(name)
    ?? throw new InvalidOperationException("Run this project through tests/desktop-activation.run.ts.");
var server = new Uri(Required("OPTIMIZER_TEST_SERVER"));
if (!server.IsLoopback || server.AbsolutePath != "/") throw new InvalidOperationException("Integration tests only target a loopback origin.");
var directory = Required("OPTIMIZER_TEST_DIRECTORY");
var licenseId = Required("OPTIMIZER_TEST_LICENSE");
var installationIds = new List<string>();
var passed = 0;
void Check(bool condition, string description)
{
    if (!condition) throw new InvalidOperationException("FAIL " + description);
    passed++;
    Console.WriteLine("PASS " + description);
}

HttpClient Browser() => new(new HttpClientHandler { CookieContainer = new CookieContainer(), AllowAutoRedirect = false })
{ BaseAddress = server, Timeout = TimeSpan.FromSeconds(30) };

async Task<HttpResponseMessage> Send(HttpClient browser, HttpMethod method, string path, object? body = null, string? origin = null)
{
    using var request = new HttpRequestMessage(method, path);
    if (body is not null) request.Content = JsonContent.Create(body, options: LeaseVerifier.Json);
    if (origin is not null) request.Headers.Add("Origin", origin);
    return await browser.SendAsync(request);
}

async Task<JsonElement> Data(HttpResponseMessage response)
{
    using (response)
    {
        Check(response.IsSuccessStatusCode, "HTTP response succeeded");
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Check(json.RootElement.GetProperty("code").GetInt32() == 0, "API envelope succeeded");
        return json.RootElement.GetProperty("data").Clone();
    }
}

async Task Login(HttpClient browser, string emailVariable, string passwordVariable)
{
    using var response = await Send(browser, HttpMethod.Post, "/api/auth/sign-in/email",
        new { email = Required(emailVariable), password = Required(passwordVariable) }, server.GetLeftPart(UriPartial.Authority));
    Check(response.IsSuccessStatusCode, "real account login succeeded without exporting its session");
}

try
{
    using var buyer = Browser();
    using var other = Browser();
    using var anonymous = Browser();
    // The harness obtains the expected public trust roots once from its explicit
    // loopback test server. Subsequent native verification never trusts cached key data.
    var keySet = await Data(await Send(anonymous, HttpMethod.Get, "/api/desktop/keys"));
    var trustedKeys = JsonSerializer.Deserialize<SigningKey[]>(keySet.GetProperty("keys").GetRawText(), LeaseVerifier.Json)
        ?? throw new InvalidOperationException("Test server supplied no signing keys.");
    Check(trustedKeys.Length > 0, "integration harness fixes its expected server public keys before activation");
    await Login(buyer, "OPTIMIZER_TEST_EMAIL", "OPTIMIZER_TEST_PASSWORD");
    await Login(other, "OPTIMIZER_TEST_OTHER_EMAIL", "OPTIMIZER_TEST_OTHER_PASSWORD");
    var origin = server.GetLeftPart(UriPartial.Authority);
    var buyerLicenses = await Data(await Send(buyer, HttpMethod.Get, "/api/licenses"));
    Check(buyerLicenses.EnumerateArray().Any(item => item.GetProperty("id").GetString() == licenseId), "paid fixture is visible in the buyer account");
    var otherLicenses = await Data(await Send(other, HttpMethod.Get, "/api/licenses"));
    Check(otherLicenses.GetArrayLength() == 0, "another account cannot list the buyer license");
    using (var denied = await Send(anonymous, HttpMethod.Get, "/api/licenses"))
        Check(denied.StatusCode == HttpStatusCode.Unauthorized, "license list requires a web session");

    var primaryDirectory = Path.Combine(directory, "primary");
    using var primary = new LicenseClient(server.ToString(), primaryDirectory, trustedKeys);
    installationIds.Add(primary.InstallationId);
    Check(!primary.CachedStatus().IsPremium, "fresh actual client starts in Free edition");
    var started = await primary.StartAsync();
    Check(started.ExpiresAt > DateTimeOffset.UtcNow && started.ExpiresAt < DateTimeOffset.UtcNow.AddMinutes(6), "server grants a five-minute device request");
    Check(primary.VerificationUri(started).GetLeftPart(UriPartial.Authority) == origin, "actual client trusts the configured activation origin");
    Check(await primary.PollAsync(started.DeviceCode) is null, "actual client observes pending approval");
    var preview = await Data(await Send(buyer, HttpMethod.Get, "/api/desktop/approve?userCode=" + Uri.EscapeDataString(started.UserCode)));
    Check(preview.GetProperty("deviceName").GetString() == Environment.MachineName, "signed-in preview identifies the actual device");
    using (var denied = await Send(anonymous, HttpMethod.Get, "/api/desktop/approve?userCode=" + Uri.EscapeDataString(started.UserCode)))
        Check(denied.StatusCode == HttpStatusCode.Unauthorized, "device preview requires login");
    var approval = new { userCode = started.UserCode, licenseId };
    using (var denied = await Send(buyer, HttpMethod.Post, "/api/desktop/approve", approval))
        Check(denied.StatusCode == HttpStatusCode.Forbidden, "approval without Origin is rejected");
    using (var denied = await Send(buyer, HttpMethod.Post, "/api/desktop/approve", approval, "https://untrusted.example"))
        Check(denied.StatusCode == HttpStatusCode.Forbidden, "approval from another origin is rejected");
    using (var denied = await Send(other, HttpMethod.Post, "/api/desktop/approve", approval, origin))
        Check(denied.StatusCode == HttpStatusCode.NotFound, "another account cannot approve the buyer license");
    await Data(await Send(buyer, HttpMethod.Post, "/api/desktop/approve", approval, origin));
    await Task.Delay(TimeSpan.FromSeconds(started.IntervalSeconds + 0.2));
    var activated = await primary.PollAsync(started.DeviceCode);
    Check(activated?.IsPremium == true, "actual C# client verifies and accepts the server RSA lease");
    Check(activated?.ExpiresAt > DateTimeOffset.UtcNow.AddDays(6.99), "server lease grants the documented seven-day offline window");
    try
    {
        await primary.PollAsync(started.DeviceCode);
        throw new InvalidOperationException("One-time device code unexpectedly exchanged twice");
    }
    catch (LicenseApiException) { Check(true, "consumed device request cannot be exchanged again"); }
    var refreshed = await primary.RefreshAsync();
    Check(refreshed.IsPremium && refreshed.Message == "Premium · verified", "actual client refreshes online with its restricted device token");
    Check(refreshed.ExpiresAt > activated?.ExpiresAt, "online refresh issues a newer lease instead of using the offline cache");
    using (var restarted = new LicenseClient(server.ToString(), primaryDirectory, trustedKeys))
    {
        Check(restarted.InstallationId == primary.InstallationId, "installation identity survives a client restart");
        Check(restarted.CachedStatus().IsPremium, "DPAPI-protected persisted lease verifies after restart");
    }
    var encrypted = File.ReadAllBytes(Path.Combine(primaryDirectory, "device-license.dat"));
    Check(!System.Text.Encoding.UTF8.GetString(encrypted).Contains(primary.InstallationId, StringComparison.Ordinal), "actual client persists credentials through Windows DPAPI");

    using var secondary = new LicenseClient(server.ToString(), Path.Combine(directory, "secondary"), trustedKeys);
    installationIds.Add(secondary.InstallationId);
    var secondRequest = await secondary.StartAsync();
    using (var denied = await Send(buyer, HttpMethod.Post, "/api/desktop/approve", new { userCode = secondRequest.UserCode, licenseId }, origin))
        Check(denied.StatusCode == HttpStatusCode.Conflict, "server refuses a second device while its slot is occupied");
    using (var denied = await Send(other, HttpMethod.Post, "/api/licenses", new { action = "unbind", licenseId }, origin))
        Check(denied.StatusCode == HttpStatusCode.NotFound, "another account cannot unbind the buyer device");
    await Data(await Send(buyer, HttpMethod.Post, "/api/licenses", new { action = "unbind", licenseId }, origin));
    await Task.Delay(1100);
    Check(!(await primary.RefreshAsync()).IsPremium, "unbinding causes the actual client to reject its old token");
    Check(!primary.CachedStatus().IsPremium, "online revocation clears the previously cached premium lease");
    using (var restarted = new LicenseClient(server.ToString(), primaryDirectory, trustedKeys))
        Check(!restarted.CachedStatus().IsPremium, "revocation remains cleared after a client restart");
    await Data(await Send(buyer, HttpMethod.Post, "/api/desktop/approve", new { userCode = secondRequest.UserCode, licenseId }, origin));
    Check((await secondary.PollAsync(secondRequest.DeviceCode))?.IsPremium == true, "released slot permits activation on another real client installation");
    Console.WriteLine($"{passed} native/server HTTP checks passed. Payment was a local fixture, not a merchant charge.");
}
catch (Exception error)
{
    Console.Error.WriteLine(error.GetType().Name + ": " + error.Message);
    Environment.ExitCode = 1;
}
finally
{
    // Only non-secret installation IDs are returned for exact fixture cleanup.
    File.WriteAllText(Path.Combine(directory, "result.json"), JsonSerializer.Serialize(new { installationIds, passed }));
}
