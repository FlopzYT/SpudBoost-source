using System.Windows;
using System.Windows.Controls;

namespace GameTweaks;

/// <summary>Sidebar sections, each with one or more pages shown as tabs along the top.</summary>
public partial class MainWindow
{
    sealed record NavSection(string Id, string Title, (string Label, string Page)[] Pages);

    static readonly NavSection[] Sections =
    [
        new("home", "Home", [("Home", "HomePage")]),
        new("optimize", "Optimizations", [("Tweaks", "TweaksPage"), ("Network", "NetworkPage"), ("Startup apps", "StartupPage"), ("Debloater", "DebloatPage")]),
        new("boost", "Boost-Up", [("Game Boost", "BoostPage"), ("Cleaner", "CleanerPage"), ("FPS Benchmark", "BenchmarkPage")]),
        // Game pages (Fortnite, Roblox, Minecraft, CS2) and the crosshair open from the Library and Home instead of tabs.
        new("games", "Games", [("Library", "LibraryPage")]),
        new("monitor", "Hardware Monitor", [("Monitor", "MonitorPage")]),
        new("backups", "Profiles & Backups", [("My profiles", "ProfilesPage"), ("Backup & Undo", "BackupPage")]),
        new("settings", "Settings", [("Settings", "SettingsPage")]),
    ];

    /// <summary>The tab last used in each section, so coming back opens the same page.</summary>
    readonly Dictionary<string, string> _lastPage = [];
    string? _currentPage;
    bool _buildingTabs;

    void Section_Checked(object sender, RoutedEventArgs e)
    {
        if (Pages == null || _selectingSection) return;
        var section = Sections.First(s => s.Id == (string)((RadioButton)sender).Tag);
        ShowSection(section, _lastPage.GetValueOrDefault(section.Id, section.Pages[0].Page));
    }

    void ShowSection(NavSection section, string page)
    {
        PageHeading.Text = section.Title;
        _buildingTabs = true;
        SubNav.Children.Clear();
        if (section.Pages.Length > 1)
            foreach (var (label, target) in section.Pages)
            {
                var tab = new RadioButton { Content = label, Tag = target, Style = (Style)FindResource("SubTab"), GroupName = "SubNav", IsChecked = target == page };
                tab.Checked += (_, _) => { if (!_buildingTabs) ShowPage(section, target); };
                SubNav.Children.Add(tab);
            }
        _buildingTabs = false;
        ShowPage(section, page);
    }

    /// <summary>Shows a page (by its element name, or "Game:id" for the Roblox/Minecraft/CS2 page).</summary>
    void ShowPage(NavSection section, string target)
    {
        // Only tab pages are remembered: pages opened from elsewhere (a game page, the crosshair) aren't returned to
        // when the sidebar section is clicked again, so Games always goes back to the Library.
        if (section.Pages.Any(p => p.Page == target)) _lastPage[section.Id] = target;
        _currentPage = target;
        foreach (FrameworkElement page in Pages.Children)
        {
            var show = page.Name == (target.StartsWith("Game:") ? "GamePage" : target);
            if (show && page.Visibility == Visibility.Visible) continue;
            page.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (show) AnimatePageIn(page);
        }
        if (target == "FortnitePage") LoadFortnite();
        if (target == "CrosshairPage") UpdateCrosshairWarning();
        if (target.StartsWith("Game:")) ShowGame(target[5..]);
        OnPageShown(target);
    }

    /// <summary>Opens a page from anywhere: selects its sidebar section and tab.</summary>
    void NavigateTo(string page)
    {
        var section = Sections.FirstOrDefault(s => s.Pages.Any(p => p.Page == page)) ?? Sections.First(s => s.Id == "games");
        var nav = NavList.Children.OfType<RadioButton>().First(r => (string)r.Tag == section.Id);
        if (nav.IsChecked != true)
        {
            _selectingSection = true; // highlight the section without opening its default page first
            nav.IsChecked = true;
            _selectingSection = false;
        }
        ShowSection(section, page);
    }

    bool _selectingSection;

    /// <summary>The SpudBoost Discord, used for support. Should be an invite set to never expire.</summary>
    public const string DiscordInvite = "https://discord.gg/PZdabSCMH";

    void GetSupport_Click(object sender, RoutedEventArgs e) =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(DiscordInvite) { UseShellExecute = true });
}
