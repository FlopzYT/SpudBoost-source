using System.Diagnostics;
using System.Globalization;

namespace GameTweaks.Core;

public sealed record HealthItem(string Title, string Detail);

/// <summary>Spots the biggest performance problems that a toggle can't fix on its own (BIOS, hardware, habits).</summary>
public static class HealthCheck
{
    static readonly (string Process, string Label)[] HeavyBackground =
    [
        ("MedalEncoder", "Medal (clip recording)"), ("SignalRgb", "SignalRGB"), ("VoicemodV3", "Voicemod"),
        ("DubbingAI", "DubbingAI"), ("Overwolf", "Overwolf"), ("iCUE", "Corsair iCUE"), ("wallpaper64", "Wallpaper Engine"),
        ("obs64", "OBS"), ("Filmora", "Filmora"),
    ];

    public static List<HealthItem> Run()
    {
        var items = new List<HealthItem>();
        var info = Query();

        if (info.GetValueOrDefault("battery") == "1")
            items.Add(new("Laptop is running on battery",
                "Windows and your graphics card limit performance on battery. Plug in the charger while gaming."));

        if (info.TryGetValue("hz", out var hzText) && info.TryGetValue("maxhz", out var maxText) &&
            int.TryParse(hzText, out var hz) && int.TryParse(maxText, out var maxHz) && hz > 0 && maxHz > hz + 5)
            items.Add(new($"Monitor is set to {hz} Hz but supports {maxHz} Hz",
                "Windows often leaves high-refresh monitors at 60 Hz. Settings → System → Display → Advanced display → Choose a refresh rate → pick the highest. " +
                "You'll see far more of your FPS. Also make sure the cable is plugged into the graphics card, not the motherboard."));

        // DDR4 (type 26) defaults to 2133–2666 MHz without XMP; DDR5 (type 34) defaults to 4800 MHz.
        if (info.TryGetValue("ramspeed", out var ram) && int.TryParse(ram, out var mhz) && mhz > 0 &&
            ((info.GetValueOrDefault("ramtype") == "26" && mhz <= 2666) || (info.GetValueOrDefault("ramtype") == "34" && mhz <= 4800)))
            items.Add(new("Turn on XMP/EXPO for your RAM (often the biggest gain)",
                $"Your RAM runs at {mhz} MHz, the slow default speed. Most kits are rated much faster, but only reach it with XMP (Intel) or EXPO/DOCP (AMD) enabled in the BIOS. " +
                "In CPU-limited games that's often 10–20% more FPS. Restart, open the BIOS (usually Delete or F2), find XMP / EXPO / DOCP and set it to Profile 1."));

        if (info.TryGetValue("driverage", out var ageText) && int.TryParse(ageText, out var days) && days > 365)
            items.Add(new("Graphics driver is over a year old",
                $"Your GPU driver is {days / 30} months old. New drivers often add FPS and fix stutter in current games. Update it from NVIDIA, AMD or Intel's website (or their app)."));

        if (info.TryGetValue("freepct", out var freeText) && int.TryParse(freeText, out var free) && free < 10)
            items.Add(new($"System drive is {100 - free}% full",
                "Windows and games need free space for updates, shader caches and the page file. Under 10% free causes stutter. Free up space or uninstall unused games."));

        if (info.TryGetValue("ramgb", out var gbText) && int.TryParse(gbText, out var gb) && gb > 0 && gb < 16)
            items.Add(new($"Only {gb} GB of RAM",
                "Modern games want 16 GB. With less, close browsers and other apps before playing, and use Game Boost's close list."));

        if (info.GetValueOrDefault("hvci") == "1")
            items.Add(new("Memory Integrity is on",
                "This security feature costs CPU time, especially on Intel 6th–9th gen and Ryzen 1000 CPUs. See \"Disable Memory Integrity\" below for the trade-off."));

        if (info.TryGetValue("ethernet", out var link) && long.TryParse(link, out var bps) && bps > 0 && bps < 1_000_000_000)
            items.Add(new("Ethernet is running at only " + (bps / 1_000_000) + " Mbps",
                "Your network card supports 1 Gbps. This usually means a damaged or old cable or a loose plug. Try a different Cat5e/Cat6 cable or router port."));

        if (info.TryGetValue("plan", out var plan) && !plan.Contains("8c5e7fda", StringComparison.OrdinalIgnoreCase) &&
            !plan.Contains("e9a42b02", StringComparison.OrdinalIgnoreCase))
            items.Add(new("Not using a performance power plan", "Switch on \"High performance power plan\" below."));

        var running = HeavyBackground.Where(h => Process.GetProcessesByName(h.Process).Length > 0).Select(h => h.Label).ToList();
        if (running.Count > 0)
            items.Add(new("Heavy background apps are running",
                $"{string.Join(", ", running)} use CPU while you play. Close them before gaming, or add them to Game Boost's close list."));

        var startup = StartupManager.List().Where(e => e.Enabled && !e.Protected).ToList();
        var heavyStartup = startup.Where(e => e.Heavy).Select(e => e.Name).ToList();
        if (heavyStartup.Count > 0 || startup.Count > 10)
            items.Add(new($"{startup.Count} apps start with Windows" + (heavyStartup.Count > 0 ? $", {heavyStartup.Count} of them heavy" : ""),
                (heavyStartup.Count > 0 ? $"Heavy: {string.Join(", ", heavyStartup)}. " : "") +
                "Each one uses memory and CPU in the background. Turn off the ones you don't need on the Startup apps page."));

        return items;
    }

    static Dictionary<string, string> Query()
    {
        const string script = """
            $mem = @(Get-CimInstance Win32_PhysicalMemory)
            $m = $mem | Select-Object -First 1
            "ramspeed=$($m.ConfiguredClockSpeed)"
            "ramtype=$($m.SMBIOSMemoryType)"
            "ramgb=$([math]::Round(($mem | Measure-Object Capacity -Sum).Sum / 1GB))"
            $b = Get-CimInstance Win32_Battery -ErrorAction SilentlyContinue | Select-Object -First 1
            if ($b) { "battery=$(if ($b.BatteryStatus -eq 1) { 1 } else { 0 })" }
            $gpu = Get-CimInstance Win32_VideoController | Where-Object { $_.CurrentRefreshRate -gt 0 } | Sort-Object AdapterRAM -Descending | Select-Object -First 1
            if ($gpu) {
              "hz=$($gpu.CurrentRefreshRate)"
              "maxhz=$($gpu.MaxRefreshRate)"
              if ($gpu.DriverDate) { "driverage=$([int]((Get-Date) - $gpu.DriverDate).TotalDays)" }
            }
            $c = Get-CimInstance Win32_LogicalDisk -Filter "DeviceID='$env:SystemDrive'"
            if ($c.Size) { "freepct=$([int](100 * $c.FreeSpace / $c.Size))" }
            $dg = Get-CimInstance -Namespace root\Microsoft\Windows\DeviceGuard -ClassName Win32_DeviceGuard -ErrorAction SilentlyContinue
            "hvci=$(if ($dg.SecurityServicesRunning -contains 2) { 1 } else { 0 })"
            $e = Get-NetAdapter -Physical | Where-Object { $_.Status -eq 'Up' -and $_.MediaType -eq '802.3' } | Select-Object -First 1
            if ($e) { "ethernet=$($e.Speed)" }
            "plan=$(powercfg /getactivescheme)"
            """;
        var (_, output) = Shell.PowerShell(script);
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(l => l.Split('=', 2))
            .Where(p => p.Length == 2)
            .GroupBy(p => p[0])
            .ToDictionary(g => g.Key, g => g.First()[1].Trim(), StringComparer.OrdinalIgnoreCase);
    }
}
