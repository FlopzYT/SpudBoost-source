using System.IO;
using GameTweaks.Core;
using Microsoft.Win32;

namespace GameTweaks.Tweaks;

/// <summary>Every tweak the app offers, grouped by page.</summary>
public static class TweakCatalog
{
    const string SystemProfile = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";
    const string TcpInterfaces = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces";

    static RegValue Cu(string key, string name, int value) => new(RegistryHive.CurrentUser, key, name, RegistryValueKind.DWord, value);
    static RegValue CuS(string key, string name, string value) => new(RegistryHive.CurrentUser, key, name, RegistryValueKind.String, value);
    static RegValue Lm(string key, string name, int value) => new(RegistryHive.LocalMachine, key, name, RegistryValueKind.DWord, value);
    static RegValue LmS(string key, string name, string value) => new(RegistryHive.LocalMachine, key, name, RegistryValueKind.String, value);

    public static List<Tweak> System(Func<IEnumerable<string>> gameProcesses)
    {
        var list = SystemCore();
        if (NvidiaProfileTweak.HasNvidiaGpu)
            list.Add(new NvidiaProfileTweak
            {
                Id = "nvidia-profiles",
                Name = "NVIDIA: performance driver settings for your games",
                Description = "Sets maximum-performance power, unlimited shader cache (less stutter) and Ultra low latency on the NVIDIA profiles of the games in your Game Boost list. Downloads NVIDIA Profile Inspector (open source, 0.4 MB) on first use.",
                Settings = NvidiaProfileTweak.PerformanceSettings,
                BackupFileName = "nvidia-original.nip",
                GameProcesses = gameProcesses,
                RequiresAdmin = true,
            });
        if (EpicGames.FortniteExe is { } fortnite)
        {
            list.Add(new RegistryTweak
            {
                Id = "fortnite-gpu",
                Name = "Fortnite: use the high-performance GPU",
                Description = "Tells Windows to always run Fortnite on your dedicated graphics card. Huge gain on laptops that otherwise use the weak built-in graphics; harmless on desktops.",
                Values = [CuS(@"Software\Microsoft\DirectX\UserGpuPreferences", fortnite, "GpuPreference=2;")],
            });
            list.Add(new RegistryTweak
            {
                Id = "fortnite-fso",
                Name = "Fortnite: disable fullscreen optimizations",
                Description = "Runs Fortnite in true exclusive fullscreen instead of Windows' borderless hybrid mode. Can lower input delay and stutter. Applies next time Fortnite starts.",
                Values = [CuS(@"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers", fortnite, "~ DISABLEDXMAXIMIZEDWINDOWEDMODE")],
            });
        }
        return list;
    }

    /// <summary>Tweaks shown on the Fortnite Potato page.</summary>
    public static List<Tweak> Fortnite()
    {
        if (!NvidiaProfileTweak.HasNvidiaGpu || EpicGames.FortniteExe == null) return [];
        return
        [
            new NvidiaProfileTweak
            {
                Id = "nvidia-potato",
                Name = "NVIDIA potato textures",
                Description = "Makes your NVIDIA driver use the blurriest, lowest-detail version of every texture in Fortnite (texture LOD bias +3, " +
                              "high-performance filtering, anisotropic filtering off). Surfaces turn into flat colours. Changes only the driver, not the game. " +
                              "Needs administrator rights; applies next time Fortnite starts.",
                Settings = NvidiaProfileTweak.PotatoTextureSettings,
                BackupFileName = "nvidia-potato.nip",
                GameProcesses = () => ["FortniteClient-Win64-Shipping"],
                RequiresAdmin = true,
            },
        ];
    }

    static List<Tweak> SystemCore() =>
    [
        new PowerPlanTweak
        {
            Id = "power-plan",
            Name = "High performance power plan",
            Description = "Keeps the CPU at full clock speed instead of parking cores to save power.",
        },
        new RegistryTweak
        {
            Id = "game-mode",
            Name = "Windows Game Mode",
            Description = "Lets Windows prioritise the game and pause Windows Update installs while you play.",
            Values = [Cu(@"Software\Microsoft\GameBar", "AutoGameModeEnabled", 1), Cu(@"Software\Microsoft\GameBar", "AllowAutoGameMode", 1)],
        },
        new RegistryTweak
        {
            Id = "game-dvr",
            Name = "Disable background recording",
            Description = "Turns off Xbox Game Bar background capture, which costs FPS even when you never use it.",
            Values = [Cu(@"System\GameConfigStore", "GameDVR_Enabled", 0), Cu(@"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 0)],
        },
        new RegistryTweak
        {
            Id = "hags",
            Name = "Hardware-accelerated GPU scheduling",
            Description = "Lets the GPU manage its own memory scheduling. Lowers latency on most modern GPUs.",
            Values = [Lm(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", 2)],
            RequiresAdmin = true,
            TakesEffect = TakesEffect.AfterRestart,
        },
        new RegistryTweak
        {
            Id = "game-priority",
            Name = "Game scheduling priority",
            Description = "Gives games more CPU and GPU time in the Windows multimedia scheduler.",
            Values =
            [
                Lm(SystemProfile, "SystemResponsiveness", 10),
                Lm($@"{SystemProfile}\Tasks\Games", "GPU Priority", 8),
                Lm($@"{SystemProfile}\Tasks\Games", "Priority", 6),
                LmS($@"{SystemProfile}\Tasks\Games", "Scheduling Category", "High"),
                LmS($@"{SystemProfile}\Tasks\Games", "SFIO Priority", "High"),
            ],
            RequiresAdmin = true,
            TakesEffect = TakesEffect.AfterRestart,
        },
        new RegistryTweak
        {
            Id = "mouse-accel",
            Name = "Disable mouse acceleration",
            Description = "Turns off \"Enhance pointer precision\" so aim moves the same distance at any speed.",
            Values =
            [
                CuS(@"Control Panel\Mouse", "MouseSpeed", "0"),
                CuS(@"Control Panel\Mouse", "MouseThreshold1", "0"),
                CuS(@"Control Panel\Mouse", "MouseThreshold2", "0"),
            ],
            TakesEffect = TakesEffect.AfterSignOut,
        },
        new RegistryTweak
        {
            Id = "transparency",
            Name = "Disable transparency effects",
            Description = "Removes blur and see-through effects from the taskbar and Start menu.",
            Values = [Cu(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", 0)],
        },
        new RegistryTweak
        {
            Id = "visual-effects",
            Name = "Visual effects: best performance",
            Description = "Turns off window animations and shadows to free up resources.",
            Values = [Cu(@"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting", 2)],
            TakesEffect = TakesEffect.AfterSignOut,
        },
        new RegistryTweak
        {
            Id = "foreground-boost",
            Name = "Prioritise the active window",
            Description = "Gives the program you're using (your game) longer, more frequent CPU time slices than background programs.",
            Values = [Lm(@"SYSTEM\CurrentControlSet\Control\PriorityControl", "Win32PrioritySeparation", 0x26)],
            RequiresAdmin = true,
        },
        new RegistryTweak
        {
            Id = "power-throttling",
            Name = "Disable power throttling",
            Description = "Stops Windows slowing down programs it decides are in the background, such as voice chat and launchers running alongside your game.",
            Values = [Lm(@"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling", "PowerThrottlingOff", 1)],
            RequiresAdmin = true,
            TakesEffect = TakesEffect.AfterRestart,
        },
        new ScriptTweak
        {
            Id = "memory-compression",
            Name = "Disable memory compression",
            Description = "Windows compresses memory to fit more in RAM, which costs CPU time. With plenty of RAM (16 GB or more) that's wasted CPU, and your FPS is CPU-limited.",
            CheckScript = "-not (Get-MMAgent).MemoryCompression",
            ApplyScript = "Disable-MMAgent -MemoryCompression",
            RevertScript = "Enable-MMAgent -MemoryCompression",
            RequiresAdmin = true,
            TakesEffect = TakesEffect.AfterRestart,
        },
        new RegistryTweak
        {
            Id = "memory-integrity",
            Name = "Disable Memory Integrity (Core isolation)",
            Description = "Big CPU saving on Intel 6th–9th gen and AMD Ryzen 1000 CPUs, which have to emulate this security feature in software. Lowers protection against kernel-level malware.",
            Values = [Lm(@"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity", "Enabled", 0)],
            RequiresAdmin = true,
            TakesEffect = TakesEffect.AfterRestart,
            ConfirmMessage =
                "Memory Integrity blocks malicious drivers from taking over Windows' core. Turning it off can raise FPS on older CPUs " +
                "like yours, but your PC loses that protection.\n\n" +
                "Only do this if you don't install drivers or software from untrusted sources. Some games' anti-cheat may ask you to turn it back on; " +
                "you can switch it back here or in Windows Security → Device security → Core isolation.\n\nTurn Memory Integrity off?",
        },
        new RegistryTweak
        {
            Id = "background-apps",
            Name = "Stop Store apps running in the background",
            Description = "Prevents Microsoft Store apps from running, syncing and refreshing in the background while you play.",
            Values = [Cu(@"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled", 1)],
        },
        new RegistryTweak
        {
            Id = "game-bar-overlay",
            Name = "Disable Xbox Game Bar overlay",
            Description = "Stops the Game Bar overlay loading into games and the Win+G shortcut. Saves a little CPU and memory.",
            Values = [Cu(@"Software\Microsoft\GameBar", "UseNexusForGameBarEnabled", 0)],
        },
    ];

    public static List<Tweak> Network(string dnsProvider) =>
    [
        new RegistryTweak
        {
            Id = "nagle",
            Name = "Disable Nagle's algorithm",
            Description = "Sends small game packets immediately instead of batching them. Can lower ping spikes.",
            Values = ActiveInterfaces()
                .SelectMany(i => new[] { Lm($@"{TcpInterfaces}\{i}", "TcpAckFrequency", 1), Lm($@"{TcpInterfaces}\{i}", "TCPNoDelay", 1) })
                .ToList(),
            RequiresAdmin = true,
            TakesEffect = TakesEffect.AfterRestart,
        },
        new RegistryTweak
        {
            Id = "net-throttle",
            Name = "Disable network throttling",
            Description = "Stops Windows limiting network traffic while media is playing.",
            Values = [Lm(SystemProfile, "NetworkThrottlingIndex", unchecked((int)0xFFFFFFFF))],
            RequiresAdmin = true,
            TakesEffect = TakesEffect.AfterRestart,
        },
        new AdapterPropertyTweak
        {
            Id = "nic-power",
            Name = "Disable network adapter power saving",
            Description = "Stops the adapter dozing between packets (Energy Efficient Ethernet, Green Ethernet, low-power modes). Waking up adds small lag spikes. Your connection drops for a few seconds while this applies.",
            Properties =
            [
                "Energy Efficient Ethernet", "Advanced EEE", "Green Ethernet", "Ultra Low Power Mode",
                "Reduce Speed On Power Down", "Power Saving Mode", "Gigabit Lite", "Link Speed Battery Saver",
            ],
            PowerManagement = true,
            RequiresAdmin = true,
        },
        new AdapterPropertyTweak
        {
            Id = "nic-interrupt",
            Name = "Disable interrupt moderation",
            Description = "Handles each network packet immediately instead of in batches. Slightly lower latency, slightly more CPU use. Your connection drops for a few seconds while this applies.",
            Properties = ["Interrupt Moderation"],
            RequiresAdmin = true,
        },
        new RegistryTweak
        {
            Id = "delivery-optimization",
            Name = "Stop Windows Update sharing uploads",
            Description = "Windows can upload updates to other PCs over the internet in the background. Turning this off keeps your upload free for games.",
            Values = [Lm(@"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization", "DODownloadMode", 0)],
            RequiresAdmin = true,
        },
        new DnsTweak
        {
            Id = "dns",
            Name = "Fast public DNS",
            Description = "Uses the DNS provider picked above on every connected adapter. Can speed up lookups.",
            RequiresAdmin = true,
            Provider = dnsProvider,
        },
    ];

    /// <summary>TCP/IP interface GUIDs that currently have an IPv4 address.</summary>
    static IEnumerable<string> ActiveInterfaces() =>
        RegistryHelper.SubKeyNames(RegistryHive.LocalMachine, TcpInterfaces).Where(guid =>
        {
            var dhcp = RegistryHelper.Read(RegistryHive.LocalMachine, $@"{TcpInterfaces}\{guid}", "DhcpIPAddress") as string;
            var fixedIp = RegistryHelper.Read(RegistryHive.LocalMachine, $@"{TcpInterfaces}\{guid}", "IPAddress") as string[];
            return (!string.IsNullOrEmpty(dhcp) && dhcp != "0.0.0.0") ||
                   (fixedIp is { Length: > 0 } && fixedIp[0] is { Length: > 0 } ip && ip != "0.0.0.0");
        });
}
