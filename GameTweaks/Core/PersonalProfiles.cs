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

    // ---- Published config files (.spudboost) ----

    public const string FileExtension = ".spudboost";
    const string FormatName = "spudboost-config";

    sealed class ConfigFile
    {
        public string Format { get; set; } = FormatName;
        public int Version { get; set; } = 1;
        public PersonalProfile? Profile { get; set; }
    }

    /// <summary>Writes a profile to a .spudboost file anyone with SpudBoost can install.</summary>
    public static void Export(PersonalProfile profile, string path) =>
        File.WriteAllText(path, JsonSerializer.Serialize(new ConfigFile { Profile = profile }, new JsonSerializerOptions { WriteIndented = true }));

    /// <summary>
    /// Reads a .spudboost file. Throws FormatException if it isn't a valid SpudBoost config. The result is only data:
    /// nothing changes until the user loads it (with a preview), and loading only uses SpudBoost's own tweaks and known settings.
    /// </summary>
    public static PersonalProfile Import(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw new FormatException("That config file doesn't exist.");
        if (info.Length > 512 * 1024) throw new FormatException("That file is too big to be a SpudBoost config.");
        ConfigFile? file;
        try { file = JsonSerializer.Deserialize<ConfigFile>(File.ReadAllText(path)); }
        catch (JsonException) { throw new FormatException("That isn't a SpudBoost config file (or it's damaged)."); }
        if (file is not { Format: FormatName, Profile: { } p }) throw new FormatException("That isn't a SpudBoost config file.");
        if (file.Version > 1) throw new FormatException("That config was made with a newer SpudBoost. Update SpudBoost and try again.");

        p.Name = string.IsNullOrWhiteSpace(p.Name) ? Path.GetFileNameWithoutExtension(path) : p.Name.Trim();
        if (p.Name.Length > 60) p.Name = p.Name[..60];
        p.Tweaks = p.Tweaks.Where(id => id.Length is > 0 and < 64).Distinct().Take(200).ToList();
        p.Saved = DateTime.Now;
        return p;
    }
}
