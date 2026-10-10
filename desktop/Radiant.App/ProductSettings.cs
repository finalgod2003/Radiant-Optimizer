using System.IO;
using System.Text.Json;
using System.Reflection;
using Radiant.Licensing;

namespace Radiant.App;

public sealed record ProductSettings(string AppName, string ServerUrl, string Channel)
{
    public static SigningKey[] TrustedKeys()
    {
        using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("Optimizer.TrustedLicenseKeys");
        return resource is null ? [] : JsonSerializer.Deserialize<SigningKey[]>(resource,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? [];
    }

    public static ProductSettings Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "desktop-settings.local.json");
        var settings = JsonSerializer.Deserialize<ProductSettings>(File.ReadAllText(path),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException("Desktop configuration is missing. Run pnpm desktop:build.");
        if (string.IsNullOrWhiteSpace(settings.AppName)) throw new InvalidDataException("An app name is required.");
        return settings;
    }
}
