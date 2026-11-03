using System.IO;
using System.Text.Json;

namespace GameTweaks.Core;

/// <summary>User preferences, stored next to the backups.</summary>
public sealed class AppSettings
{
    public bool BoostEnabled { get; set; }
    public bool RaiseGamePriority { get; set; } = true;
    public bool LowerBackgroundApps { get; set; } = true;

    public List<string> GameProcesses { get; set; } =
    [
        "FortniteClient-Win64-Shipping", "VALORANT-Win64-Shipping", "cs2", "r5apex",
        "RobloxPlayerBeta", "Overwatch", "RocketLeague", "Minecraft.Windows", "javaw",
    ];

    public List<string> BackgroundProcesses { get; set; } =
    [
        "chrome", "msedge", "firefox", "opera", "OneDrive", "Spotify", "steamwebhelper", "EpicWebHelper",
    ];

    public bool CloseAppsOnGameStart { get; set; }
    /// <summary>While a game runs, close list apps again if they reopen.</summary>
    public bool KeepAppsClosed { get; set; } = true;
    /// <summary>While a game runs, free standby RAM when free memory gets low (admin only).</summary>
    public bool AutoFreeRam { get; set; } = true;
    /// <summary>Running totals for achievements.</summary>
    public long RamFreedMb { get; set; }
    public long JunkCleanedBytes { get; set; }
    public HashSet<string> Achievements { get; set; } = [];
    /// <summary>Turn off the startup entries of list apps (checked again each time SpudBoost starts).</summary>
    public bool DisableAppsAtStartup { get; set; }

    /// <summary>Apps that are safe to close for gaming: they restart themselves or reopen when needed.</summary>
    public List<string> AppsToClose { get; set; } =
    [
        "OneDrive", "ms-teams", "Teams", "Widgets", "WidgetService", "PhoneExperienceHost", "CrossDeviceResume",
        "GameBar", "XboxPcAppFT", "Copilot", "YourPhone", "Cortana", "AdobeARM", "CCXProcess",
    ];

    public string DnsProvider { get; set; } = "Cloudflare";

    /// <summary>Closing the window hides SpudBoost to the system tray so Game Boost keeps working.</summary>
    public bool MinimizeToTray { get; set; } = true;
    public bool TrayHintShown { get; set; }

    /// <summary>Create a Windows restore point before the first tweak is applied.</summary>
    public bool AutoRestorePoint { get; set; } = true;
    /// <summary>Accent colour as #RRGGBB (SpudBoost orange by default).</summary>
    public string AccentColor { get; set; } = "#FF7A1A";
    public bool GpuHeatAlerts { get; set; } = true;
    /// <summary>Library keys (e.g. "steam-730") of favourite games, shown first.</summary>
    public List<string> FavoriteGames { get; set; } = [];
    /// <summary>Which version of the Terms of Service was accepted (0 = not yet) and when.</summary>
    public int TermsAcceptedVersion { get; set; }
    public DateTime TermsAcceptedAt { get; set; }

    /// <summary>Newest version already announced with a notification, so each update pops up only once.</summary>
    public string? LastNotifiedUpdate { get; set; }

    /// <summary>Fortnite potato settings to put back automatically if Fortnite resets them; null = off.</summary>
    public GameTweaks.Fortnite.FortniteSnapshot? KeptFortniteSettings { get; set; }

    /// <summary>NVIDIA potato textures strength: 1 = Light (+1), 2 = Medium (+2), 3 = Max (+3).</summary>
    public int PotatoFlatness { get; set; } = 2;

    /// <summary>Clean temp files, logs and crash dumps automatically once a week.</summary>
    public bool AutoCleanWeekly { get; set; }
    public DateTime LastAutoClean { get; set; }

    /// <summary>Last time the "your GPU driver is old" reminder was shown.</summary>
    public DateTime LastDriverReminder { get; set; }

    /// <summary>Version whose "What's new" pop-up was last shown (or that was freshly installed).</summary>
    public string? LastSeenVersion { get; set; }

    /// <summary>The first-run welcome screen (pick a preset) has been shown.</summary>
    public bool WelcomeShown { get; set; }


    public CrosshairSettings Crosshair { get; set; } = new();

    public StatsOverlaySettings StatsOverlay { get; set; } = new();

    static string FilePath => Path.Combine(BackupStore.Folder, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new();
        }
        catch (JsonException) { }
        return new();
    }

    public void Save()
    {
        Directory.CreateDirectory(BackupStore.Folder);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}

/// <summary>The in-game stats readout (temperatures, usage, ping) shown in a corner over Fortnite.</summary>
public sealed class StatsOverlaySettings
{
    public bool Enabled { get; set; }
    public string Corner { get; set; } = "Top right";
    /// <summary>Key that shows/hides the overlay in game. Empty = no hotkey.</summary>
    public string ToggleKey { get; set; } = "F9";
    /// <summary>Fortnite region to ping, e.g. "NA-East".</summary>
    public string PingRegion { get; set; } = "NA-East";
}

/// <summary>How one crosshair looks.</summary>
public class CrosshairLook
{
    public string Style { get; set; } = "Cross";
    public string Color { get; set; } = "#00FF66";
    public double Size { get; set; } = 10;
    public double Thickness { get; set; } = 2;
    public double Gap { get; set; } = 3;
    public double Opacity { get; set; } = 1;
    public bool Outline { get; set; } = true;
    public bool CenterDot { get; set; }
}

/// <summary>A crosshair used after pressing one of its keys (e.g. the slot your shotgun is in).</summary>
public sealed class WeaponCrosshair : CrosshairLook
{
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;
    /// <summary>Keys that switch to this crosshair, e.g. "2, Mouse4".</summary>
    public string Keys { get; set; } = "";
}

/// <summary>The custom crosshair overlay. Its own look fields are the default crosshair.</summary>
public sealed class CrosshairSettings : CrosshairLook
{
    public bool Enabled { get; set; }
    /// <summary>Only show while Fortnite is the window in front (hidden on the desktop and when alt-tabbed).</summary>
    public bool OnlyInGame { get; set; } = true;
    public double OffsetX { get; set; }
    public double OffsetY { get; set; }

    /// <summary>Keys that switch back to the default crosshair (Fortnite's default pickaxe and other slots).</summary>
    public string DefaultKeys { get; set; } = "1, 3, 4, 5";

    /// <summary>Hide while right mouse is held, so a scope's own reticle is clear.</summary>
    public bool HideWhileAiming { get; set; } = true;

    /// <summary>Extra gap (px) while left mouse is held, like weapon bloom. 0 = off.</summary>
    public double FireSpread { get; set; }

    /// <summary>Key that shows/hides the crosshair while in game. Empty = no hotkey.</summary>
    public string ToggleKey { get; set; } = "F8";

    public List<WeaponCrosshair> Weapons { get; set; } =
    [
        new() { Name = "Shotgun", Keys = "2", Style = "Circle + cross", Size = 6, Gap = 16, Thickness = 2, CenterDot = true },
        new() { Name = "Sniper", Keys = "6", Style = "Dot", Thickness = 2, Color = "#FF2D2D" },
    ];
}
