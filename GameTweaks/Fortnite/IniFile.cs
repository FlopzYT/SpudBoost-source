using System.IO;
using System.Text;

namespace GameTweaks.Fortnite;

/// <summary>
/// Line-preserving INI editor. Unreal config files repeat keys and hold long structured values, so this
/// only touches the exact lines being changed and writes everything else back byte-for-byte.
/// </summary>
public sealed class IniFile
{
    readonly List<string> _lines;
    readonly Encoding _encoding;
    readonly string _newline;

    public string Path { get; }

    IniFile(string path, List<string> lines, Encoding encoding, string newline)
    {
        Path = path;
        _lines = lines;
        _encoding = encoding;
        _newline = newline;
    }

    public static IniFile Load(string path)
    {
        var bytes = File.ReadAllBytes(path);
        Encoding encoding =
            bytes is [0xFF, 0xFE, ..] ? new UnicodeEncoding(false, true) :
            bytes is [0xEF, 0xBB, 0xBF, ..] ? new UTF8Encoding(true) :
            new UTF8Encoding(false);
        var bom = encoding.GetPreamble().Length;
        var text = encoding.GetString(bytes, bom, bytes.Length - bom);
        var newline = text.Contains("\r\n") ? "\r\n" : "\n";
        return new IniFile(path, text.Split(newline).ToList(), encoding, newline);
    }

    /// <summary>Index range of the lines inside a section (after its header), or null if absent.</summary>
    (int Start, int End)? Section(string section)
    {
        var header = $"[{section}]";
        var start = _lines.FindIndex(l => l.Trim() == header);
        if (start < 0) return null;
        var end = start + 1;
        while (end < _lines.Count && !_lines[end].TrimStart().StartsWith('[')) end++;
        return (start + 1, end);
    }

    int FindKey(int start, int end, string key)
    {
        for (var i = start; i < end; i++)
            if (_lines[i].StartsWith(key + "=", StringComparison.Ordinal)) return i;
        return -1;
    }

    public string? Get(string section, string key)
    {
        if (Section(section) is not { } range) return null;
        var i = FindKey(range.Start, range.End, key);
        return i < 0 ? null : _lines[i][(key.Length + 1)..];
    }

    public void Set(string section, string key, string value)
    {
        var line = $"{key}={value}";
        if (Section(section) is not { } range)
        {
            // Add the section before the trailing blank lines at the end of the file.
            var at = _lines.Count;
            while (at > 0 && _lines[at - 1].Length == 0) at--;
            _lines.InsertRange(at, ["", $"[{section}]", line]);
            return;
        }

        var (start, end) = range;
        var i = FindKey(start, end, key);
        if (i >= 0)
        {
            _lines[i] = line;
            return;
        }
        var insert = end;
        while (insert > start && _lines[insert - 1].Trim().Length == 0) insert--;
        _lines.Insert(insert, line);
    }

    public void Save()
    {
        var text = string.Join(_newline, _lines);
        File.WriteAllBytes(Path, [.. _encoding.GetPreamble(), .. _encoding.GetBytes(text)]);
    }
}
