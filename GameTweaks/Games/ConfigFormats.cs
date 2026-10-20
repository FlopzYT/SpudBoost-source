using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace GameTweaks.Games;

/// <summary>
/// Reads and edits one kind of game settings file. Edits only replace the values of keys that already exist,
/// so everything else in the file (and any setting SpudBoost doesn't know) is kept exactly as it was.
/// </summary>
public interface IConfigFormat
{
    /// <summary>Value of each key present in the text (raw, without quotes).</summary>
    Dictionary<string, string> Read(string text);

    /// <summary>The text with the given keys' values replaced (keys that aren't present are ignored).</summary>
    string Write(string text, IReadOnlyDictionary<string, string> values);
}

/// <summary>Minecraft options.txt: one "key:value" per line. Keeps quotes if the value had them.</summary>
public sealed class ColonFormat : IConfigFormat
{
    static readonly Regex Line = new(@"^(?<key>[A-Za-z0-9_.]+):(?<value>.*)$", RegexOptions.Multiline);

    public Dictionary<string, string> Read(string text) =>
        Line.Matches(text).GroupBy(m => m.Groups["key"].Value)
            .ToDictionary(g => g.Key, g => g.First().Groups["value"].Value.TrimEnd('\r').Trim('"'));

    public string Write(string text, IReadOnlyDictionary<string, string> values) =>
        Line.Replace(text, m =>
        {
            var key = m.Groups["key"].Value;
            if (!values.TryGetValue(key, out var value)) return m.Value;
            var old = m.Groups["value"].Value;
            var cr = old.EndsWith('\r') ? "\r" : "";
            var quoted = old.TrimEnd('\r').StartsWith('"');
            return $"{key}:{(quoted ? $"\"{value}\"" : value)}{cr}";
        });
}

/// <summary>Valve KeyValues files like CS2's cs2_video.txt: "key"  "value".</summary>
public sealed class ValveKvFormat : IConfigFormat
{
    static readonly Regex Pair = new("^(?<pre>\\s*\"(?<key>[^\"]+)\"\\s+\")(?<value>[^\"]*)(?<post>\".*)$", RegexOptions.Multiline);

    public Dictionary<string, string> Read(string text) =>
        Pair.Matches(text).GroupBy(m => m.Groups["key"].Value).ToDictionary(g => g.Key, g => g.First().Groups["value"].Value);

    public string Write(string text, IReadOnlyDictionary<string, string> values) =>
        Pair.Replace(text, m => values.TryGetValue(m.Groups["key"].Value, out var v)
            ? m.Groups["pre"].Value + v + m.Groups["post"].Value
            : m.Value);
}

/// <summary>Roblox GlobalBasicSettings XML: &lt;int name="key"&gt;value&lt;/int&gt; (also token, bool, float, string).</summary>
public sealed class RobloxXmlFormat : IConfigFormat
{
    static readonly Regex Item = new(@"(?<open><(?<type>int|token|bool|float|string) name=""(?<key>[^""]+)"">)(?<value>[^<]*)(?<close></\k<type>>)");

    public Dictionary<string, string> Read(string text) =>
        Item.Matches(text).GroupBy(m => m.Groups["key"].Value).ToDictionary(g => g.Key, g => g.First().Groups["value"].Value);

    public string Write(string text, IReadOnlyDictionary<string, string> values) =>
        Item.Replace(text, m => values.TryGetValue(m.Groups["key"].Value, out var v)
            ? m.Groups["open"].Value + v + m.Groups["close"].Value
            : m.Value);
}

/// <summary>Reads and writes text files byte-for-byte compatible: same encoding and byte-order mark as before.</summary>
public static class TextFile
{
    public static (string Text, Encoding Encoding) Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        Encoding encoding =
            bytes is [0xFF, 0xFE, ..] ? new UnicodeEncoding(false, true) :
            bytes is [0xEF, 0xBB, 0xBF, ..] ? new UTF8Encoding(true) :
            new UTF8Encoding(false);
        var bom = encoding.GetPreamble().Length;
        return (encoding.GetString(bytes, bom, bytes.Length - bom), encoding);
    }

    public static void Write(string path, string text, Encoding encoding) =>
        File.WriteAllBytes(path, [.. encoding.GetPreamble(), .. encoding.GetBytes(text)]);
}
