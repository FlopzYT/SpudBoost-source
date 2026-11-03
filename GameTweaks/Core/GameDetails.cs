using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using GameTweaks.Tweaks;
using Microsoft.Win32;

namespace GameTweaks.Core;

/// <summary>Store information shown on a game's page (description, tags, screenshots).</summary>
public sealed class GameInfo
{
    public string Description { get; set; } = "";
    public List<string> Genres { get; set; } = [];
    public string? ReleaseDate { get; set; }
    public string? Developer { get; set; }
    public List<string> Screenshots { get; set; } = [];
    public string? HeaderImage { get; set; }
}

/// <summary>Store info (from Steam's public store API, cached for a week) and per-game tweaks.</summary>
public static class GameDetails
{
    static string CacheFolder => Path.Combine(BackupStore.Folder, "GameInfo");
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    /// <summary>Descriptions for the non-Steam games SpudBoost knows, since Epic has no public store API.</summary>
    static readonly Dictionary<string, GameInfo> BuiltIn = new()
    {
        ["epic-fortnite"] = new()
        {
            Description = "The free-to-play battle royale from Epic Games: drop in, build or go Zero Build, and be the last one standing. SpudBoost has a full Fortnite page with potato graphics, a custom crosshair and more.",
            Genres = ["Battle royale", "Shooter", "Free to play"], Developer = "Epic Games", ReleaseDate = "Jul 25, 2017",
        },
        ["roblox"] = new()
        {
            Description = "Play millions of experiences made by the community, from obbies to shooters to tycoons. SpudBoost has a Roblox page with graphics settings and an FPS cap you can raise.",
            Genres = ["Platform", "Multiplayer", "Free to play"], Developer = "Roblox Corporation", ReleaseDate = "Sep 1, 2006",
        },
        ["ms-minecraft"] = new()
        {
            Description = "Build, explore and survive in endless blocky worlds. The launcher starts both Java and Bedrock editions.",
            Genres = ["Sandbox", "Survival", "Building"], Developer = "Mojang Studios", ReleaseDate = "Nov 18, 2011",
        },
    };

    public static async Task<GameInfo?> GetInfoAsync(LibraryGame game)
    {
        if (BuiltIn.TryGetValue(game.Key, out var known)) return known;
        if (game.SteamAppId == null) return null;

        var cache = Path.Combine(CacheFolder, game.Key + ".json");
        try
        {
            if (File.Exists(cache) && File.GetLastWriteTime(cache) > DateTime.Now.AddDays(-7))
                return JsonSerializer.Deserialize<GameInfo>(await File.ReadAllTextAsync(cache));
        }
        catch (Exception ex) when (ex is JsonException or IOException) { }

        try
        {
            var json = await Http.GetStringAsync($"https://store.steampowered.com/api/appdetails?appids={game.SteamAppId}&l=english");
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty(game.SteamAppId, out var entry) ||
                !entry.TryGetProperty("success", out var ok) || !ok.GetBoolean() || !entry.TryGetProperty("data", out var d)) return null;
            string? Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            var info = new GameInfo
            {
                Description = System.Net.WebUtility.HtmlDecode(Regex.Replace(Str(d, "short_description") ?? "", "<.*?>", "")),
                Genres = d.TryGetProperty("genres", out var g) ? g.EnumerateArray().Select(x => Str(x, "description")).OfType<string>().Take(4).ToList() : [],
                ReleaseDate = d.TryGetProperty("release_date", out var r) ? Str(r, "date") : null,
                Developer = d.TryGetProperty("developers", out var dev) && dev.GetArrayLength() > 0 ? dev[0].GetString() : null,
                Screenshots = d.TryGetProperty("screenshots", out var s) ? s.EnumerateArray().Select(x => Str(x, "path_thumbnail")).OfType<string>().Take(12).ToList() : [],
                HeaderImage = Str(d, "header_image"),
            };
            Directory.CreateDirectory(CacheFolder);
            await File.WriteAllTextAsync(cache, JsonSerializer.Serialize(info));
            return info;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException or InvalidOperationException or KeyNotFoundException)
        {
            return null;
        }
    }

    // ---- The game's main exe ----

    static string ExeCacheFile => Path.Combine(BackupStore.Folder, "game-exes.json");
    static Dictionary<string, string>? _exeCache;

    static Dictionary<string, string> ExeCache
    {
        get
        {
            if (_exeCache != null) return _exeCache;
            try { _exeCache = File.Exists(ExeCacheFile) ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(ExeCacheFile)) : null; }
            catch (Exception ex) when (ex is JsonException or IOException) { }
            return _exeCache ??= [];
        }
    }

    static readonly string[] NotTheGame = ["crash", "vconsole", "ffmpeg", "diag", "browser", "crashpad", "editor", "server", "launcher", "bootstrap", "setup", "unins", "redist", "vcredist", "directx", "dxsetup", "easyanticheat",
        "battleye", "beservice", "helper", "updater", "report", "uploader", "prereq", "dotnet", "ue4prereq", "cefprocess", "unitycrashhandler", "install"];

    static readonly string[] SkipFolders = [@"\Plugins", @"\Redist", @"\_CommonRedist", @"\ThirdParty", @"\Tools", @"\Support", @"\Installers", @"\DirectX", @"\vcredist"];

    /// <summary>Games whose biggest exe isn't the game itself.</summary>
    static readonly Dictionary<string, string> KnownExes = new()
    {
        ["steam-730"] = @"game\bin\win64\cs2.exe",
        ["steam-221100"] = "DayZ_x64.exe",
    };

    /// <summary>Prefers Unreal "-Shipping" builds and exes named like the game or its folder.</summary>
    static int Score(FileInfo f, LibraryGame game, string folder)
    {
        static string Plain(string s) => new(s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        var name = Plain(Path.GetFileNameWithoutExtension(f.Name));
        var score = 0;
        if (name.EndsWith("win64shipping")) score += 100;
        var gameName = Plain(game.Name);
        var folderName = Plain(Path.GetFileName(folder.TrimEnd('\\')));
        var stem = name.Replace("win64shipping", "").Replace("x64", "");
        if (stem.Length >= 3 && (gameName.Contains(stem) || folderName.Contains(stem) || stem.Contains(folderName))) score += 50;
        return score;
    }

    /// <summary>Known exe from earlier, if any (no disk scan).</summary>
    public static string? CachedExe(LibraryGame game) => ExeCache.GetValueOrDefault(game.Key) is { } p && File.Exists(p) ? p : null;

    /// <summary>
    /// The game's own exe: the biggest .exe in its folder (4 levels deep) that isn't a launcher, installer or anti-cheat.
    /// Fortnite's real exe is known. The result is remembered so the folder is only scanned once.
    /// </summary>
    public static string? FindMainExe(LibraryGame game)
    {
        if (CachedExe(game) is { } cached) return cached;
        string? best = null;
        if (game.Key == "epic-fortnite") best = EpicGames.FortniteExe;
        else if (game.InstallFolder is { } folder && Directory.Exists(folder))
        {
            try
            {
                var known = KnownExes.GetValueOrDefault(game.Key) is { } rel ? Path.Combine(folder, rel) : null;
                best = known != null && File.Exists(known) ? known : new DirectoryInfo(folder)
                    .EnumerateFiles("*.exe", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 4, IgnoreInaccessible = true })
                    .Where(f => !NotTheGame.Any(n => f.Name.Contains(n, StringComparison.OrdinalIgnoreCase)) &&
                                !SkipFolders.Any(d => f.DirectoryName!.Contains(d, StringComparison.OrdinalIgnoreCase)))
                    .OrderByDescending(f => Score(f, game, folder))
                    .ThenByDescending(f => f.Length)
                    .FirstOrDefault()?.FullName;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        if (best == null) return null;
        ExeCache[game.Key] = best;
        try
        {
            Directory.CreateDirectory(BackupStore.Folder);
            File.WriteAllText(ExeCacheFile, JsonSerializer.Serialize(ExeCache));
        }
        catch (IOException) { }
        return best;
    }

    // ---- Game-specific tweaks ----

    const string GpuPrefs = @"Software\Microsoft\DirectX\UserGpuPreferences";
    const string Layers = @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";
    const string Ifeo = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options";

    /// <summary>Tweaks for one game's exe, all reversible like the rest of SpudBoost.</summary>
    public static List<Tweak> TweaksFor(LibraryGame game, string exe, AppSettings settings)
    {
        var process = Path.GetFileNameWithoutExtension(exe);
        var id = game.Key;
        return
        [
            new RegistryTweak
            {
                Id = $"{id}-gpu",
                Name = "Use the high-performance GPU",
                Description = $"Always runs {game.Name} on your dedicated graphics card instead of built-in graphics. A big win on laptops; harmless on desktops.",
                Values = [new RegValue(RegistryHive.CurrentUser, GpuPrefs, exe, RegistryValueKind.String, "GpuPreference=2;")],
                Category = "Game",
            },
            new RegistryTweak
            {
                Id = $"{id}-fso",
                Name = "Disable fullscreen optimizations",
                Description = "Runs the game in true exclusive fullscreen (when it's set to Fullscreen) for lower input delay. Overlays like the SpudBoost crosshair can't show over exclusive fullscreen.",
                Values = [new RegValue(RegistryHive.CurrentUser, Layers, exe, RegistryValueKind.String, "~ DISABLEDXMAXIMIZEDWINDOWEDMODE")],
                Category = "Game",
            },
            new RegistryTweak
            {
                Id = $"{id}-priority",
                Name = "High CPU priority every launch",
                Description = $"Windows starts {process}.exe with High priority so it gets the CPU before background apps, even when SpudBoost isn't running.",
                Values = [new RegValue(RegistryHive.LocalMachine, $@"{Ifeo}\{Path.GetFileName(exe)}\PerfOptions", "CpuPriorityClass", RegistryValueKind.DWord, 3)],
                RequiresAdmin = true,
                Category = "Game",
            },
            new GameBoostListTweak(settings, process)
            {
                Id = $"{id}-boost",
                Name = "Boost with Game Boost",
                Description = $"Adds {process} to Game Boost: background apps are lowered while it runs, your play time is recorded and NVIDIA settings apply to it.",
                Category = "Game",
            },
        ];
    }
}

/// <summary>Adds or removes a process name from the Game Boost list.</summary>
public sealed class GameBoostListTweak(AppSettings settings, string process) : Tweak
{
    public override bool HasBackup => IsApplied();
    public override bool IsApplied() => settings.GameProcesses.Contains(process, StringComparer.OrdinalIgnoreCase);

    protected override void ApplyCore()
    {
        if (IsApplied()) return;
        settings.GameProcesses.Add(process);
        settings.Save();
    }

    protected override void RevertCore()
    {
        settings.GameProcesses.RemoveAll(p => p.Equals(process, StringComparison.OrdinalIgnoreCase));
        settings.Save();
    }
}
