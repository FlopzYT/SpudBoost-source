using System.IO;
using System.Text.Json;
using GameTweaks.Core;

namespace GameTweaks.Fortnite;

/// <summary>
/// Puts your potato settings back if Fortnite resets them (it often does after big updates).
/// Only acts while Fortnite is closed, and the normal backup is taken before every change.
/// </summary>
public sealed class FortniteGuard
{
    readonly AppSettings _settings;
    DateTime _lastSeenWrite;

    public FortniteGuard(AppSettings settings) => _settings = settings;

    /// <summary>Checks once; returns true if the settings had been reset and were put back.</summary>
    public bool CheckAndRestore()
    {
        if (_settings.KeptFortniteSettings is not { } kept || !FortniteSettings.ConfigExists || FortniteSettings.GameRunning) return false;

        // Cheap check first: only look inside the file when Fortnite has written to it.
        var written = File.GetLastWriteTimeUtc(FortniteSettings.ConfigPath);
        if (written == _lastSeenWrite) return false;
        _lastSeenWrite = written;

        var current = new FortniteSettings();
        current.Load();
        if (Same(current.Snapshot(), kept)) return false;

        current.ApplySnapshot(kept);
        current.Save();
        _lastSeenWrite = File.GetLastWriteTimeUtc(FortniteSettings.ConfigPath);
        return true;
    }

    static bool Same(FortniteSnapshot a, FortniteSnapshot b) =>
        JsonSerializer.Serialize(a) == JsonSerializer.Serialize(b);
}
