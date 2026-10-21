using System.Windows;
using System.Windows.Media;
using GameTweaks.Core;
using GameTweaks.Tweaks;

namespace GameTweaks;

/// <summary>One achievement badge on the Home page.</summary>
public sealed record Achievement(string Id, string Icon, string Title, string Description, bool Unlocked)
{
    public double Opacity => Unlocked ? 1 : 0.35;
    public string Tip => Unlocked ? $"{Title}: {Description}" : $"🔒 {Title}: {Description}";
}

/// <summary>Achievements: worked out from what you've done, remembered once unlocked.</summary>
public partial class MainWindow
{
    List<Achievement> ComputeAchievements()
    {
        var sessions = _sessions?.Sessions ?? [];
        var privacy = _systemTweaks.Where(t => t.Category == "Privacy" && t.Id != "location" && t.IsChecked).ToList();
        var checks = new (string Id, string Icon, string Title, string Description, bool Done)[]
        {
            ("first-boost", "\uE945", "First Boost", "Turn on your first tweak", AllTweaks.Any(t => t.HasBackup)),
            ("crispy", "\uE9F9", "Crispy", "Reach a Spud Score of 85", _score?.Score >= 85),
            ("golden", "\uE734", "Golden Fries", "Reach a Spud Score of 95", _score?.Score >= 95),
            ("clean-sweep", "\uE74D", "Clean Sweep", "Clean 1 GB of junk files", _settings.JunkCleanedBytes >= 1L << 30),
            ("ram-ranger", "\uE950", "RAM Ranger", "Free 1 GB of RAM", _settings.RamFreedMb >= 1024),
            ("marathon", "\uE916", "Marathon", "Play one session of 2 hours or more", sessions.Any(s => s.Minutes >= 120)),
            ("dedicated", "\uE7FC", "Dedicated", "Track 10 hours of play", sessions.Sum(s => s.Minutes) >= 600),
            ("night-owl", "\uE708", "Night Owl", "Start a game between midnight and 5 AM", sessions.Any(s => s.Start.Hour < 5)),
            ("collector", "\uE8F1", "Collector", "Save 3 profiles", _profiles.Count >= 3),
            ("privacy-pro", "\uE72E", "Privacy Pro", "Turn on every Privacy tweak", privacy.Count > 0 && privacy.All(t => t.IsOn)),
            ("sharpshooter", "\uF272", "Sharpshooter", "Switch on the custom crosshair", _settings.Crosshair.Enabled),
            ("boosted-play", "\uE768", "Boost & Play", "Launch a game from the Library", _settings.Achievements.Contains("boosted-play")),
        };

        // Once unlocked, always unlocked (and announced once).
        var newly = checks.Where(c => c.Done && _settings.Achievements.Add(c.Id)).ToList();
        if (newly.Count > 0)
        {
            _settings.Save();
            if (_achievementsReady)
                foreach (var a in newly) _tray.ShowHint($"🏆 Achievement unlocked: {a.Title}! {a.Description}.");
        }
        return checks.Select(c => new Achievement(c.Id, c.Icon, c.Title, c.Description, _settings.Achievements.Contains(c.Id))).ToList();
    }

    /// <summary>The first check after startup only records; announcements start after that.</summary>
    bool _achievementsReady;

    /// <summary>The Spud Score is only final once every tweak is read and the health check is done.</summary>
    bool _healthLoaded;

    void UpdateAchievements()
    {
        // Until then the score can be briefly too high (no health problems known yet), which would unlock score badges by mistake.
        if (AchievementList == null || !_healthLoaded || AllTweaks.Any(t => !t.IsChecked)) return;
        var list = ComputeAchievements();
        _achievementsReady = true;
        AchievementList.ItemsSource = list;
        AchievementCount.Text = $"{list.Count(a => a.Unlocked)} / {list.Count}";
    }

    /// <summary>For achievements that come from an action rather than a state.</summary>
    void Unlock(string id)
    {
        if (!_settings.Achievements.Add(id)) return;
        _settings.Save();
        var a = ComputeAchievements().First(x => x.Id == id);
        _tray.ShowHint($"🏆 Achievement unlocked: {a.Title}! {a.Description}.");
        UpdateAchievements();
    }
}
