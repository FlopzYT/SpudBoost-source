using System.IO;
using System.Text.Json;
using GameTweaks.Fortnite;

namespace GameTweaks.Core;

/// <summary>A named snapshot of your setup that you can switch back to later.</summary>
public sealed class PersonalProfile
{
    public string Name { get; set; } = "";
    public DateTime Saved { get; set; } = DateTime.Now;
    /// <summary>IDs of the tweaks that were on.</summary>
    public List<string> Tweaks { get; set; } = [];
    public bool IncludesTweaks { get; set; }
    public FortniteSnapshot? Fortnite { get; set; }
    /// <summary>Crosshair share code (SPUD1:...).</summary>
    public string? Crosshair { get; set; }
    /// <summary>Game id -> setting key -> value (Roblox, Minecraft, CS2).</summary>
    public Dictionary<string, Dictionary<string, string>> Games { get; set; } = new();

    public string Summary =>
        string.Join(" · ", new[]
        {
            IncludesTweaks ? $"{Tweaks.Count} tweak{(Tweaks.Count == 1 ? "" : "s")} on" : null,
            Fortnite != null ? "Fortnite" : null,
            Crosshair != null ? "crosshairs" : null,
            Games.Count > 0 ? string.Join(", ", Games.Keys.Select(k => k switch { "cs2" => "CS2", "roblox" => "Roblox", "minecraft" => "Minecraft", _ => k })) : null,
        }.Where(s => s != null)) + $" · saved {Saved:MMM d, h:mm tt}";
}

/// <summary>Your saved profiles, in profiles.json next to the other SpudBoost data (only on this PC).</summary>
public static class PersonalProfiles
{
    static string FilePath => Path.Combine(BackupStore.Folder, "profiles.json");

    public static List<PersonalProfile> Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<List<PersonalProfile>>(File.ReadAllText(FilePath)) ?? [];
        }
        catch (JsonException) { }
        return [];
    }

    public static void Save(List<PersonalProfile> profiles)
    {
        Directory.CreateDirectory(BackupStore.Folder);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(profiles, new JsonSerializerOptions { WriteIndented = true }));
    }
}
