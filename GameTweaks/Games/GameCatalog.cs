using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using GameTweaks.Core;

namespace GameTweaks.Games;

/// <summary>The games with their own settings page. Setting names come from the games' real settings files.</summary>
public static class GameCatalog
{
    static string Local => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    static string Roaming => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    static readonly (string, string)[] OffOn = [("Off", "0"), ("On", "1")];

    public static List<GameProfile> All() => [Roblox(), Minecraft(), Cs2()];

    // ---- Roblox ----

    static GameProfile Roblox() => new()
    {
        Id = "roblox",
        Name = "Roblox",
        Emoji = "🧱",
        Description = "Edits Roblox's own settings file (GlobalBasicSettings). Graphics quality 1 is the potato look; an unlimited FPS cap uses Roblox's built-in option.",
        Processes = ["RobloxPlayerBeta"],
        FindConfigs = () => File.Exists(Path.Combine(Local, @"Roblox\GlobalBasicSettings_13.xml")) ? [Path.Combine(Local, @"Roblox\GlobalBasicSettings_13.xml")] : [],
        Format = new RobloxXmlFormat(),
        Settings =
        [
            new() { Key = "SavedQualityLevel", Label = "Graphics quality", Hint = "1 = potato, 10 = max. 0 = automatic.", Min = 0, Max = 10 },
            new()
            {
                Key = "FramerateCap", Label = "Max FPS", Kind = SettingKind.Choice,
                Choices = [("60", "60"), ("120", "120"), ("144", "144"), ("165", "165"), ("240", "240"), ("Unlimited", "-1")],
            },
            new() { Key = "Fullscreen", Label = "Fullscreen", Kind = SettingKind.Toggle },
        ],
        Presets = new()
        {
            ["🥔 Potato"] = new() { ["SavedQualityLevel"] = "1", ["FramerateCap"] = "-1" },
            ["🎯 Competitive"] = new() { ["SavedQualityLevel"] = "3", ["FramerateCap"] = "-1" },
        },
        // Roblox keeps the menu slider (1-10) and the actual level (1-21) separately; move both together.
        BeforeSave = v =>
        {
            if (v.TryGetValue("SavedQualityLevel", out var s) && int.TryParse(s, out var level) && level > 0)
                v["GraphicsQualityLevel"] = Math.Max(1, (int)Math.Round(level * 2.1)).ToString();
        },
        FindExes = () => FilesIn([Path.Combine(Local, @"Roblox\Versions")], "RobloxPlayerBeta.exe", 2),
    };

    // ---- Minecraft (Java) ----

    static readonly string MinecraftOptions = Path.Combine(Roaming, @".minecraft\options.txt");

    static GameProfile Minecraft() => new()
    {
        Id = "minecraft",
        Name = "Minecraft",
        Emoji = "⛏",
        Description = "Java Edition video settings (options.txt). Only settings your version has are shown, since names change between versions.",
        Processes = ["javaw"],
        FindConfigs = () => File.Exists(MinecraftOptions) ? [MinecraftOptions] : [],
        Format = new ColonFormat(),
        Settings =
        [
            new() { Key = "renderDistance", Label = "Render distance (chunks)", Hint = "The biggest FPS setting.", Min = 2, Max = 32 },
            new() { Key = "simulationDistance", Label = "Simulation distance", Min = 5, Max = 32 },
            new() { Key = "maxFps", Label = "Max FPS", Hint = "260 = unlimited.", Min = 10, Max = 260 },
            new() { Key = "enableVsync", Label = "VSync", Kind = SettingKind.Toggle },
            new() { Key = "graphicsMode", Label = "Graphics", Kind = SettingKind.Choice, Choices = [("Fast", "0"), ("Fancy", "1"), ("Fabulous", "2")] },
            new() { Key = "fancyGraphics", Label = "Fancy graphics", Kind = SettingKind.Toggle },
            new() { Key = "renderClouds", Label = "Clouds", Kind = SettingKind.Choice, Choices = [("Off", "false"), ("Fast", "fast"), ("Fancy", "true")] },
            new() { Key = "particles", Label = "Particles", Kind = SettingKind.Choice, Choices = [("All", "0"), ("Decreased", "1"), ("Minimal", "2")] },
            new() { Key = "ao", Label = "Smooth lighting", Kind = SettingKind.Toggle },
            new() { Key = "entityShadows", Label = "Entity shadows", Kind = SettingKind.Toggle },
            new() { Key = "mipmapLevels", Label = "Mipmap levels", Min = 0, Max = 4 },
            new() { Key = "biomeBlendRadius", Label = "Biome blend", Min = 0, Max = 7 },
        ],
        Presets = new()
        {
            ["🥔 Potato"] = new()
            {
                ["renderDistance"] = "6", ["simulationDistance"] = "5", ["maxFps"] = "260", ["enableVsync"] = "off",
                ["graphicsMode"] = "0", ["fancyGraphics"] = "off", ["renderClouds"] = "false", ["particles"] = "2",
                ["ao"] = "off", ["entityShadows"] = "off", ["mipmapLevels"] = "0", ["biomeBlendRadius"] = "0",
            },
            ["🎯 Competitive"] = new()
            {
                ["renderDistance"] = "8", ["simulationDistance"] = "6", ["maxFps"] = "260", ["enableVsync"] = "off",
                ["graphicsMode"] = "0", ["fancyGraphics"] = "off", ["renderClouds"] = "false", ["particles"] = "1",
                ["entityShadows"] = "off", ["mipmapLevels"] = "0", ["biomeBlendRadius"] = "0",
            },
        },
        FindExes = () => FilesIn(
        [
            Path.Combine(Roaming, @".minecraft\runtime"),
            Path.Combine(Local, @"Packages\Microsoft.4297127D64EC6_8wekyb3d8bbwe\LocalCache\Local\runtime"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Minecraft Launcher\runtime"),
        ], "javaw.exe", 6),
    };

    // ---- Counter-Strike 2 ----

    static GameProfile Cs2() => new()
    {
        Id = "cs2",
        Name = "CS2",
        Emoji = "🔫",
        Description = "Counter-Strike 2 video settings (cs2_video.txt, one per Steam account). The Competitive preset keeps shadows on so you can see players' shadows.",
        Processes = ["cs2"],
        FindConfigs = () => SteamRoot() is { } steam && Directory.Exists(Path.Combine(steam, "userdata"))
            ? Directory.GetDirectories(Path.Combine(steam, "userdata"))
                .Select(d => Path.Combine(d, @"730\local\cfg\cs2_video.txt")).Where(File.Exists).ToList()
            : [],
        Format = new ValveKvFormat(),
        Settings =
        [
            new() { Key = "setting.videocfg_shadow_quality", Label = "Shadow quality", Kind = SettingKind.Choice, Choices = [("Low", "0"), ("Medium", "1"), ("High", "2"), ("Very high", "3")] },
            new() { Key = "setting.videocfg_dynamic_shadows", Label = "Dynamic shadows", Kind = SettingKind.Choice, Choices = [("Sun only", "0"), ("All", "1")] },
            new() { Key = "setting.videocfg_texture_detail", Label = "Texture detail", Kind = SettingKind.Choice, Choices = [("Low", "0"), ("Medium", "1"), ("High", "2")] },
            new()
            {
                Key = "setting.r_texturefilteringquality", Label = "Texture filtering", Kind = SettingKind.Choice,
                Choices = [("Bilinear", "0"), ("Trilinear", "1"), ("Anisotropic 2x", "2"), ("Anisotropic 4x", "3"), ("Anisotropic 8x", "4"), ("Anisotropic 16x", "5")],
            },
            new() { Key = "setting.shaderquality", Label = "Shader detail", Kind = SettingKind.Choice, Choices = [("Low", "0"), ("High", "1")] },
            new() { Key = "setting.videocfg_particle_detail", Label = "Particle detail", Kind = SettingKind.Choice, Choices = [("Low", "0"), ("Medium", "1"), ("High", "2"), ("Very high", "3")] },
            new() { Key = "setting.videocfg_ao_detail", Label = "Ambient occlusion", Kind = SettingKind.Choice, Choices = [("Off", "0"), ("Medium", "2"), ("High", "3")] },
            new() { Key = "setting.msaa_samples", Label = "MSAA", Kind = SettingKind.Choice, Choices = [("None", "0"), ("2x", "2"), ("4x", "4"), ("8x", "8")] },
            new() { Key = "setting.r_csgo_cmaa_enable", Label = "CMAA2 anti-aliasing", Kind = SettingKind.Choice, Choices = OffOn },
            new() { Key = "setting.videocfg_hdr_detail", Label = "HDR", Kind = SettingKind.Choice, Choices = [("Performance", "3"), ("Quality", "-1")] },
            new() { Key = "setting.videocfg_fsr_detail", Label = "FidelityFX upscaling", Kind = SettingKind.Choice, Choices = [("Off", "0"), ("Ultra quality", "1"), ("Quality", "2"), ("Balanced", "3"), ("Performance", "4")] },
            new() { Key = "setting.r_low_latency", Label = "NVIDIA Reflex", Kind = SettingKind.Choice, Choices = [("Off", "0"), ("On", "1"), ("On + Boost", "2")] },
            new() { Key = "setting.mat_vsync", Label = "VSync", Kind = SettingKind.Choice, Choices = OffOn },
        ],
        Presets = new()
        {
            ["🥔 Potato"] = new()
            {
                ["setting.videocfg_shadow_quality"] = "0", ["setting.videocfg_dynamic_shadows"] = "0", ["setting.videocfg_texture_detail"] = "0",
                ["setting.r_texturefilteringquality"] = "0", ["setting.shaderquality"] = "0", ["setting.videocfg_particle_detail"] = "0",
                ["setting.videocfg_ao_detail"] = "0", ["setting.msaa_samples"] = "0", ["setting.r_csgo_cmaa_enable"] = "0",
                ["setting.videocfg_hdr_detail"] = "3", ["setting.videocfg_fsr_detail"] = "0", ["setting.r_low_latency"] = "1", ["setting.mat_vsync"] = "0",
            },
            ["🎯 Competitive"] = new()
            {
                ["setting.videocfg_shadow_quality"] = "2", ["setting.videocfg_dynamic_shadows"] = "1", ["setting.videocfg_texture_detail"] = "0",
                ["setting.r_texturefilteringquality"] = "3", ["setting.shaderquality"] = "0", ["setting.videocfg_particle_detail"] = "0",
                ["setting.videocfg_ao_detail"] = "0", ["setting.msaa_samples"] = "4", ["setting.r_csgo_cmaa_enable"] = "0",
                ["setting.videocfg_hdr_detail"] = "3", ["setting.videocfg_fsr_detail"] = "0", ["setting.r_low_latency"] = "1", ["setting.mat_vsync"] = "0",
            },
        },
        FindExes = () => SteamLibraries()
            .Select(l => Path.Combine(l, @"steamapps\common\Counter-Strike Global Offensive\game\bin\win64\cs2.exe"))
            .Where(File.Exists).ToList(),
    };

    // ---- Helpers ----

    static string? SteamRoot() =>
        RegistryHelper.Read(RegistryHive.CurrentUser, @"Software\Valve\Steam", "SteamPath") is string p && Directory.Exists(p)
            ? Path.GetFullPath(p) : null;

    /// <summary>Every Steam library folder (from libraryfolders.vdf), including the main install.</summary>
    static List<string> SteamLibraries()
    {
        if (SteamRoot() is not { } steam) return [];
        var libraries = new List<string> { steam };
        var vdf = Path.Combine(steam, @"steamapps\libraryfolders.vdf");
        if (File.Exists(vdf))
            libraries.AddRange(Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\"")
                .Select(m => m.Groups[1].Value.Replace(@"\\", @"\")));
        return libraries.Distinct(StringComparer.OrdinalIgnoreCase).Where(Directory.Exists).ToList();
    }

    static List<string> FilesIn(IEnumerable<string> roots, string fileName, int maxDepth)
    {
        var found = new List<string>();
        foreach (var root in roots.Where(Directory.Exists))
        {
            try
            {
                found.AddRange(Directory.EnumerateFiles(root, fileName,
                    new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = maxDepth, IgnoreInaccessible = true }));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return found;
    }
}
