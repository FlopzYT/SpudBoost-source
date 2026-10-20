using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using GameTweaks.Core;

namespace GameTweaks.Games;

/// <summary>
/// One game's settings page: finds its settings file(s), shows the settings that exist in them, applies presets,
/// and saves with a backup. The very first backup of each file is kept forever as the original.
/// </summary>
public sealed class GameProfile : ObservableObject
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Emoji { get; init; }
    public required string Description { get; init; }
    /// <summary>Process names (without .exe); saving is refused while any is running.</summary>
    public required string[] Processes { get; init; }
    /// <summary>Every settings file for this game on this PC (e.g. one per Steam account).</summary>
    public required Func<List<string>> FindConfigs { get; init; }
    public required IConfigFormat Format { get; init; }
    public required GameSettingDef[] Settings { get; init; }
    /// <summary>Preset name -> (key -> value). Toggles use "on"/"off". Keys missing from the file are skipped.</summary>
    public required Dictionary<string, Dictionary<string, string>> Presets { get; init; }
    /// <summary>Optional: adjusts values before saving (e.g. keep two related keys in step).</summary>
    public Action<Dictionary<string, string>>? BeforeSave { get; init; }
    /// <summary>The game's executables, for per-game Windows tweaks.</summary>
    public Func<List<string>> FindExes { get; init; } = () => [];

    public string NavLabel => $"{Emoji}  {Name}";
    public List<string> PresetNames => Presets.Keys.ToList();

    List<GameSetting> _loaded = [];
    public List<GameSetting> Loaded { get => _loaded; private set => SetField(ref _loaded, value); }

    string _status = "";
    public string Status { get => _status; private set => SetField(ref _status, value); }

    public bool Installed => FindConfigs().Count > 0;
    public bool Running => Processes.Any(p => Process.GetProcessesByName(p).Length > 0);

    static string BackupFolder(string id) => Path.Combine(BackupStore.Folder, "Games", id);

    /// <summary>Stable short name for a config path, so several files (e.g. Steam accounts) get separate backups.</summary>
    static string FileTag(string path) =>
        Path.GetFileNameWithoutExtension(path) + "-" + Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(path.ToLowerInvariant())))[..8];

    /// <summary>Reads the first settings file and lists the known settings it contains.</summary>
    public void Load()
    {
        var configs = FindConfigs();
        if (configs.Count == 0)
        {
            Loaded = [];
            Status = $"{Name}'s settings file wasn't found. Start the game once, change any setting, then come back.";
            return;
        }
        var values = Format.Read(TextFile.Read(configs[0]).Text);
        Loaded = Settings.Where(d => values.ContainsKey(d.Key)).Select(d => new GameSetting(d, values[d.Key])).ToList();
        Status = configs.Count > 1 ? $"{configs.Count} settings files found (e.g. one per account); saving updates all of them." : "";
    }

    public void ApplyPreset(string preset)
    {
        foreach (var (key, value) in Presets[preset])
            Loaded.FirstOrDefault(s => s.Def.Key == key)?.Set(value);
    }

    /// <summary>Current values, e.g. for saving a personal profile.</summary>
    public Dictionary<string, string> Snapshot() => Loaded.ToDictionary(s => s.Def.Key, s => s.Raw);

    /// <summary>Loads values from a saved profile into the editor (not saved until <see cref="Save"/>).</summary>
    public void ApplySnapshot(IReadOnlyDictionary<string, string> values)
    {
        foreach (var s in Loaded)
            if (values.TryGetValue(s.Def.Key, out var v)) s.Set(s.Kind == SettingKind.Toggle ? (v is "true" or "1" ? "on" : "off") : v);
    }

    public void Save()
    {
        if (Running) throw new InvalidOperationException($"Close {Name} first. It rewrites its settings when it exits and would undo your changes.");
        var values = Snapshot();
        BeforeSave?.Invoke(values);

        var folder = BackupFolder(Id);
        Directory.CreateDirectory(folder);
        foreach (var config in FindConfigs())
        {
            var tag = FileTag(config);
            var original = Path.Combine(folder, $"{tag}.original{Path.GetExtension(config)}");
            if (!File.Exists(original)) File.Copy(config, original);
            File.Copy(config, Path.Combine(folder, $"{tag}.{DateTime.Now:yyyyMMdd-HHmmss}{Path.GetExtension(config)}"), true);

            var info = new FileInfo(config);
            if (info.IsReadOnly) info.IsReadOnly = false;
            var (text, encoding) = TextFile.Read(config);
            TextFile.Write(config, Format.Write(text, values), encoding);
        }
    }

    public bool HasOriginalBackup =>
        FindConfigs().Any(c => File.Exists(Path.Combine(BackupFolder(Id), $"{FileTag(c)}.original{Path.GetExtension(c)}")));

    public void RestoreOriginal()
    {
        if (Running) throw new InvalidOperationException($"Close {Name} first.");
        var restored = 0;
        foreach (var config in FindConfigs())
        {
            var original = Path.Combine(BackupFolder(Id), $"{FileTag(config)}.original{Path.GetExtension(config)}");
            if (!File.Exists(original)) continue;
            var info = new FileInfo(config);
            if (info.IsReadOnly) info.IsReadOnly = false;
            File.Copy(original, config, true);
            restored++;
        }
        if (restored == 0) throw new InvalidOperationException($"SpudBoost hasn't changed {Name}'s settings yet, so there's nothing to restore.");
    }
}
