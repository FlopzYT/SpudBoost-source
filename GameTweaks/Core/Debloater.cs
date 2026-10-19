namespace GameTweaks.Core;

/// <summary>A preinstalled app the Debloater can remove, with the package(s) it's made of.</summary>
public sealed class BloatApp : ObservableObject
{
    public required string Name { get; init; }
    /// <summary>Package names; a trailing ".*" matches any package starting with that prefix.</summary>
    public required string[] Packages { get; init; }
    /// <summary>Why someone might want to keep it. Shown as a warning.</summary>
    public string? Note { get; init; }

    public List<string> InstalledPackages { get; } = [];

    bool _selected;
    public bool Selected { get => _selected; set => SetField(ref _selected, value); }

    internal bool Matches(string package) => Packages.Any(p => p.EndsWith(".*", StringComparison.Ordinal)
        ? package.StartsWith(p[..^1], StringComparison.OrdinalIgnoreCase)
        : package.Equals(p, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Removes preinstalled Windows apps for the current user. Only apps from this curated list can be removed, so nothing
/// Windows needs (Store, sign-in, system components) is ever offered. Removed apps can be reinstalled from the Microsoft Store.
/// </summary>
public static class Debloater
{
    /// <summary>
    /// Never removed, whatever the catalog says: the Store and installer, Xbox sign-in pieces games depend on, security,
    /// the browser, everyday tools, shared runtime libraries, and GPU/audio driver companions.
    /// </summary>
    static readonly string[] Protected =
    [
        "Microsoft.WindowsStore", "Microsoft.StorePurchaseApp", "Microsoft.DesktopAppInstaller", "Microsoft.Services.Store.*",
        "Microsoft.Xbox.TCUI", "Microsoft.XboxIdentityProvider", "Microsoft.XboxSpeechToTextOverlay", "Microsoft.GamingServices",
        "Microsoft.SecHealthUI", "Microsoft.Windows.SecHealthUI", "Microsoft.MicrosoftEdge*", "Microsoft.Edge*",
        "Microsoft.Windows.Photos", "Microsoft.WindowsCalculator", "Microsoft.WindowsNotepad", "Microsoft.Paint", "Microsoft.ScreenSketch",
        "Microsoft.WindowsTerminal", "Microsoft.WindowsCamera", "Microsoft.HEIFImageExtension", "Microsoft.HEVCVideoExtension",
        "Microsoft.VP9VideoExtensions", "Microsoft.WebpImageExtension", "Microsoft.WebMediaExtensions", "Microsoft.RawImageExtension",
        "Microsoft.VCLibs*", "Microsoft.UI.Xaml*", "Microsoft.NET*", "Microsoft.WindowsAppRuntime*", "Microsoft.DirectX*",
        "Microsoft.Windows.*", "MicrosoftWindows.*", "Windows.*", "Microsoft.AAD.*", "Microsoft.AccountsControl",
        "NVIDIACorp.*", "AdvancedMicroDevicesInc*", "AppUp.Intel*", "RealtekSemiconductorCorp.*", "DolbyLaboratories.*",
    ];

    static bool IsProtected(string package) => Protected.Any(p => p.EndsWith('*')
        ? package.StartsWith(p.TrimEnd('*'), StringComparison.OrdinalIgnoreCase)
        : package.Equals(p, StringComparison.OrdinalIgnoreCase));

    static List<BloatApp> Catalog() =>
    [
        new() { Name = "Candy Crush and other King games", Packages = ["king.com.*"] },
        new() { Name = "Microsoft News", Packages = ["Microsoft.BingNews"] },
        new() { Name = "Weather", Packages = ["Microsoft.BingWeather"] },
        new() { Name = "Bing Search", Packages = ["Microsoft.BingSearch"] },
        new() { Name = "Microsoft Solitaire Collection", Packages = ["Microsoft.MicrosoftSolitaireCollection"] },
        new() { Name = "Clipchamp video editor", Packages = ["Clipchamp.Clipchamp"] },
        new() { Name = "Get Help", Packages = ["Microsoft.GetHelp"] },
        new() { Name = "Tips", Packages = ["Microsoft.Getstarted"] },
        new() { Name = "Microsoft 365 (Office) app", Packages = ["Microsoft.MicrosoftOfficeHub"], Note = "Just a launcher; your installed Office apps keep working." },
        new() { Name = "Feedback Hub", Packages = ["Microsoft.WindowsFeedbackHub"] },
        new() { Name = "Maps", Packages = ["Microsoft.WindowsMaps"] },
        new() { Name = "People", Packages = ["Microsoft.People"] },
        new() { Name = "Power Automate", Packages = ["Microsoft.PowerAutomateDesktop"] },
        new() { Name = "Microsoft To Do", Packages = ["Microsoft.Todos"] },
        new() { Name = "Microsoft Journal", Packages = ["Microsoft.MicrosoftJournal"] },
        new() { Name = "Movies & TV", Packages = ["Microsoft.ZuneVideo"] },
        new() { Name = "Teams (personal)", Packages = ["MicrosoftTeams", "MSTeams"], Note = "Keep it if you use Teams for school or work." },
        new() { Name = "Copilot", Packages = ["Microsoft.Copilot", "Microsoft.Windows.Ai.Copilot.Provider"] },
        new() { Name = "Cortana", Packages = ["Microsoft.549981C3F5F10"] },
        new() { Name = "Phone Link", Packages = ["Microsoft.YourPhone"], Note = "Needed to see phone notifications and texts on your PC." },
        new() { Name = "Quick Assist", Packages = ["MicrosoftCorporationII.QuickAssist"], Note = "Lets someone remote-help you. Keep it if family or support uses it." },
        new() { Name = "Dev Home", Packages = ["Microsoft.Windows.DevHome"] },
        new() { Name = "Outlook (new)", Packages = ["Microsoft.OutlookForWindows"], Note = "Keep it if you read email in it." },
        new() { Name = "Mail and Calendar (old)", Packages = ["microsoft.windowscommunicationsapps"], Note = "Keep it if you read email in it." },
        new() { Name = "Microsoft Family Safety", Packages = ["MicrosoftCorporationII.MicrosoftFamily"], Note = "Keep it if parental controls are set up on this PC." },
        new() { Name = "Sound Recorder", Packages = ["Microsoft.WindowsSoundRecorder"] },
        new() { Name = "Clock (alarms and timers)", Packages = ["Microsoft.WindowsAlarms"] },
        new() { Name = "Sticky Notes", Packages = ["Microsoft.MicrosoftStickyNotes"], Note = "Your notes sync to your Microsoft account if you use them." },
        new() { Name = "Media Player", Packages = ["Microsoft.ZuneMusic"], Note = "Windows' default music and video player." },
        new() { Name = "TikTok", Packages = ["BytedancePte.Ltd.TikTok"] },
        new() { Name = "Netflix", Packages = ["4DF9E0F8.Netflix"] },
        new() { Name = "Disney+", Packages = ["Disney.37853FC22B2CE"] },
        new() { Name = "Prime Video", Packages = ["AmazonVideo.PrimeVideo"] },
        new() { Name = "Facebook, Instagram and Messenger", Packages = ["Facebook.*"] },
        new() { Name = "LinkedIn", Packages = ["7EE7776C.LinkedInforWindows"] },
        new() { Name = "Spotify (Microsoft Store version)", Packages = ["SpotifyAB.SpotifyMusic"], Note = "Keep it if this is the Spotify you use." },
        new() { Name = "Xbox app", Packages = ["Microsoft.GamingApp"], Note = "Needed for PC Game Pass and some Microsoft Store games." },
        new() { Name = "Xbox Game Bar", Packages = ["Microsoft.XboxGamingOverlay"], Note = "Needed for Win+G, game clips and some games' overlays." },
    ];

    /// <summary>The catalog apps that are installed for this user.</summary>
    public static List<BloatApp> FindInstalled()
    {
        // Windows itself flags system apps, frameworks and non-removable packages; those are skipped outright.
        var (code, output) = Shell.PowerShell(
            "Get-AppxPackage | Where-Object { -not $_.NonRemovable -and -not $_.IsFramework -and \"$($_.SignatureKind)\" -ne 'System' } | " +
            "ForEach-Object { \"$($_.Name)|$($_.PackageFullName)\" }");
        if (code != 0) throw new InvalidOperationException("Couldn't list installed apps.\n" + output);

        var installed = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(l => l.Split('|'))
            .Where(p => p.Length == 2 && !IsProtected(p[0]))
            .ToList();

        var apps = Catalog();
        foreach (var app in apps)
            app.InstalledPackages.AddRange(installed.Where(p => app.Matches(p[0])).Select(p => p[1]));
        return apps.Where(a => a.InstalledPackages.Count > 0).ToList();
    }

    /// <summary>Removes the apps for the current user. Returns the names of any that couldn't be removed.</summary>
    public static List<string> Remove(IEnumerable<BloatApp> apps)
    {
        var failed = new List<string>();
        foreach (var app in apps)
        {
            // Second check at removal time: a package name must still be unprotected (full names start with the package name).
            var packages = app.InstalledPackages.Where(full => !IsProtected(full.Split('_')[0])).ToList();
            if (packages.Count == 0) continue;
            var script = string.Join("\n", packages.Select(p => $"Remove-AppxPackage -Package '{p.Replace("'", "''")}'"));
            if (Shell.PowerShell(script).ExitCode != 0) failed.Add(app.Name);
        }
        return failed;
    }
}
