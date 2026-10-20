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

    /// <summary>Tweaks page sections, in display order.</summary>
    public static readonly string[] Categories = ["Performance", "Gaming", "Mouse & keyboard", "Windows clutter", "Privacy", "Other"];

    static readonly Dictionary<string, string> CategoryOf = new()
    {
        ["power-plan"] = "Performance", ["game-priority"] = "Performance", ["foreground-boost"] = "Performance",
        ["power-throttling"] = "Performance", ["memory-compression"] = "Performance", ["memory-integrity"] = "Performance", ["hags"] = "Performance",
        ["game-mode"] = "Gaming", ["game-dvr"] = "Gaming", ["game-bar-overlay"] = "Gaming", ["windowed-optimizations"] = "Gaming",
        ["fortnite-gpu"] = "Gaming", ["fortnite-fso"] = "Gaming", ["nvidia-profiles"] = "Gaming",
        ["mouse-accel"] = "Mouse & keyboard", ["sticky-keys"] = "Mouse & keyboard",
        ["transparency"] = "Windows clutter", ["visual-effects"] = "Windows clutter", ["background-apps"] = "Windows clutter",
        ["windows-tips"] = "Windows clutter", ["search-indexing"] = "Windows clutter", ["store-auto-updates"] = "Windows clutter",
        ["core-parking"] = "Performance", ["usb-suspend"] = "Performance", ["timer-resolution"] = "Performance", ["sysmain"] = "Performance",
        ["auto-maintenance"] = "Performance", ["fast-startup"] = "Performance",
        ["mpo-off"] = "Gaming", ["fullscreen-optimizations-global"] = "Gaming",
        ["keyboard-repeat"] = "Mouse & keyboard",
        ["menu-delay"] = "Windows clutter", ["startup-delay"] = "Windows clutter", ["edge-background"] = "Windows clutter",
        ["telemetry"] = "Privacy", ["diagtrack"] = "Privacy", ["advertising-id"] = "Privacy", ["tailored-experiences"] = "Privacy",
        ["promo-apps"] = "Privacy", ["activity-history"] = "Privacy", ["bing-search"] = "Privacy", ["typing-data"] = "Privacy",
        ["online-speech"] = "Privacy", ["feedback-prompts"] = "Privacy", ["app-tracking"] = "Privacy", ["copilot"] = "Privacy",
        ["recall"] = "Privacy", ["location"] = "Privacy",
    };

    /// <summary>System tweaks, sorted into their Tweaks page sections.</summary>
    public static List<Tweak> System(Func<IEnumerable<string>> gameProcesses)
    {
        var list = SystemUnsorted(gameProcesses);
        foreach (var t in list) t.Category = CategoryOf.GetValueOrDefault(t.Id, "Other");
        return list.OrderBy(t => Array.IndexOf(Categories, t.Category)).ToList(); // stable: keeps order within a section
    }

    static List<Tweak> SystemUnsorted(Func<IEnumerable<string>> gameProcesses)
    {
        var list = SystemCore();
        list.AddRange(More());
        if (NvidiaProfileTweak.HasNvidiaGpu)
            list.Add(new NvidiaProfileTweak
            {
                Id = "nvidia-profiles",
                Name = "NVIDIA: performance driver settings for your games",
                Description = "Sets maximum-performance power, unlimited shader cache (less stutter) and Ultra low latency on the NVIDIA profiles of the games in your Game Boost list. Downloads NVIDIA Profile Inspector (open source, 0.4 MB) on first use.",
                Settings = () => NvidiaProfileTweak.PerformanceSettings,
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
    public static List<Tweak> Fortnite(Func<int> flatness)
    {
        if (!NvidiaProfileTweak.HasNvidiaGpu || EpicGames.FortniteExe == null) return [];
        return
        [
            new NvidiaProfileTweak
            {
                Id = "nvidia-potato",
                Name = "NVIDIA potato textures",
                Description = "Makes your NVIDIA driver use lower-detail, blurrier textures in Fortnite (texture LOD bias, strength set by Flatness below, " +
                              "high-performance filtering). Surfaces turn into flat colours while hills and the ground stay readable. Changes only the driver, not the game. " +
                              "Needs administrator rights; applies next time Fortnite starts.",
                Settings = () => NvidiaProfileTweak.PotatoTextureSettings(flatness()),
                RetiredSettings = NvidiaProfileTweak.RetiredPotatoSettings,
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
            Id = "sticky-keys",
            Name = "Turn off Sticky Keys pop-ups",
            Description = "Stops the Sticky Keys / Filter Keys / Toggle Keys pop-up that kicks you out of your game when you press Shift five times or hold it down.",
            Values =
            [
                CuS(@"Control Panel\Accessibility\StickyKeys", "Flags", "506"),
                CuS(@"Control Panel\Accessibility\Keyboard Response", "Flags", "122"),
                CuS(@"Control Panel\Accessibility\ToggleKeys", "Flags", "58"),
            ],
            TakesEffect = TakesEffect.AfterSignOut,
        },
        new RegistryTweak
        {
            Id = "windowed-optimizations",
            Name = "Optimizations for windowed games",
            Description = "Windows 11 runs borderless and windowed games with the same low-latency path as fullscreen. Lowers input delay in Windowed fullscreen (which the crosshair overlay needs). Restart the game to apply.",
            Values = [CuS(@"Software\Microsoft\DirectX\UserGpuPreferences", "DirectXUserGlobalSettings", "SwapEffectUpgradeEnable=1;")],
        },
        new RegistryTweak
        {
            Id = "windows-tips",
            Name = "Turn off Windows tips and suggestions",
            Description = "Stops \"tips\", app suggestions and welcome pop-ups that can appear over your game.",
            Values =
            [
                Cu(@"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-338389Enabled", 0),
                Cu(@"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SoftLandingEnabled", 0),
                Cu(@"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SystemPaneSuggestionsEnabled", 0),
            ],
        },
        new RegistryTweak
        {
            Id = "store-auto-updates",
            Name = "Stop Microsoft Store auto-updating apps",
            Description = "The Store downloads and installs app updates in the background every 15–30 minutes, which causes regular stutters mid-game. Apps then update when you open the Store. Windows Update itself is not affected.",
            Values = [Lm(@"SOFTWARE\Policies\Microsoft\WindowsStore", "AutoDownload", 2)],
            RequiresAdmin = true,
        },
        new ScriptTweak
        {
            Id = "search-indexing",
            Name = "Turn off Windows Search indexing",
            Description = "Stops Windows constantly scanning your files in the background, which can cause stutter and disk use mid-game. Searching in Start still works, just a little slower.",
            CheckScript = "(Get-Service WSearch).StartType -eq 'Disabled'",
            ApplyScript = "Stop-Service WSearch -Force -ErrorAction SilentlyContinue; Set-Service WSearch -StartupType Disabled",
            RevertScript = "sc.exe config WSearch start= delayed-auto | Out-Null; Start-Service WSearch",
            RequiresAdmin = true,
        },
        new RegistryTweak
        {
            Id = "game-bar-overlay",
            Name = "Disable Xbox Game Bar overlay",
            Description = "Stops the Game Bar overlay loading into games and the Win+G shortcut. Saves a little CPU and memory.",
            Values = [Cu(@"Software\Microsoft\GameBar", "UseNexusForGameBarEnabled", 0)],
        },
    ];

    const string ContentDelivery = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";
    const string DataCollectionPolicy = @"SOFTWARE\Policies\Microsoft\Windows\DataCollection";
    const string UsbSub = "2a737441-1930-4402-8d77-b2bebba308a3";
    const string UsbSuspend = "48e6b7a6-50f5-4782-a5d4-53bb8f07e226";

    /// <summary>
    /// Script for a setting of the active power plan: prints True when its plugged-in value equals <paramref name="wanted"/>.
    /// Reads the hex values (the last two are plugged in / on battery), so it works in any Windows language.
    /// </summary>
    static string PowerCheck(string sub, string setting, int wanted) =>
        $"$v = [regex]::Matches((powercfg /qh scheme_current {sub} {setting} | Out-String), '0x[0-9a-fA-F]{{8}}'); " +
        $"$v.Count -ge 2 -and [Convert]::ToInt32($v[$v.Count - 2].Value, 16) -eq {wanted}";

    static string PowerSet(string sub, string setting, string value) =>
        $"powercfg /setacvalueindex scheme_current {sub} {setting} {value}; if ($LASTEXITCODE) {{ exit 1 }}; powercfg /setactive scheme_current";

    static string ServiceOff(string name) => $"Stop-Service {name} -Force -ErrorAction SilentlyContinue; Set-Service {name} -StartupType Disabled";
    static string ServiceOn(string name) => $"Set-Service {name} -StartupType Automatic; Start-Service {name} -ErrorAction SilentlyContinue";

    /// <summary>Added in 1.5.0: more performance tweaks and a Privacy section.</summary>
    static List<Tweak> More() =>
    [
        new ScriptTweak
        {
            Id = "core-parking",
            Name = "Keep all CPU cores awake (no core parking)",
            Description = "Stops Windows putting CPU cores to sleep to save power. Sleeping cores take time to wake, which shows up as stutter and 1% low drops when a game suddenly needs them.",
            CheckScript = PowerCheck("sub_processor", "CPMINCORES", 100),
            ApplyScript = PowerSet("sub_processor", "CPMINCORES", "100"),
            // Windows' own default: 100% on the High/Ultimate performance plans, 10% on Balanced.
            RevertScript = "$p = powercfg /getactivescheme | Out-String; $d = if ($p -match '8c5e7fda|e9a42b02') { 100 } else { 10 }; " +
                           "powercfg /setacvalueindex scheme_current sub_processor CPMINCORES $d; powercfg /setactive scheme_current",
            RequiresAdmin = true,
        },
        new ScriptTweak
        {
            Id = "usb-suspend",
            Name = "Disable USB selective suspend",
            Description = "Stops Windows powering down USB ports to save energy. A sleeping port can make your mouse, keyboard or headset skip or lag for a moment when it wakes.",
            CheckScript = PowerCheck(UsbSub, UsbSuspend, 0),
            ApplyScript = PowerSet(UsbSub, UsbSuspend, "0"),
            RevertScript = PowerSet(UsbSub, UsbSuspend, "1"),
            RequiresAdmin = true,
        },
        new RegistryTweak
        {
            Id = "timer-resolution",
            Name = "Precise game timers",
            Description = "Windows 11 lowers timer precision for programs that aren't in focus or are minimised. This lets games keep the precise timing they ask for, for smoother frame pacing.",
            Values = [Lm(@"SYSTEM\CurrentControlSet\Control\Session Manager\kernel", "GlobalTimerResolutionRequests", 1)],
            RequiresAdmin = true,
            TakesEffect = TakesEffect.AfterRestart,
        },
        new RegistryTweak
        {
            Id = "mpo-off",
            Name = "Disable Multiplane Overlay (MPO)",
            Description = "A known cause of flickering, black screens and stutter in games and on second monitors, especially with NVIDIA cards. Turning it off is the fix NVIDIA itself has recommended.",
            Values = [Lm(@"SOFTWARE\Microsoft\Windows\Dwm", "OverlayTestMode", 5)],
            RequiresAdmin = true,
            TakesEffect = TakesEffect.AfterRestart,
        },
        new ScriptTweak
        {
            Id = "sysmain",
            Name = "Turn off SysMain (Superfetch)",
            Description = "SysMain preloads apps into memory and can cause disk and CPU spikes in the background. With an SSD it adds very little. Apps may open a moment slower the first time.",
            CheckScript = "(Get-Service SysMain).StartType -eq 'Disabled'",
            ApplyScript = ServiceOff("SysMain"),
            RevertScript = ServiceOn("SysMain"),
            RequiresAdmin = true,
        },
        new RegistryTweak
        {
            Id = "auto-maintenance",
            Name = "Stop automatic maintenance",
            Description = "Windows runs disk optimisation, scans and other housekeeping when it thinks you're idle, which can kick in mid-game. Windows Update and Defender still run normally.",
            Values = [Lm(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Schedule\Maintenance", "MaintenanceDisabled", 1)],
            RequiresAdmin = true,
        },
        new RegistryTweak
        {
            Id = "edge-background",
            Name = "Stop Microsoft Edge running in the background",
            Description = "Edge keeps itself running after you close it (Startup boost and background mode), using memory and CPU while you play. Edge just opens a little slower.",
            Values =
            [
                Lm(@"SOFTWARE\Policies\Microsoft\Edge", "StartupBoostEnabled", 0),
                Lm(@"SOFTWARE\Policies\Microsoft\Edge", "BackgroundModeEnabled", 0),
            ],
            RequiresAdmin = true,
        },
        new RegistryTweak
        {
            Id = "fast-startup",
            Name = "Turn off Fast Startup",
            Description = "With Fast Startup, \"Shut down\" only half-shuts Windows, so drivers and memory are never fully reset and problems build up. Shutting down then gives a truly fresh start. Booting takes a few seconds longer.",
            Values = [Lm(@"SYSTEM\CurrentControlSet\Control\Session Manager\Power", "HiberbootEnabled", 0)],
            RequiresAdmin = true,
        },
        new RegistryTweak
        {
            Id = "fullscreen-optimizations-global",
            Name = "Exclusive fullscreen for every game",
            Description = "Games set to Fullscreen run in true exclusive fullscreen instead of Windows' borderless hybrid. Can lower input delay. Windowed fullscreen (which the crosshair overlay uses) isn't affected.",
            Values =
            [
                Cu(@"System\GameConfigStore", "GameDVR_FSEBehaviorMode", 2),
                Cu(@"System\GameConfigStore", "GameDVR_HonorUserFSEBehaviorMode", 1),
                Cu(@"System\GameConfigStore", "GameDVR_FSEBehavior", 2),
                Cu(@"System\GameConfigStore", "GameDVR_DXGIHonorFSEWindowsCompatible", 1),
            ],
            TakesEffect = TakesEffect.AfterSignOut,
        },
        new RegistryTweak
        {
            Id = "keyboard-repeat",
            Name = "Fastest key repeat",
            Description = "Shortest delay and fastest rate when you hold a key down, so held keys respond straight away.",
            Values = [CuS(@"Control Panel\Keyboard", "KeyboardDelay", "0"), CuS(@"Control Panel\Keyboard", "KeyboardSpeed", "31")],
            TakesEffect = TakesEffect.AfterSignOut,
        },
        new RegistryTweak
        {
            Id = "menu-delay",
            Name = "Instant menus",
            Description = "Removes the built-in pause before Windows menus and submenus open.",
            Values = [CuS(@"Control Panel\Desktop", "MenuShowDelay", "0")],
            TakesEffect = TakesEffect.AfterSignOut,
        },
        new RegistryTweak
        {
            Id = "startup-delay",
            Name = "No delay for startup apps",
            Description = "Windows waits about 10 seconds after you sign in before starting your startup apps. This removes the wait.",
            Values = [Cu(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize", "StartupDelayInMSec", 0)],
        },

        // ---- Privacy (never counted in the Spud Score: it's a personal choice) ----
        new RegistryTweak
        {
            Id = "telemetry",
            Name = "Send Microsoft the minimum diagnostic data",
            Description = "Limits the usage and diagnostic data Windows sends to Microsoft to the required minimum (no optional data about the apps you use and sites you visit).",
            Values = [Lm(DataCollectionPolicy, "AllowTelemetry", 0), Lm(DataCollectionPolicy, "DoNotShowFeedbackNotifications", 1)],
            RequiresAdmin = true,
        },
        new ScriptTweak
        {
            Id = "diagtrack",
            Name = "Turn off the telemetry service",
            Description = "Stops \"Connected User Experiences and Telemetry\" (DiagTrack), the service that collects and uploads diagnostic data in the background. Also saves a little CPU and disk.",
            CheckScript = "(Get-Service DiagTrack).StartType -eq 'Disabled'",
            ApplyScript = ServiceOff("DiagTrack"),
            RevertScript = ServiceOn("DiagTrack"),
            RequiresAdmin = true,
        },
        new RegistryTweak
        {
            Id = "advertising-id",
            Name = "Turn off your advertising ID",
            Description = "Stops apps using your Windows advertising ID to track you and show personalised ads.",
            Values = [Cu(@"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", 0)],
        },
        new RegistryTweak
        {
            Id = "tailored-experiences",
            Name = "No tailored experiences",
            Description = "Stops Microsoft using your diagnostic data to show you personalised tips, ads and recommendations.",
            Values = [Cu(@"Software\Microsoft\Windows\CurrentVersion\Privacy", "TailoredExperiencesWithDiagnosticDataEnabled", 0)],
        },
        new RegistryTweak
        {
            Id = "promo-apps",
            Name = "Stop ads and auto-installed promo apps",
            Description = "Stops Windows silently installing promoted apps and games, and removes suggestions and ads from Start, Settings and the lock screen.",
            Values =
            [
                Cu(ContentDelivery, "SilentInstalledAppsEnabled", 0),
                Cu(ContentDelivery, "PreInstalledAppsEnabled", 0),
                Cu(ContentDelivery, "OemPreInstalledAppsEnabled", 0),
                Cu(ContentDelivery, "SubscribedContent-338388Enabled", 0),
                Cu(ContentDelivery, "SubscribedContent-338393Enabled", 0),
                Cu(ContentDelivery, "SubscribedContent-353694Enabled", 0),
                Cu(ContentDelivery, "SubscribedContent-353696Enabled", 0),
                Cu(ContentDelivery, "RotatingLockScreenOverlayEnabled", 0),
            ],
        },
        new RegistryTweak
        {
            Id = "activity-history",
            Name = "Turn off activity history",
            Description = "Stops Windows recording which apps, files and websites you open and uploading that timeline to Microsoft.",
            Values =
            [
                Lm(@"SOFTWARE\Policies\Microsoft\Windows\System", "PublishUserActivities", 0),
                Lm(@"SOFTWARE\Policies\Microsoft\Windows\System", "UploadUserActivities", 0),
            ],
            RequiresAdmin = true,
        },
        new RegistryTweak
        {
            Id = "bing-search",
            Name = "No web results in Start search",
            Description = "Start menu search only searches your PC instead of sending what you type to Bing. Also makes search a bit faster.",
            Values =
            [
                Cu(@"Software\Microsoft\Windows\CurrentVersion\Search", "BingSearchEnabled", 0),
                Cu(@"Software\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions", 1),
            ],
            RequiresAdmin = true,
            TakesEffect = TakesEffect.AfterSignOut,
        },
        new RegistryTweak
        {
            Id = "typing-data",
            Name = "Don't send typing and handwriting data",
            Description = "Stops Windows collecting what you type and write to \"improve\" typing, inking and suggestions.",
            Values =
            [
                Cu(@"Software\Microsoft\Input\TIPC", "Enabled", 0),
                Cu(@"Software\Microsoft\InputPersonalization", "RestrictImplicitInkCollection", 1),
                Cu(@"Software\Microsoft\InputPersonalization", "RestrictImplicitTextCollection", 1),
            ],
        },
        new RegistryTweak
        {
            Id = "online-speech",
            Name = "Turn off online speech recognition",
            Description = "Stops your voice being sent to Microsoft's cloud for dictation and voice typing.",
            Values = [Cu(@"Software\Microsoft\Speech_OneCore\Settings\OnlineSpeechPrivacy", "HasAccepted", 0)],
        },
        new RegistryTweak
        {
            Id = "feedback-prompts",
            Name = "No feedback requests",
            Description = "Stops Windows popping up \"How likely are you to recommend...\" feedback questions.",
            Values = [Cu(@"Software\Microsoft\Siuf\Rules", "NumberOfSIUFInPeriod", 0)],
        },
        new RegistryTweak
        {
            Id = "app-tracking",
            Name = "Don't track which apps you launch",
            Description = "Stops Windows keeping a list of the apps you open most to personalise Start and search. Start's \"most used\" list will be empty.",
            Values = [Cu(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "Start_TrackProgs", 0)],
        },
        new RegistryTweak
        {
            Id = "copilot",
            Name = "Turn off Windows Copilot",
            Description = "Turns off Copilot in Windows (on versions that support the switch) so it doesn't run or collect anything.",
            Values = [Cu(@"Software\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot", 1)],
            RequiresAdmin = true,
            TakesEffect = TakesEffect.AfterSignOut,
        },
        new RegistryTweak
        {
            Id = "recall",
            Name = "Turn off Recall snapshots",
            Description = "On Copilot+ PCs, Recall takes regular screenshots of everything you do. This turns that off. Does nothing on PCs without Recall.",
            Values = [Cu(@"Software\Policies\Microsoft\Windows\WindowsAI", "DisableAIDataAnalysis", 1)],
            RequiresAdmin = true,
            TakesEffect = TakesEffect.AfterSignOut,
        },
        new RegistryTweak
        {
            Id = "location",
            Name = "Turn off location access",
            Description = "Stops Windows and apps using your location. Weather, maps and Find my device won't know where you are.",
            Values = [LmS(@"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\location", "Value", "Deny")],
            RequiresAdmin = true,
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
            Id = "nic-lso",
            Name = "Disable Large Send Offload",
            Description = "Stops the network adapter batching outgoing data into big chunks, a known cause of ping spikes and delayed packets on many adapters. Your connection drops for a few seconds while this applies.",
            Properties = ["Large Send Offload V2 (IPv4)", "Large Send Offload V2 (IPv6)", "Large Send Offload (IPv4)", "Large Send Offload v2 (IPv4)", "Large Send Offload v2 (IPv6)"],
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
