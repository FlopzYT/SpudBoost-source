using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace GameTweaks.Core;

/// <summary>An installed game SpudBoost can launch.</summary>
public sealed class LibraryGame
{
    public required string Name { get; init; }
    /// <summary>"Steam", "Epic Games" or "Microsoft Store".</summary>
    public required string Source { get; init; }
    /// <summary>What to open to start the game (a launcher URL or shell: path).</summary>
    public required string LaunchTarget { get; init; }
    /// <summary>Install folder, used to recognise the game's process once it's running. Null when unknown.</summary>
    public string? InstallFolder { get; init; }
    public string? CoverPath { get; init; }
    public string? IconExe { get; init; }

    ImageSource? _image;
    bool _imageLoaded;
    /// <summary>Steam's cover art when available, otherwise the game's own icon.</summary>
    public ImageSource? Image
    {
        get
        {
            if (_imageLoaded) return _image;
            _imageLoaded = true;
            _image = GameLibrary.LoadImage(CoverPath) ?? GameLibrary.LoadIcon(IconExe);
            return _image;
        }
    }

    public bool HasImage => Image != null;
    public string Initial => Name.Length > 0 ? Name[..1].ToUpperInvariant() : "?";
}

/// <summary>Finds installed Steam, Epic Games and Minecraft titles from the launchers' own records (read-only).</summary>
public static class GameLibrary
{
    const string MinecraftFamily = "Microsoft.4297127D64EC6_8wekyb3d8bbwe";

    /// <summary>Steam tools that show up as "installed apps" but aren't games.</summary>
    static readonly HashSet<string> SteamNonGames = ["228980", "1070560", "1391110", "1628350", "1493710", "2180100", "250820", "1826330"];

    public static List<LibraryGame> Scan()
    {
        var games = new List<LibraryGame>();
        try { games.AddRange(Epic()); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        try { games.AddRange(Steam()); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        if (Minecraft() is { } mc) games.Add(mc);
        return games
            .GroupBy(g => g.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
            .OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    static IEnumerable<LibraryGame> Epic()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Epic\EpicGamesLauncher\Data\Manifests");
        if (!Directory.Exists(folder)) yield break;
        foreach (var file in Directory.GetFiles(folder, "*.item"))
        {
            LibraryGame? game = null;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                var r = doc.RootElement;
                string Str(string name) => r.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
                var categories = r.TryGetProperty("AppCategories", out var c) && c.ValueKind == JsonValueKind.Array
                    ? c.EnumerateArray().Select(x => x.GetString() ?? "").ToList() : [];
                var app = Str("AppName");
                var main = Str("MainGameAppName");
                var exe = Str("LaunchExecutable");
                var dir = Str("InstallLocation");
                // Skip DLC, add-ons, Unreal Engine and plugins: only real games with something to launch.
                if (exe.Length == 0 || dir.Length == 0 || (main.Length > 0 && main != app) ||
                    categories.Any(x => x is "addons" or "plugins" or "engines") || !Directory.Exists(dir)) continue;
                var exePath = Path.Combine(dir, exe);
                game = new LibraryGame
                {
                    Name = Str("DisplayName") is { Length: > 0 } n ? n : app,
                    Source = "Epic Games",
                    LaunchTarget = $"com.epicgames.launcher://apps/{Uri.EscapeDataString(Str("CatalogNamespace"))}%3A{Uri.EscapeDataString(Str("CatalogItemId"))}%3A{Uri.EscapeDataString(app)}?action=launch&silent=true",
                    InstallFolder = dir,
                    IconExe = File.Exists(exePath) ? exePath : null,
                };
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { }
            if (game != null) yield return game;
        }
    }

    static IEnumerable<LibraryGame> Steam()
    {
        if (Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) is not string steam || !Directory.Exists(steam)) yield break;
        steam = Path.GetFullPath(steam);
        var libraries = new List<string> { steam };
        var vdf = Path.Combine(steam, @"steamapps\libraryfolders.vdf");
        if (File.Exists(vdf))
            foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                libraries.Add(m.Groups[1].Value.Replace(@"\\", @"\"));

        var seen = new HashSet<string>();
        foreach (var lib in libraries.Select(l => Path.GetFullPath(l).TrimEnd('\\')).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var apps = Path.Combine(lib, "steamapps");
            if (!Directory.Exists(apps)) continue;
            foreach (var acf in Directory.GetFiles(apps, "appmanifest_*.acf"))
            {
                string text;
                try { text = File.ReadAllText(acf); }
                catch (IOException) { continue; }
                string Field(string key) => Regex.Match(text, $"\"{key}\"\\s+\"([^\"]*)\"", RegexOptions.IgnoreCase) is { Success: true } m ? m.Groups[1].Value : "";
                var id = Field("appid");
                var name = Field("name");
                if (id.Length == 0 || name.Length == 0 || !seen.Add(id) || SteamNonGames.Contains(id) ||
                    name.Contains("Redistributable", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("Steamworks", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("Proton", StringComparison.OrdinalIgnoreCase)) continue;
                var dir = Path.Combine(apps, "common", Field("installdir"));
                yield return new LibraryGame
                {
                    Name = name,
                    Source = "Steam",
                    LaunchTarget = $"steam://rungameid/{id}",
                    InstallFolder = Directory.Exists(dir) ? dir : null,
                    CoverPath = SteamCover(steam, id),
                };
            }
        }
    }

    /// <summary>Steam keeps tall cover art for every game in its library cache (older layout: flat files; newer: a folder per game).</summary>
    static string? SteamCover(string steam, string id)
    {
        var cache = Path.Combine(steam, @"appcache\librarycache");
        var flat = Path.Combine(cache, $"{id}_library_600x900.jpg");
        if (File.Exists(flat)) return flat;
        var dir = Path.Combine(cache, id);
        if (!Directory.Exists(dir)) return null;
        try
        {
            return new[] { "library_600x900.jpg", "library_capsule.jpg", "header.jpg", "library_header.jpg" }
                .Select(name => Directory.GetFiles(dir, name, SearchOption.AllDirectories).FirstOrDefault())
                .FirstOrDefault(f => f != null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    static LibraryGame? Minecraft()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages");
        if (key?.GetSubKeyNames().Any(n => n.StartsWith("Microsoft.4297127D64EC6_", StringComparison.OrdinalIgnoreCase)) != true) return null;
        return new LibraryGame { Name = "Minecraft Launcher", Source = "Microsoft Store", LaunchTarget = $@"shell:AppsFolder\{MinecraftFamily}!Minecraft" };
    }

    public static void Launch(LibraryGame game) =>
        Process.Start(new ProcessStartInfo(game.LaunchTarget) { UseShellExecute = true });

    internal static ImageSource? LoadImage(string? path)
    {
        if (path == null || !File.Exists(path)) return null;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(path);
            image.DecodePixelWidth = 180;
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UriFormatException or InvalidOperationException) { return null; }
    }

    internal static ImageSource? LoadIcon(string? exe)
    {
        if (exe == null || !File.Exists(exe)) return null;
        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(exe);
            if (icon == null) return null;
            var source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(icon.Handle, System.Windows.Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or System.ComponentModel.Win32Exception or COMException) { return null; }
    }

    // ---- Recognising a launched game's process (so Game Boost learns it) ----

    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);
    const uint QueryLimitedInformation = 0x1000;

    /// <summary>Full exe path of a process. Works for most anti-cheat protected games too (limited query rights only).</summary>
    public static string? ProcessPath(int pid)
    {
        var handle = OpenProcess(QueryLimitedInformation, false, pid);
        if (handle == IntPtr.Zero) return null;
        try
        {
            var size = 1024;
            var sb = new StringBuilder(size);
            return QueryFullProcessImageName(handle, 0, sb, ref size) ? sb.ToString() : null;
        }
        finally { CloseHandle(handle); }
    }

    static readonly string[] HelperNames = ["crash", "launcher", "bootstrap", "setup", "unins", "redist", "easyanticheat", "battleye", "beservice", "helper", "updater", "webhelper"];

    /// <summary>
    /// The biggest-memory process running from the game's folder that isn't a launcher, crash reporter or anti-cheat
    /// (that's the actual game). Null when nothing is running from there yet.
    /// </summary>
    public static string? FindGameProcess(string installFolder)
    {
        var root = Path.GetFullPath(installFolder).TrimEnd('\\') + "\\";
        string? best = null;
        long bestMemory = 0;
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                try
                {
                    if (HelperNames.Any(h => p.ProcessName.Contains(h, StringComparison.OrdinalIgnoreCase))) continue;
                    var path = ProcessPath(p.Id);
                    if (path == null || !path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;
                    var memory = p.WorkingSet64;
                    if (memory > bestMemory)
                    {
                        bestMemory = memory;
                        best = p.ProcessName;
                    }
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            }
        }
        return best;
    }
}
