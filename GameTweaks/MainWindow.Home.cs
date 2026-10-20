using System.Windows;
using System.Windows.Input;
using GameTweaks.Core;
using GameTweaks.Tweaks;

namespace GameTweaks;

/// <summary>Home dashboard and the custom dark window frame.</summary>
public partial class MainWindow
{
    void InitHome()
    {
        HomeBoostTile.DataContext = _booster;
        HomeCrosshairTile.DataContext = _crosshair;
        HomeOverlayTile.DataContext = _statsOverlay;
        HomeGreeting.Text = (DateTime.Now.Hour switch { < 5 => "LATE NIGHT GAMING?", < 12 => "GOOD MORNING", < 18 => "GOOD AFTERNOON", _ => "GOOD EVENING" });
        Loaded += async (_, _) =>
        {
            // The Home page shows a few games, so scan the library in the background straight away.
            if (!_libraryLoaded) await LoadLibraryAsync();
            UpdateHome();
        };
        if (_sessions != null) _sessions.SessionEnded += _ => UpdateHome();
    }

    /// <summary>Refreshes the headline, stats and game row (called after tweak changes and library scans).</summary>
    void UpdateHome()
    {
        if (HomeTweaksOn == null) return;
        var counted = _systemTweaks.Concat(_networkTweaks).Where(t => t.IsChecked).ToList();
        HomeTweaksOn.Text = counted.Count == 0 ? "–" : $"{counted.Count(t => t.IsOn)} / {counted.Count}";
        HomePlayWeek.Text = _sessions != null ? SessionTracker.FormatMinutes(_sessions.MinutesSince(DateTime.Now.AddDays(-7))) : "0 min";
        HomeGameCount.Text = _libraryLoaded ? _library.Count.ToString() : "–";

        // Most-played games first, then the rest alphabetically.
        var playtime = _sessions?.Sessions.GroupBy(s => s.Game, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Sum(s => s.Minutes), StringComparer.OrdinalIgnoreCase) ?? [];
        double Played(LibraryGame g) => playtime.Where(p => g.Name.Replace(" ", "").Contains(p.Key, StringComparison.OrdinalIgnoreCase) ||
                                                            (g.InstallFolder?.Contains(p.Key, StringComparison.OrdinalIgnoreCase) ?? false)).Sum(p => p.Value);
        HomeGames.ItemsSource = _library.OrderByDescending(Played).ThenBy(g => g.Name).Take(5).ToList();

        if (_score is { } s)
        {
            var boosted = s.Score >= 85;
            HomeHeadline.Text = boosted ? $"Your PC is {s.Rank}. Nice." : $"Your PC is a {s.Rank}";
            HomeSubline.Text = boosted
                ? "You're boosted. Jump into a game below, or fine-tune things on the other pages."
                : "One click turns on the recommended tweaks and Game Boost. Everything can be undone from Backup & Undo.";
            BigBoostText.Text = boosted ? "BOOSTED" : "BOOST";
            BigBoostSub.Text = boosted ? $"score {s.Score}" : "one click";
        }
    }

    async void BigBoost_Click(object sender, RoutedEventArgs e)
    {
        var preset = Presets.All.First(p => p.Name == "Competitive");
        await EnsureTweaksCheckedAsync();
        var known = AllTweaks.ToDictionary(t => t.Id);
        var off = preset.TweakIds.Where(known.ContainsKey).Select(id => known[id]).Count(t => !t.IsOn && t.ConfirmMessage == null);
        if (off == 0 && _booster.Enabled)
        {
            SetStatus("Already boosted: every recommended tweak and Game Boost are on.");
            return;
        }
        if (MessageBox.Show(this,
                $"Boost this PC?\n\nThis turns on Game Boost and the Competitive preset ({off} change{(off == 1 ? "" : "s")}): power, CPU/GPU priority, " +
                "low-latency network and input tweaks, plus competitive Fortnite settings.\n\nEverything can be undone from Backup & Undo.",
                "SpudBoost", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        BigBoostButton.IsEnabled = false;
        BigBoostSub.Text = "boosting...";
        try
        {
            _booster.Enabled = true;
            if (off > 0) await ApplyPresetAsync(preset);
        }
        finally
        {
            BigBoostButton.IsEnabled = true;
            UpdateHome();
        }
    }

    void HomeTweaks_Click(object sender, MouseButtonEventArgs e) => NavigateTo("TweaksPage");
    void HomeMonitor_Click(object sender, MouseButtonEventArgs e) => NavigateTo("MonitorPage");
    void HomeLibrary_Click(object sender, RoutedEventArgs e) => NavigateTo("LibraryPage");

    // ---- Custom window frame ----

    void MinimizeWindow_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    void MaximizeWindow_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    void CloseWindow_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>A maximised borderless window hangs over the screen edge by the resize border, so pad it back in.</summary>
    void Window_StateChanged(object? sender, EventArgs e)
    {
        var maximized = WindowState == WindowState.Maximized;
        var b = SystemParameters.WindowResizeBorderThickness;
        RootGrid.Margin = maximized ? new Thickness(b.Left + 4, b.Top + 4, b.Right + 4, b.Bottom + 4) : new Thickness(0);
        MaximizeButton.Content = maximized ? "" : "";
        MaximizeButton.ToolTip = maximized ? "Restore" : "Maximise";
    }
}
