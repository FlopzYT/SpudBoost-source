using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Xml.Linq;
using GameTweaks.Core;

namespace GameTweaks.Tweaks;

/// <summary>
/// Sets NVIDIA driver options on the driver profiles of your games, using NVIDIA Profile Inspector (open source, MIT).
/// The affected profiles are saved first. Undo puts back only this tweak's own settings, so several NVIDIA tweaks
/// on the same game can be switched on and off independently.
/// </summary>
public sealed class NvidiaProfileTweak : Tweak
{
    const string ToolUrl = "https://github.com/Orbmu2k/nvidiaProfileInspector/releases/download/v3.0.2.1/nvidiaProfileInspector.zip";

    /// <summary>Lower latency and fewer stutters (IDs from Profile Inspector's setting reference).</summary>
    public static readonly IReadOnlyDictionary<uint, uint> PerformanceSettings = new Dictionary<uint, uint>
    {
        [0x1057EB71] = 1,          // Power management mode: Prefer maximum performance
        [0x00AC8497] = 0xFFFFFFFF, // Shader cache size: Unlimited (fewer stutters from shader compiles)
        [0x0005F543] = 2,          // Low latency mode (Control Panel state): Ultra
        [0x10835000] = 1,          // Ultra low latency: enabled
        [0x007BA09E] = 1,          // Maximum pre-rendered frames: 1
    };

    /// <summary>Flatness levels for potato textures: 1 = Light (+1), 2 = Medium (+2), 3 = Max (+3).</summary>
    public static readonly string[] FlatnessLevels = ["Light (+1)", "Medium (+2)", "Max (+3)"];

    /// <summary>
    /// Blurry, flat "potato" textures: the driver uses lower-detail texture copies. Anisotropic filtering is left to the game,
    /// because forcing it off smears ground seen at an angle (hills, ramps) into mush for almost no FPS gain.
    /// </summary>
    public static IReadOnlyDictionary<uint, uint> PotatoTextureSettings(int flatness) => new Dictionary<uint, uint>
    {
        [0x00738E8F] = (uint)(Math.Clamp(flatness, 1, 3) * 8), // Texture LOD bias (DX) in 1/8 steps: +1, +2 or +3
        [0x00638E8F] = 0,  // Driver-controlled LOD bias: off, so the bias above is used
        [0x0019BB68] = 1,  // Negative LOD bias: clamp
        [0x00CE2691] = 20, // Texture filtering quality: High performance
        [0x002ECAF2] = 0,  // Trilinear optimization: on
    };

    /// <summary>Settings 1.3.1 and earlier forced (anisotropic filtering off); put back to the originals when applying or undoing.</summary>
    public static readonly uint[] RetiredPotatoSettings = [0x10D2BB16, 0x101E61A9, 0x0084CD70, 0x00E73211];

    /// <summary>The settings to apply, read at apply time (so e.g. the flatness level can change).</summary>
    public required Func<IReadOnlyDictionary<uint, uint>> Settings { get; init; }

    /// <summary>Settings this tweak no longer changes but older versions did; restored to their originals.</summary>
    public IReadOnlyCollection<uint> RetiredSettings { get; init; } = [];
    public required Func<IEnumerable<string>> GameProcesses { get; init; }

    /// <summary>File name (in the data folder) holding the profiles as they were before this tweak.</summary>
    public required string BackupFileName { get; init; }

    IReadOnlyDictionary<uint, uint> Desired => Settings();

    static string ToolFolder => Path.Combine(BackupStore.Folder, "Tools", "nvidiaProfileInspector");
    static string ToolExe => Path.Combine(ToolFolder, "nvidiaProfileInspector.exe");
    string OriginalBackup => Path.Combine(BackupStore.Folder, BackupFileName);

    public static bool HasNvidiaGpu => File.Exists(Path.Combine(Environment.SystemDirectory, "nvapi64.dll"));

    public override bool HasBackup => File.Exists(OriginalBackup);

    public override bool IsApplied()
    {
        // Only ask the driver once SpudBoost has made this change, so startup never runs the tool needlessly.
        if (!File.Exists(ToolExe) || !HasBackup) return false;
        var targets = TargetProfiles(Export());
        return targets.Count > 0 && targets.All(p => Desired.All(d => Value(p, d.Key) == d.Value));
    }

    protected override void ApplyCore()
    {
        EnsureTool();
        var targets = TargetProfiles(Export());
        if (targets.Count == 0)
            throw new InvalidOperationException(
                "None of your games have an NVIDIA profile SpudBoost can edit yet. Open NVIDIA Control Panel → Manage 3D settings → " +
                "Program settings, pick the game, change any one setting and click Apply. Then try again.");

        if (!HasBackup) Write(OriginalBackup, targets.Select(p => new XElement(p)));
        var originals = Originals();

        var desired = Desired;
        foreach (var profile in targets)
        {
            foreach (var (id, value) in desired)
                SetValue(profile, id, value);
            if (originals.TryGetValue(profile.Element("ProfileName")?.Value ?? "", out var original))
                foreach (var id in RetiredSettings)
                    RestoreValue(profile, original, id);
        }

        var import = Path.Combine(Path.GetTempPath(), $"SpudBoost-nvidia-{Guid.NewGuid():N}.nip");
        try
        {
            Write(import, targets);
            Import(import);
        }
        finally { File.Delete(import); }
    }

    /// <summary>Puts back only this tweak's settings (others' changes stay): original value, or removed if it wasn't set.</summary>
    protected override void RevertCore()
    {
        EnsureTool();
        var originals = Originals();
        var current = Export().Elements("Profile")
            .Where(p => originals.ContainsKey(p.Element("ProfileName")?.Value ?? ""))
            .ToList();

        foreach (var profile in current)
        {
            var original = originals[profile.Element("ProfileName")!.Value];
            foreach (var id in Desired.Keys.Concat(RetiredSettings))
                RestoreValue(profile, original, id);
        }

        if (current.Count > 0)
        {
            var import = Path.Combine(Path.GetTempPath(), $"SpudBoost-nvidia-{Guid.NewGuid():N}.nip");
            try
            {
                Write(import, current);
                Import(import);
            }
            finally { File.Delete(import); }
        }
        File.Delete(OriginalBackup);
    }

    /// <summary>The game profiles as they were before this tweak, by profile name.</summary>
    Dictionary<string, XElement> Originals() =>
        XElement.Parse(File.ReadAllText(OriginalBackup)).Elements("Profile")
            .GroupBy(p => p.Element("ProfileName")?.Value ?? "")
            .ToDictionary(g => g.Key, g => g.First());

    /// <summary>Puts one setting back to its value in <paramref name="original"/>, or removes it if it wasn't set there.</summary>
    static void RestoreValue(XElement profile, XElement original, uint id)
    {
        if (Value(original, id) is { } before) SetValue(profile, id, before);
        else RemoveValue(profile, id);
    }

    static void RemoveValue(XElement profile, uint id) =>
        profile.Element("Settings")?.Elements("ProfileSetting").Where(s => (uint?)s.Element("SettingID") == id).Remove();

    static void EnsureTool()
    {
        if (File.Exists(ToolExe)) return;
        Directory.CreateDirectory(ToolFolder);
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("SpudBoost");
        using var zip = new ZipArchive(new MemoryStream(http.GetByteArrayAsync(ToolUrl).GetAwaiter().GetResult()));
        zip.ExtractToDirectory(ToolFolder, overwriteFiles: true);
    }

    /// <summary>Exports every customized driver profile and returns the parsed file.</summary>
    static XElement Export()
    {
        var before = Directory.GetFiles(ToolFolder, "CustomProfiles_*.nip").ToHashSet();
        RunTool("-exportCustomized");
        var file = Directory.GetFiles(ToolFolder, "CustomProfiles_*.nip").Except(before).OrderByDescending(File.GetLastWriteTime).FirstOrDefault()
                   ?? throw new InvalidOperationException("NVIDIA Profile Inspector didn't export the driver profiles.");
        try { return XElement.Parse(File.ReadAllText(file)); }
        finally { File.Delete(file); }
    }

    List<XElement> TargetProfiles(XElement root)
    {
        var exes = GameProcesses().Select(g => g.ToLowerInvariant() + ".exe").ToHashSet();
        return root.Elements("Profile")
            .Where(p => p.Element("Executeables")?.Elements("string").Any(s => exes.Contains(s.Value.ToLowerInvariant())) == true)
            .ToList();
    }

    static uint? Value(XElement profile, uint id) =>
        profile.Element("Settings")?.Elements("ProfileSetting")
            .FirstOrDefault(s => (uint?)s.Element("SettingID") == id)?.Element("SettingValue") is { } v && uint.TryParse(v.Value, out var n)
            ? n : null;

    static void SetValue(XElement profile, uint id, uint value)
    {
        var settings = profile.Element("Settings") ?? new XElement("Settings");
        if (settings.Parent == null) profile.Add(settings);
        var setting = settings.Elements("ProfileSetting").FirstOrDefault(s => (uint?)s.Element("SettingID") == id);
        if (setting == null)
        {
            setting = new XElement("ProfileSetting",
                new XElement("SettingNameInfo"), new XElement("SettingID", id), new XElement("SettingValue", value), new XElement("ValueType", "Dword"));
            settings.Add(setting);
        }
        else setting.SetElementValue("SettingValue", value);
    }

    /// <summary>Writes profiles in the same format Profile Inspector exports (UTF-8 BOM, utf-16 declaration).</summary>
    static void Write(string path, IEnumerable<XElement> profiles)
    {
        var root = new XElement("ArrayOfProfile", profiles);
        File.WriteAllText(path, "<?xml version=\"1.0\" encoding=\"utf-16\"?>\r\n" + root, new UTF8Encoding(true));
    }

    /// <summary>Silent import replaces each listed profile with exactly what's in the file.</summary>
    static void Import(string file) => RunTool($"-silentImport \"{file}\"");

    static void RunTool(string args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo(ToolExe, args) { WorkingDirectory = ToolFolder, UseShellExecute = false, CreateNoWindow = true };
        using var p = System.Diagnostics.Process.Start(psi) ?? throw new InvalidOperationException("Couldn't start NVIDIA Profile Inspector.");
        if (!p.WaitForExit(60000))
        {
            p.Kill();
            throw new InvalidOperationException("NVIDIA Profile Inspector didn't respond.");
        }
        if (p.ExitCode != 0) throw new InvalidOperationException($"NVIDIA Profile Inspector failed (exit code {p.ExitCode}).");
    }
}
