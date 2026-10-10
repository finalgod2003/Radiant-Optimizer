using System.Text;

namespace Radiant.Games;

internal sealed class ConfigDocument
{
    private sealed record ValueSpan(int Start, int Length, string Value);
    private readonly Dictionary<string, List<ValueSpan>> values = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<ValueSpan, string> replacements = [];
    private readonly Encoding encoding;
    private readonly byte[] preamble;
    private readonly string text;
    private ConfigDocument(byte[] bytes)
    {
        encoding = new UTF8Encoding(false, true); preamble = [];
        if (bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf })) preamble = [0xef, 0xbb, 0xbf];
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xff, 0xfe })) { encoding = new UnicodeEncoding(false, false, true); preamble = [0xff, 0xfe]; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xfe, 0xff })) { encoding = new UnicodeEncoding(true, false, true); preamble = [0xfe, 0xff]; }
        try { text = encoding.GetString(bytes, preamble.Length, bytes.Length - preamble.Length); }
        catch (DecoderFallbackException) { throw new GameProfileException("Unsupported or invalid configuration text encoding."); }
        if (text.Contains('\0') || text.Length > SafeFiles.MaximumBytes || text.Count(c => c == '\n') > 30000)
            throw new GameProfileException("Configuration is binary or exceeds the supported size.");
    }

    private static string Address(string section, string key) => section + "\0" + key;
    private void Add(string section, string key, int start, int length)
    {
        var address = Address(section, key);
        if (!values.TryGetValue(address, out var found)) values.Add(address, found = []);
        found.Add(new(start, length, text.Substring(start, length)));
    }

    internal static ConfigDocument Ini(byte[] bytes)
    {
        var document = new ConfigDocument(bytes);
        var section = "";
        var sections = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var offset = 0;
        foreach (var line in document.text.Split('\n'))
        {
            var content = line.TrimEnd('\r');
            var trimmed = content.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith(';') || trimmed.StartsWith('#')) { offset += line.Length + 1; continue; }
            if (trimmed.StartsWith('['))
            {
                if (!trimmed.EndsWith(']') || trimmed.Length < 3) throw new GameProfileException("Malformed INI section.");
                section = trimmed[1..^1];
                if (!sections.Add(section)) throw new GameProfileException("Duplicate INI sections are unsupported.");
            }
            else
            {
                var equal = content.IndexOf('=');
                if (equal < 1 || section.Length == 0) throw new GameProfileException("Malformed or unsectioned INI value.");
                var key = content[..equal].Trim();
                if (key.Length == 0 || key.Contains('\0')) throw new GameProfileException("Malformed INI key.");
                var start = equal + 1;
                while (start < content.Length && char.IsWhiteSpace(content[start])) start++;
                var end = content.Length;
                // Only simple numeric/bool values are modified. Quoted or structured unknown values remain untouched.
                var semicolon = content.IndexOf(';', start);
                if (semicolon >= 0 && (semicolon == start || char.IsWhiteSpace(content[semicolon - 1]))) end = semicolon;
                while (end > start && char.IsWhiteSpace(content[end - 1])) end--;
                document.Add(section, key, offset + start, end - start);
            }
            offset += line.Length + 1;
        }
        return document;
    }

    internal static ConfigDocument ValveVideo(byte[] bytes)
    {
        var document = new ConfigDocument(bytes);
        var source = document.text;
        var position = 0;
        (string Text, int Start, int Length) Token()
        {
            while (position < source.Length)
            {
                if (char.IsWhiteSpace(source[position])) { position++; continue; }
                if (position + 1 < source.Length && source[position] == '/' && source[position + 1] == '/')
                { while (position < source.Length && source[position] != '\n') position++; continue; }
                break;
            }
            if (position == source.Length) return ("", position, 0);
            if (source[position] is '{' or '}') return (source[position++].ToString(), position - 1, 1);
            if (source[position++] != '"') throw new GameProfileException("Unsupported KeyValues token, directive or condition.");
            var start = position;
            while (position < source.Length && source[position] != '"')
            {
                if (source[position] is '\r' or '\n' or '\\') throw new GameProfileException("Unsupported escaped or multiline KeyValues value.");
                position++;
            }
            if (position == source.Length) throw new GameProfileException("Unclosed KeyValues string.");
            var length = position - start;
            position++;
            return (source.Substring(start, length), start, length);
        }
        if (Token().Text != "video.cfg" || Token().Text != "{") throw new GameProfileException("Expected a video.cfg KeyValues root.");
        while (true)
        {
            var key = Token();
            if (key.Text == "}") break;
            if (key.Text is "" or "{") throw new GameProfileException("Malformed KeyValues root.");
            var value = Token();
            if (value.Text is "{" or "}" || value.Length == 0) throw new GameProfileException("Nested or empty video settings are unsupported.");
            document.Add("video.cfg", key.Text, value.Start, value.Length);
        }
        if (Token().Text.Length != 0) throw new GameProfileException("Unexpected content after the video.cfg root.");
        return document;
    }

    internal bool Has(string section, string key) => values.ContainsKey(Address(section, key));
    internal string Get(string section, string key)
    {
        if (!values.TryGetValue(Address(section, key), out var found) || found.Count != 1)
            throw new GameProfileException($"Required setting is missing or duplicated: {section} / {key}.");
        foreach (var modifier in new[] { "+", "-", "!", "." })
            if (Has(section, modifier + key)) throw new GameProfileException($"Ambiguous INI command for {key}.");
        return found[0].Value;
    }
    internal void Set(string section, string key, string after, List<GameSettingChange> changes)
    {
        var before = Get(section, key);
        if (before == after) return;
        replacements.Add(values[Address(section, key)][0], after);
        changes.Add(new(section, key, before, after));
    }
    internal byte[] Render()
    {
        var builder = new StringBuilder(text);
        foreach (var (span, value) in replacements.OrderByDescending(pair => pair.Key.Start))
            builder.Remove(span.Start, span.Length).Insert(span.Start, value);
        return [.. preamble, .. encoding.GetBytes(builder.ToString())];
    }
}
