using System.Windows.Input;

namespace GameTweaks.Crosshair;

/// <summary>Turns text like "2, 6, Mouse4, F" into Windows virtual-key codes.</summary>
public static class KeyList
{
    static readonly Dictionary<string, int> MouseKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Mouse3"] = 0x04, ["MiddleMouse"] = 0x04, ["Mouse4"] = 0x05, ["Mouse5"] = 0x06,
    };

    static readonly KeyConverter Converter = new();

    public static List<int> Parse(string text)
    {
        var codes = new List<int>();
        foreach (var token in text.Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (MouseKeys.TryGetValue(token, out var mouse)) codes.Add(mouse);
            else if (TryKey(token, out var key)) codes.Add(KeyInterop.VirtualKeyFromKey(key));
        }
        return codes;
    }

    /// <summary>Accepts "2" as well as WPF names like "D2", "F", "F1", "LeftShift".</summary>
    static bool TryKey(string token, out Key key)
    {
        key = Key.None;
        var name = token.Length == 1 && char.IsDigit(token[0]) ? "D" + token : token;
        try
        {
            if (Converter.ConvertFromInvariantString(name) is Key k && k != Key.None) { key = k; return true; }
        }
        catch (ArgumentException) { }
        catch (NotSupportedException) { }
        return false;
    }
}
