namespace GameTweaks.Tweaks;

/// <summary>A one-click bundle of tweaks, plus optionally a Fortnite graphics preset.</summary>
public sealed record Preset(string Name, string Emoji, string Summary, string[] TweakIds, string? FortnitePreset);

/// <summary>
/// The three starting points shown on the welcome screen and the Tweaks page. Memory Integrity is never in a preset
/// (it needs its own confirmation), and neither is "disable fullscreen optimizations", which would hide the crosshair overlay.
/// </summary>
public static class Presets
{
    static readonly string[] SafeTweaks =
    [
        "power-plan", "game-mode", "game-dvr", "game-bar-overlay", "windowed-optimizations", "mouse-accel", "sticky-keys",
        "windows-tips", "transparency", "background-apps", "fortnite-gpu", "delivery-optimization", "usb-suspend", "startup-delay", "menu-delay",
        "edge-background",
    ];

    static readonly string[] CompetitiveTweaks =
    [
        .. SafeTweaks,
        "foreground-boost", "game-priority", "hags", "power-throttling", "memory-compression", "nvidia-profiles",
        "nagle", "net-throttle", "nic-power", "nic-lso", "store-auto-updates", "core-parking", "timer-resolution", "mpo-off", "auto-maintenance",
        "keyboard-repeat",
    ];

    static readonly string[] MaxFpsTweaks =
    [
        .. CompetitiveTweaks,
        "visual-effects", "search-indexing", "nic-interrupt", "nvidia-potato", "sysmain",
    ];

    public static readonly Preset[] All =
    [
        new("Safe", "🛡", "Easy wins with no downsides: power plan, Game Mode, no background recording or pop-ups, raw mouse input.", SafeTweaks, null),
        new("Competitive", "🎯", "Safe + CPU and GPU priority, low-latency network tweaks and NVIDIA performance settings. Fortnite: low settings, sharp image, far view distance.", CompetitiveTweaks, "competitive"),
        new("Max FPS", "🥔", "Everything: Competitive + no animations, no search indexing and NVIDIA potato textures. Fortnite: Ultra Potato.", MaxFpsTweaks, "ultra"),
    ];
}
