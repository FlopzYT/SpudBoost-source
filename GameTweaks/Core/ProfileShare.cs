using System.IO;
using System.IO.Compression;
using System.Text.Json;
using GameTweaks.Fortnite;

namespace GameTweaks.Core;

/// <summary>Everything a SpudBoost profile code carries. Tweaks are referenced by ID only, so a code can never do anything SpudBoost can't.</summary>
public sealed class SharedProfile
{
    public string Name { get; set; } = "";
    public List<string> Tweaks { get; set; } = [];
    public FortniteSnapshot? Fortnite { get; set; }
    /// <summary>A crosshair share code (SPUD1:...), or null.</summary>
    public string? Crosshair { get; set; }

    public string Summary =>
        string.Join(" · ", new[]
        {
            Tweaks.Count > 0 ? $"{Tweaks.Count} tweak{(Tweaks.Count == 1 ? "" : "s")}" : null,
            Fortnite != null ? "Fortnite graphics" : null,
            Crosshair != null ? "crosshairs" : null,
        }.Where(s => s != null));
}

/// <summary>A saved friend and the last profile code they sent.</summary>
public sealed class Friend
{
    public string Name { get; set; } = "";
    public string Code { get; set; } = "";
    public DateTime Updated { get; set; } = DateTime.Now;

    public string Summary
    {
        get
        {
            try { return ProfileShare.Decode(Code).Summary + $" · saved {Updated:MMM d}"; }
            catch (FormatException) { return "Invalid code"; }
        }
    }
}

/// <summary>Profile codes ("SPUDP1:..."): your whole SpudBoost setup as text you can send to friends.</summary>
public static class ProfileShare
{
    const string Prefix = "SPUDP1:";

    public static string Encode(SharedProfile profile)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(profile);
        using var buffer = new MemoryStream();
        using (var deflate = new DeflateStream(buffer, CompressionLevel.SmallestSize)) deflate.Write(json);
        return Prefix + Convert.ToBase64String(buffer.ToArray());
    }

    public static SharedProfile Decode(string code)
    {
        code = code.Trim();
        if (!code.StartsWith(Prefix, StringComparison.Ordinal))
            throw new FormatException(code.StartsWith("SPUD1:", StringComparison.Ordinal)
                ? "That's a crosshair code. Paste it on the Crosshair page instead."
                : "That isn't a SpudBoost profile code (they start with SPUDP1:).");
        try
        {
            using var input = new DeflateStream(new MemoryStream(Convert.FromBase64String(code[Prefix.Length..])), CompressionMode.Decompress);
            return JsonSerializer.Deserialize<SharedProfile>(input) ?? throw new FormatException("That profile code is empty.");
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or IOException)
        {
            throw new FormatException("That profile code is damaged or incomplete. Copy it again.", ex);
        }
    }
}
