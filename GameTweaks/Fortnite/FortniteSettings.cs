using System.Diagnostics;
using System.Globalization;
using System.IO;
using GameTweaks.Core;

namespace GameTweaks.Fortnite;

/// <summary>One Low/Medium/High/Epic slider from [ScalabilityGroups].</summary>
public sealed class QualitySetting(string label, string key) : ObservableObject
{
    public static readonly string[] Levels = ["Low", "Medium", "High", "Epic"];

    public string Label { get; } = label;
    public string Key { get; } = key;

    int _value;
    public int Value
    {
        get => _value;
        set { if (SetField(ref _value, Math.Clamp(value, 0, 3))) OnPropertyChanged(nameof(LevelName)); }
    }

    public string LevelName => Levels[_value];
}

/// <summary>One on/off option from the main FortGameUserSettings section.</summary>
public sealed class BoolSetting(string label, string key, string hint) : ObservableObject
{
    public string Label { get; } = label;
    public string Key { get; } = key;
    public string Hint { get; } = hint;

    bool _value;
    public bool Value { get => _value; set => SetField(ref _value, value); }
}

/// <summary>Editable view of Fortnite's GameUserSettings.ini, with presets and backups.</summary>
public sealed class FortniteSettings : ObservableObject
{
    const string Main = "/Script/FortniteGame.FortGameUserSettings";
    const string Scalability = "ScalabilityGroups";
    const string Rhi = "D3DRHIPreference";
    const string PerformanceMode = "PerformanceMode";
    const string GameProcess = "FortniteClient-Win64-Shipping";

    public static string ConfigPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        @"FortniteGame\Saved\Config\WindowsClient\GameUserSettings.ini");

    public static string BackupFolder { get; } = Path.Combine(BackupStore.Folder, "Fortnite");
    static string OriginalBackup => Path.Combine(BackupFolder, "GameUserSettings.original.ini");

    public static readonly string[] WindowModes = ["Fullscreen", "Windowed fullscreen", "Windowed"];
    public static readonly string[] RenderModes = ["Performance mode", "DirectX 11", "DirectX 12", "Legacy Performance (DX11, deprecated)"];

    /// <summary>Graphics API per render mode. Legacy Performance is the old mobile renderer (ES3.1) on DirectX 11.</summary>
    static readonly string[] RhiNames = ["dx12", "dx11", "dx12", "dx11"];

    /// <summary>Epic launcher "Additional Command Line Arguments" that force Legacy Performance mode.</summary>
    public const string LegacyLaunchArgs = "-d3d11 -FeatureLevelES31";
    const int LegacyPerformance = 3;
    public static readonly string[] MeshLevels = ["Low", "Medium", "High"];
    static readonly string[] FeatureLevels = ["es31", "sm5", "sm6", "es31"];

    public List<QualitySetting> Qualities { get; } =
    [
        new("View distance", "sg.ViewDistanceQuality"),
        new("Shadows", "sg.ShadowQuality"),
        new("Anti-aliasing", "sg.AntiAliasingQuality"),
        new("Textures", "sg.TextureQuality"),
        new("Effects", "sg.EffectsQuality"),
        new("Post processing", "sg.PostProcessQuality"),
        new("Global illumination", "sg.GlobalIlluminationQuality"),
        new("Reflections", "sg.ReflectionQuality"),
        new("Foliage", "sg.FoliageQuality"),
        new("Shading", "sg.ShadingQuality"),
        new("Landscape", "sg.LandscapeQuality"),
    ];

    public List<BoolSetting> Toggles { get; } =
    [
        new("VSync", "bUseVSync", "Caps FPS to your refresh rate; adds input delay."),
        new("Motion blur", "bMotionBlur", "Blurs the screen when moving fast."),
        new("Show grass", "bShowGrass", "Off removes grass entirely, which helps both FPS and visibility."),
        new("Nanite", "bUseNanite", "High-detail geometry (DirectX 12 only). Very heavy."),
        new("Ray tracing", "bRayTracing", "Hardware ray tracing (DirectX 12 only). Very heavy."),
        new("Dynamic resolution", "bUseDynamicResolution", "Lowers resolution automatically under load."),
        new("Show FPS counter", "bShowFPS", "Shows the in-game FPS counter."),
        new("Disable mouse acceleration", "bDisableMouseAcceleration", "Raw, consistent aim."),
    ];

    int _resX = 1920, _resY = 1080, _windowMode, _frameRateLimit, _renderMode, _meshQuality;
    double _resolutionPercent = 100;
    bool _lockReadOnly;

    public int ResX { get => _resX; set => SetField(ref _resX, value); }
    public int ResY { get => _resY; set => SetField(ref _resY, value); }
    public int WindowMode { get => _windowMode; set => SetField(ref _windowMode, value); }
    public int RenderMode
    {
        get => _renderMode;
        set
        {
            if (!SetField(ref _renderMode, value)) return;
            OnPropertyChanged(nameof(IsPerformanceMode));
            OnPropertyChanged(nameof(IsLegacyPerformance));
        }
    }

    /// <summary>Both performance renderers use the Mesh quality setting.</summary>
    public bool IsPerformanceMode => _renderMode is 0 or LegacyPerformance;

    public bool IsLegacyPerformance => _renderMode == LegacyPerformance;

    /// <summary>Performance mode only: how detailed buildings and objects are. Low gives the flat "potato" look.</summary>
    public int MeshQuality { get => _meshQuality; set => SetField(ref _meshQuality, Math.Clamp(value, 0, 2)); }

    /// <summary>3D resolution as a percentage of the screen resolution.</summary>
    public double ResolutionPercent { get => _resolutionPercent; set => SetField(ref _resolutionPercent, Math.Round(value)); }

    /// <summary>0 means unlimited.</summary>
    public int FrameRateLimit { get => _frameRateLimit; set => SetField(ref _frameRateLimit, value); }

    /// <summary>Marks the file read-only so Fortnite cannot reset the settings.</summary>
    public bool LockReadOnly { get => _lockReadOnly; set => SetField(ref _lockReadOnly, value); }

    public static bool ConfigExists => File.Exists(ConfigPath);
    public static bool GameRunning => Process.GetProcessesByName(GameProcess).Length > 0;
    public static bool HasOriginalBackup => File.Exists(OriginalBackup);

    public void Load()
    {
        var ini = IniFile.Load(ConfigPath);
        ResX = Int(ini.Get(Main, "ResolutionSizeX"), 1920);
        ResY = Int(ini.Get(Main, "ResolutionSizeY"), 1080);
        WindowMode = Math.Clamp(Int(ini.Get(Main, "FullscreenMode"), 0), 0, 2);
        FrameRateLimit = (int)Num(ini.Get(Main, "FrameRateLimit"), 0);
        ResolutionPercent = Num(ini.Get(Scalability, "sg.ResolutionQuality"), 100);
        var level = ini.Get(Rhi, "PreferredFeatureLevel")?.ToLowerInvariant();
        var rhi = ini.Get(Rhi, "PreferredRHI")?.ToLowerInvariant();
        RenderMode = level switch
        {
            "es31" => rhi == "dx11" ? LegacyPerformance : 0,
            "sm6" => 2,
            _ => 1,
        };
        MeshQuality = Int(ini.Get(PerformanceMode, "MeshQuality"), 0);
        foreach (var q in Qualities) q.Value = Int(ini.Get(Scalability, q.Key), 0);
        foreach (var t in Toggles) t.Value = string.Equals(ini.Get(Main, t.Key), "True", StringComparison.OrdinalIgnoreCase);
        LockReadOnly = new FileInfo(ConfigPath).IsReadOnly;
    }

    /// <summary>Lowest everything: maximum FPS on weak hardware.</summary>
    public void ApplyPotatoPreset()
    {
        foreach (var q in Qualities) q.Value = 0;
        SetToggle("bUseVSync", false);
        SetToggle("bMotionBlur", false);
        SetToggle("bShowGrass", false);
        SetToggle("bUseNanite", false);
        SetToggle("bRayTracing", false);
        SetToggle("bUseDynamicResolution", false);
        ResolutionPercent = 50;
        RenderMode = 0;
        MeshQuality = 0;
        WindowMode = 0;
    }

    /// <summary>
    /// The flattest look the game's own settings allow: potato settings, minimum 3D resolution and
    /// stretched 4:3 so everything renders as few pixels as possible.
    /// </summary>
    public void ApplyUltraPotatoPreset()
    {
        ApplyPotatoPreset();
        ResolutionPercent = 25;
        ResX = 1440;
        ResY = 1080;
        // The old mobile renderer gives the flattest look; Fortnite falls back to Performance mode if it's gone.
        RenderMode = LegacyPerformance;
        SetToggle("bShowFPS", true);
    }

    /// <summary>Potato settings but full sharpness and far view distance for spotting players.</summary>
    public void ApplyCompetitivePreset()
    {
        ApplyPotatoPreset();
        ResolutionPercent = 100;
        Qualities.First(q => q.Key == "sg.ViewDistanceQuality").Value = 2;
    }

    void SetToggle(string key, bool value) => Toggles.First(t => t.Key == key).Value = value;

    /// <summary>Writes the settings. Returns the timestamped backup path.</summary>
    public string Save()
    {
        if (GameRunning)
            throw new InvalidOperationException("Close Fortnite first. It rewrites this file when it exits and would undo your changes.");

        var backup = Backup();
        var info = new FileInfo(ConfigPath);
        if (info.IsReadOnly) info.IsReadOnly = false;

        var ini = IniFile.Load(ConfigPath);
        var inv = CultureInfo.InvariantCulture;
        string F(double v) => v.ToString("F6", inv);

        foreach (var key in new[] { "ResolutionSizeX", "LastUserConfirmedResolutionSizeX", "DesiredScreenWidth", "LastUserConfirmedDesiredScreenWidth" })
            ini.Set(Main, key, ResX.ToString(inv));
        foreach (var key in new[] { "ResolutionSizeY", "LastUserConfirmedResolutionSizeY", "DesiredScreenHeight", "LastUserConfirmedDesiredScreenHeight" })
            ini.Set(Main, key, ResY.ToString(inv));
        foreach (var key in new[] { "FullscreenMode", "PreferredFullscreenMode", "LastConfirmedFullscreenMode" })
            ini.Set(Main, key, WindowMode.ToString(inv));

        ini.Set(Main, "FrameRateLimit", F(FrameRateLimit));
        ini.Set(Main, "NeverUpscaledResolutionQuality", F(ResolutionPercent));
        ini.Set(Main, "PotentiallyUpscaledResolutionQuality", F(ResolutionPercent));
        ini.Set(Scalability, "sg.ResolutionQuality", ResolutionPercent.ToString(inv));
        ini.Set(Rhi, "PreferredRHI", RhiNames[RenderMode]);
        ini.Set(Rhi, "PreferredFeatureLevel", FeatureLevels[RenderMode]);
        ini.Set(PerformanceMode, "MeshQuality", MeshQuality.ToString(inv));
        foreach (var q in Qualities) ini.Set(Scalability, q.Key, q.Value.ToString(inv));
        foreach (var t in Toggles) ini.Set(Main, t.Key, t.Value ? "True" : "False");
        ini.Save();

        if (LockReadOnly) info.IsReadOnly = true;
        return backup;
    }

    /// <summary>Copies the current file aside; the very first copy is kept forever as the original.</summary>
    static string Backup()
    {
        Directory.CreateDirectory(BackupFolder);
        if (!File.Exists(OriginalBackup)) File.Copy(ConfigPath, OriginalBackup);
        var path = Path.Combine(BackupFolder, $"GameUserSettings.{DateTime.Now:yyyyMMdd-HHmmss}.ini");
        File.Copy(ConfigPath, path, true);
        return path;
    }

    public static void RestoreOriginal()
    {
        if (GameRunning) throw new InvalidOperationException("Close Fortnite first.");
        if (!HasOriginalBackup) throw new InvalidOperationException("No Fortnite backup exists yet. Nothing has been changed.");
        var info = new FileInfo(ConfigPath);
        if (info.Exists && info.IsReadOnly) info.IsReadOnly = false;
        File.Copy(OriginalBackup, ConfigPath, true);
    }

    static int Int(string? s, int fallback) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? (int)Math.Round(v) : fallback;

    static double Num(string? s, double fallback) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;
}
