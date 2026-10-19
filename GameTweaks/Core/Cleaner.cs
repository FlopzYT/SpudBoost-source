using System.Diagnostics;
using System.IO;

namespace GameTweaks.Core;

/// <summary>One kind of junk the Cleaner can remove, with its current size on disk.</summary>
public sealed class CleanTarget : ObservableObject
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required IReadOnlyList<string> Folders { get; init; }

    /// <summary>Game whose files these are; skipped while it's running.</summary>
    public string? GameProcess { get; init; }

    /// <summary>Only files older than this are touched (running apps keep fresh work files in Temp).</summary>
    public double MinAgeHours { get; init; }

    bool _selected;
    public bool Selected { get => _selected; set => SetField(ref _selected, value); }

    long _bytes = -1;
    public long Bytes
    {
        get => _bytes;
        set { if (SetField(ref _bytes, value)) OnPropertyChanged(nameof(SizeText)); }
    }

    public string SizeText => _bytes < 0 ? "…" : Cleaner.FormatSize(_bytes);
}

/// <summary>Finds and deletes temporary files, logs and caches. Files in use are skipped, never forced.</summary>
public static class Cleaner
{
    static string Local => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    const string Fortnite = "FortniteClient-Win64-Shipping";

    public static List<CleanTarget> Targets() =>
    [
        new()
        {
            Name = "Temporary files",
            Description = "Leftovers from installers and apps in your Temp folders.",
            Folders = [Path.GetTempPath(), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp")],
            MinAgeHours = 24,
            Selected = true,
        },
        new()
        {
            Name = "Fortnite logs and crash reports",
            Description = "Log files Fortnite writes every session. Safe to delete.",
            Folders = [Path.Combine(Local, @"FortniteGame\Saved\Logs"), Path.Combine(Local, @"FortniteGame\Saved\Crashes")],
            GameProcess = Fortnite,
            Selected = true,
        },
        new()
        {
            Name = "Fortnite replays",
            Description = "Saved match replays. Delete only if you don't need them.",
            Folders = [Path.Combine(Local, @"FortniteGame\Saved\Demos")],
            GameProcess = Fortnite,
        },
        new()
        {
            Name = "Epic Games Launcher logs and web cache",
            Description = "The launcher rebuilds these automatically.",
            Folders = [Path.Combine(Local, @"EpicGamesLauncher\Saved\Logs"), .. Directory.Exists(Path.Combine(Local, @"EpicGamesLauncher\Saved"))
                ? Directory.GetDirectories(Path.Combine(Local, @"EpicGamesLauncher\Saved"), "webcache*") : []],
            GameProcess = "EpicGamesLauncher",
            Selected = true,
        },
        new()
        {
            Name = "Crash dumps",
            Description = "Memory dumps saved when apps crash.",
            Folders = [Path.Combine(Local, "CrashDumps")],
            Selected = true,
        },
        new()
        {
            Name = "Shader caches (DirectX and NVIDIA)",
            Description = "Can fix stutter after a GPU driver update, but games rebuild them, so your first match afterwards may stutter more.",
            Folders = [Path.Combine(Local, "D3DSCache"), Path.Combine(Local, @"NVIDIA\DXCache"), Path.Combine(Local, @"NVIDIA\GLCache")],
            GameProcess = Fortnite,
        },
    ];

    public static long Measure(CleanTarget target) =>
        target.Folders.Where(Directory.Exists).Sum(folder => Files(folder, target.MinAgeHours).Sum(f => SafeLength(f)));

    /// <summary>Deletes what it can and returns the bytes freed. Files that are in use are left alone.</summary>
    public static long Clean(CleanTarget target)
    {
        if (target.GameProcess != null && Process.GetProcessesByName(target.GameProcess).Length > 0)
            throw new InvalidOperationException($"Close {(target.GameProcess == Fortnite ? "Fortnite" : "the Epic Games Launcher")} first to clean \"{target.Name}\".");

        long freed = 0;
        foreach (var folder in target.Folders.Where(Directory.Exists))
        {
            foreach (var file in Files(folder, target.MinAgeHours))
            {
                var size = SafeLength(file);
                try
                {
                    File.Delete(file);
                    freed += size;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
            // Remove now-empty subfolders, deepest first, but keep the folder itself.
            var cutoff = DateTime.UtcNow.AddHours(-target.MinAgeHours);
            foreach (var dir in SafeDirs(folder).OrderByDescending(d => d.Length))
            {
                try
                {
                    if (!Directory.EnumerateFileSystemEntries(dir).Any() &&
                        (target.MinAgeHours <= 0 || Directory.GetLastWriteTimeUtc(dir) < cutoff))
                        Directory.Delete(dir);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        return freed;
    }

    static IEnumerable<string> Files(string folder, double minAgeHours)
    {
        var cutoff = DateTime.UtcNow.AddHours(-minAgeHours);
        try
        {
            return Directory.EnumerateFiles(folder, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true })
                .Where(f => minAgeHours <= 0 || SafeLastWrite(f) < cutoff)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }

    static DateTime SafeLastWrite(string file)
    {
        try { return File.GetLastWriteTimeUtc(file); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return DateTime.MaxValue; }
    }

    static IEnumerable<string> SafeDirs(string folder)
    {
        try { return Directory.EnumerateDirectories(folder, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }).ToList(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }

    static long SafeLength(string file)
    {
        try { return new FileInfo(file).Length; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return 0; }
    }

    public static string FormatSize(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.0} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0} MB",
        >= 1L << 10 => $"{bytes / (double)(1L << 10):0} KB",
        _ => $"{bytes} B",
    };
}
