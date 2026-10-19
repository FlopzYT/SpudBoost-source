using System.IO;
using System.Text.Json;

namespace GameTweaks.Core;

/// <summary>The original state of one registry value, captured before a tweak changed it.</summary>
public sealed class RegistryBackup
{
    public string Hive { get; set; } = "";
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Existed { get; set; }
    public string Kind { get; set; } = "";
    public string? Value { get; set; }
}

public sealed class BackupData
{
    /// <summary>Tweak id -> registry values as they were before the tweak was applied.</summary>
    public Dictionary<string, List<RegistryBackup>> Registry { get; set; } = new();

    /// <summary>Free-form originals for non-registry tweaks (power plan, DNS, ...).</summary>
    public Dictionary<string, string> Values { get; set; } = new();
}

/// <summary>Persists originals to %LOCALAPPDATA%\GameTweaks\backup.json so every tweak can be undone.</summary>
public static class BackupStore
{
    // Still "GameTweaks" (the app's original name) so existing backups and settings keep working.
    public static string Folder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GameTweaks");

    static string FilePath => Path.Combine(Folder, "backup.json");

    static BackupData? _data;
    public static BackupData Data => _data ??= Load();

    static BackupData Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<BackupData>(File.ReadAllText(FilePath)) ?? new();
        }
        catch (JsonException) { }
        return new();
    }

    public static void Save()
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(Data, new JsonSerializerOptions { WriteIndented = true }));
    }
}
