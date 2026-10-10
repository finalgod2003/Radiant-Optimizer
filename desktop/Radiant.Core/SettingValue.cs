using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Radiant.Core;

[JsonConverter(typeof(JsonStringEnumConverter<SettingValueKind>))]
public enum SettingValueKind
{
    Missing,
    DWord,
    QWord,
    String,
    ExpandString,
    MultiString,
    Binary,
    None,
    PowerPlan,
}

/// <summary>
/// Canonical data preserves registry types, unexpanded strings, binary bytes,
/// and absence. Equality compares the value, not an object or array reference.
/// </summary>
public sealed record SettingValue(bool Exists, SettingValueKind Kind, string Data)
{
    public static SettingValue Missing { get; } = new(false, SettingValueKind.Missing, "");
    public static SettingValue DWord(int value) => new(true, SettingValueKind.DWord, value.ToString(CultureInfo.InvariantCulture));
    public static SettingValue QWord(long value) => new(true, SettingValueKind.QWord, value.ToString(CultureInfo.InvariantCulture));
    public static SettingValue String(string value, bool expandable = false) => new(true, expandable ? SettingValueKind.ExpandString : SettingValueKind.String, value);
    public static SettingValue MultiString(string[] value) => new(true, SettingValueKind.MultiString, JsonSerializer.Serialize(value));
    public static SettingValue Binary(byte[] value, bool untyped = false) => new(true, untyped ? SettingValueKind.None : SettingValueKind.Binary, Convert.ToBase64String(value));
    public static SettingValue PowerPlan(Guid value) => new(true, SettingValueKind.PowerPlan, value.ToString("D"));

    public void Validate()
    {
        if (Data is null || !Enum.IsDefined(Kind) || Exists == (Kind == SettingValueKind.Missing))
            throw new InvalidDataException("The saved setting value is invalid.");

        switch (Kind)
        {
            case SettingValueKind.Missing when Data.Length != 0:
                throw new InvalidDataException("A missing setting must not contain data.");
            case SettingValueKind.DWord:
                if (!int.TryParse(Data, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                    throw new InvalidDataException("The saved DWORD is invalid.");
                break;
            case SettingValueKind.QWord:
                if (!long.TryParse(Data, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                    throw new InvalidDataException("The saved QWORD is invalid.");
                break;
            case SettingValueKind.MultiString:
                try
                {
                    var items = JsonSerializer.Deserialize<string[]>(Data);
                    if (items is null || items.Any(item => item is null))
                        throw new InvalidDataException("The saved string array is invalid.");
                }
                catch (JsonException error)
                {
                    throw new InvalidDataException("The saved string array is invalid.", error);
                }
                break;
            case SettingValueKind.Binary:
            case SettingValueKind.None:
                try { _ = Convert.FromBase64String(Data); }
                catch (FormatException error) { throw new InvalidDataException("The saved binary value is invalid.", error); }
                break;
            case SettingValueKind.PowerPlan:
                if (!Guid.TryParseExact(Data, "D", out _))
                    throw new InvalidDataException("The saved power plan identifier is invalid.");
                break;
        }
    }

    public string ToDisplayString() => Exists ? Kind switch
    {
        SettingValueKind.String or SettingValueKind.ExpandString => $"{Kind}: \"{Data}\"",
        SettingValueKind.Binary or SettingValueKind.None => $"{Kind}: {Convert.FromBase64String(Data).Length} bytes ({Data})",
        _ => $"{Kind}: {Data}",
    } : "Not set (Windows default)";
}
