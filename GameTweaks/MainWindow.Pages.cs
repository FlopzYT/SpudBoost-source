using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using GameTweaks.Core;
using GameTweaks.Tweaks;

namespace GameTweaks;

/// <summary>Cleaner, Debloater, Monitor and Share pages, plus page animations.</summary>
public partial class MainWindow
{
    readonly List<CleanTarget> _cleanTargets = Cleaner.Targets();
    readonly SystemMonitor _monitor = new();
    readonly DispatcherTimer _monitorTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    bool _cleanerScanned, _debloatLoaded;

    Overlay.StatsOverlayController? _statsOverlay;

    void InitPages()
    {
        CleanerList.ItemsSource = _cleanTargets;
        MonitorPage.DataContext = _monitor;
        _statsOverlay = new Overlay.StatsOverlayController(_settings);
        StatsOverlayCard.DataContext = _statsOverlay;
        OverlayCornerCombo.ItemsSource = Overlay.StatsOverlayController.Corners;
        OverlayRegionCombo.ItemsSource = PingTester.RegionNames.ToList();
        Application.Current.Exit += (_, _) => _statsOverlay.Shutdown();
        KeepFortniteBox.IsChecked = _settings.KeptFortniteSettings != null;
        FlatnessCombo.ItemsSource = NvidiaProfileTweak.FlatnessLevels;
        FlatnessCombo.SelectedIndex = Math.Clamp(_settings.PotatoFlatness, 1, 3) - 1;
        StartBackgroundChores();
        if (!SystemMonitor.HasNvidiaSmi)
            MonitorSubtitle.Text += " GPU readings need an NVIDIA graphics card.";
        _monitorTimer.Tick += async (_, _) => { if (IsVisible) await _monitor.UpdateAsync(); };

        RefreshProfiles();

        // Fade the whole window in when it first appears.
        Loaded += (_, _) =>
        {
            if (Content is UIElement root)
            {
                root.Opacity = 0;
                root.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(260)));
            }
        };
    }

    // ---- Background chores: keep Fortnite settings, weekly clean, driver reminder ----

    readonly DispatcherTimer _choreTimer = new() { Interval = TimeSpan.FromMinutes(1) };
    Fortnite.FortniteGuard? _fortniteGuard;
    int _choreTicks;

    void StartBackgroundChores()
    {
        _fortniteGuard = new Fortnite.FortniteGuard(_settings);
        AutoCleanBox.IsChecked = _settings.AutoCleanWeekly;
        _choreTimer.Tick += async (_, _) => await RunChoresAsync();
        _choreTimer.Start();
    }

    async Task RunChoresAsync()
    {
        _choreTicks++;

        // Every minute: put Fortnite potato settings back if the game reset them.
        try
        {
            if (await Task.Run(_fortniteGuard!.CheckAndRestore))
                _tray.ShowHint("Fortnite had reset your potato settings. SpudBoost put them back (backup saved).");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { }

        // Every hour: weekly auto-clean and the monthly driver reminder.
        if (_choreTicks % 60 != 2) return;

        if (_settings.AutoCleanWeekly && DateTime.Now - _settings.LastAutoClean > TimeSpan.FromDays(7))
        {
            long freed = 0;
            foreach (var t in Cleaner.Targets().Where(t => t.Selected)) // the safe defaults: temp, logs, crash dumps
            {
                try { freed += await Task.Run(() => Cleaner.Clean(t)); }
                catch (InvalidOperationException) { } // its game is running; try next week
            }
            _settings.LastAutoClean = DateTime.Now;
            _settings.Save();
            if (freed > 1L << 20) _tray.ShowHint($"Weekly clean freed {Cleaner.FormatSize(freed)}.");
        }

        if (DateTime.Now - _settings.LastDriverReminder > TimeSpan.FromDays(30))
        {
            var days = await Task.Run(GpuDriverAgeDays);
            if (days > 90)
            {
                _tray.ShowHint($"Your graphics driver is {days / 30} months old. New drivers often add FPS and fix stutter. Update it from NVIDIA, AMD or Intel.");
                _settings.LastDriverReminder = DateTime.Now;
                _settings.Save();
            }
        }
    }

    static int GpuDriverAgeDays()
    {
        var (code, output) = Shell.PowerShell(
            "$g = Get-CimInstance Win32_VideoController | Where-Object { $_.CurrentRefreshRate -gt 0 } | Sort-Object AdapterRAM -Descending | Select-Object -First 1; " +
            "if ($g.DriverDate) { [int]((Get-Date) - $g.DriverDate).TotalDays }");
        return code == 0 && int.TryParse(output.Trim(), out var days) ? days : 0;
    }

    void AutoCleanBox_Click(object sender, RoutedEventArgs e)
    {
        _settings.AutoCleanWeekly = AutoCleanBox.IsChecked == true;
        _settings.Save();
        SetStatus(_settings.AutoCleanWeekly ? "Temp files, logs and crash dumps will be cleaned automatically once a week." : "Weekly cleaning turned off.");
    }

    // ---- NVIDIA potato textures strength ----

    /// <summary>Saves the new level; if potato textures are already on, re-applies them at the new strength.</summary>
    async void Flatness_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var level = FlatnessCombo.SelectedIndex + 1;
        if (level < 1 || level == _settings.PotatoFlatness) return;
        _settings.PotatoFlatness = level;
        _settings.Save();

        if (_fortniteTweaks.FirstOrDefault(t => t.Id == "nvidia-potato") is not { HasBackup: true } potato) return;
        if (!potato.CanToggle)
        {
            SetStatus("Flatness saved. Restart as admin and switch NVIDIA potato textures off and on to use it.");
            return;
        }
        SetStatus($"Changing NVIDIA potato textures to {NvidiaProfileTweak.FlatnessLevels[level - 1]}...");
        try
        {
            await Task.Run(potato.Apply);
            await Task.Run(potato.Refresh);
            SetStatus($"NVIDIA potato textures set to {NvidiaProfileTweak.FlatnessLevels[level - 1]}. Restart Fortnite to see it.");
        }
        catch (Exception ex) { ShowError(ex); }
    }

    // ---- Tweaks page: sections and search ----

    void InitTweakList()
    {
        var view = new System.Windows.Data.ListCollectionView(_systemTweaks);
        view.GroupDescriptions.Add(new System.Windows.Data.PropertyGroupDescription(nameof(Tweak.Category)));
        SystemList.ItemsSource = view;
    }

    void TweakSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        var query = TweakSearchBox.Text.Trim();
        TweakSearchHint.Visibility = query.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (SystemList.ItemsSource is not System.Windows.Data.ListCollectionView view) return;
        view.Filter = query.Length == 0 ? null : o => o is Tweak t &&
            (t.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
             t.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
             t.Category.Contains(query, StringComparison.OrdinalIgnoreCase));
        NoTweaksFound.Visibility = view.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
    }

    void HealthCard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var open = HealthList.Visibility != Visibility.Visible;
        HealthList.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        HealthToggleText.Text = open ? "Hide ▴" : "Show ▾";
        if (open) AnimatePageIn(HealthList);
    }

    /// <summary>Pages fade in and slide up slightly when you switch to them.</summary>
    static void AnimatePageIn(FrameworkElement page)
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var shift = new TranslateTransform(0, 14);
        page.RenderTransform = shift;
        page.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
        shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(14, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
    }

    async void OnPageShown(string page)
    {
        if (page == "MonitorPage")
        {
            await _monitor.UpdateAsync();
            _monitorTimer.Start();
        }
        else _monitorTimer.Stop();

        if (page == "CleanerPage" && !_cleanerScanned) await ScanCleanerAsync();
        if (page == "DebloatPage" && !_debloatLoaded) await LoadDebloatAsync();
    }

    // ---- Cleaner ----

    async Task ScanCleanerAsync()
    {
        _cleanerScanned = true;
        CleanerTotal.Text = "Scanning...";
        foreach (var t in _cleanTargets) t.Bytes = -1;
        foreach (var t in _cleanTargets) t.Bytes = await Task.Run(() => Cleaner.Measure(t));
        CleanerTotal.Text = $"{Cleaner.FormatSize(_cleanTargets.Sum(t => t.Bytes))} found in total.";
    }

    async void ScanCleaner_Click(object sender, RoutedEventArgs e) => await ScanCleanerAsync();

    async void Clean_Click(object sender, RoutedEventArgs e)
    {
        var selected = _cleanTargets.Where(t => t.Selected).ToList();
        if (selected.Count == 0)
        {
            CleanerTotal.Text = "Tick at least one item to clean.";
            return;
        }
        if (MessageBox.Show(this, "Permanently delete these?\n\n• " + string.Join("\n• ", selected.Select(t => $"{t.Name} ({t.SizeText})")),
                "SpudBoost", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;

        CleanButton.IsEnabled = false;
        long freed = 0;
        var problems = new List<string>();
        foreach (var t in selected)
        {
            try { freed += await Task.Run(() => Cleaner.Clean(t)); }
            catch (InvalidOperationException ex) { problems.Add(ex.Message); }
        }
        CleanButton.IsEnabled = true;
        await ScanCleanerAsync();
        CleanerTotal.Text = $"Freed {Cleaner.FormatSize(freed)}." + (problems.Count > 0 ? " " + string.Join(" ", problems) : "");
        SetStatus(CleanerTotal.Text);
    }

    // ---- Debloater ----

    async Task LoadDebloatAsync()
    {
        _debloatLoaded = true;
        DebloatStatus.Text = "Looking for removable apps...";
        try
        {
            var apps = await Task.Run(Debloater.FindInstalled);
            DebloatList.ItemsSource = apps;
            DebloatStatus.Text = apps.Count == 0 ? "No removable extras found. This PC is already clean." : $"{apps.Count} removable apps found. Tick the ones you don't use.";
        }
        catch (InvalidOperationException ex) { DebloatStatus.Text = ex.Message; }
    }

    async void RefreshDebloat_Click(object sender, RoutedEventArgs e) => await LoadDebloatAsync();

    async void Debloat_Click(object sender, RoutedEventArgs e)
    {
        var selected = (DebloatList.ItemsSource as List<BloatApp>)?.Where(a => a.Selected).ToList() ?? [];
        if (selected.Count == 0)
        {
            DebloatStatus.Text = "Tick the apps you want to remove first.";
            return;
        }
        var warnings = selected.Where(a => a.Note != null).Select(a => $"\n   ⚠ {a.Name}: {a.Note}");
        if (MessageBox.Show(this,
                "Remove these apps?\n\n• " + string.Join("\n• ", selected.Select(a => a.Name)) + string.Concat(warnings) +
                "\n\nYou can reinstall any of them from the Microsoft Store.",
                "SpudBoost", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;

        DebloatButton.IsEnabled = false;
        await EnsureRestorePointAsync();
        DebloatStatus.Text = $"Removing {selected.Count} app(s)...";
        var failed = await Task.Run(() => Debloater.Remove(selected));
        DebloatButton.IsEnabled = true;
        await LoadDebloatAsync();
        DebloatStatus.Text = failed.Count == 0
            ? $"Removed {selected.Count} app(s)."
            : $"Removed {selected.Count - failed.Count}. Couldn't remove: {string.Join(", ", failed)} (Windows may be using them; try after a restart).";
        SetStatus(DebloatStatus.Text);
    }

    // ---- Crosshair share codes ----

    void CopyCrosshairCode_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(_crosshair.ExportCode());
        SetStatus("Crosshair code copied. Send it to a friend; they paste it on their Crosshair page.");
    }

    void PasteCrosshairCode_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _crosshair.ImportCode(Clipboard.GetText());
            CrosshairEditCombo.ItemsSource = _crosshair.EditTargets;
            CrosshairEditCombo.SelectedIndex = 0;
            SetStatus("Friend's crosshairs loaded.");
        }
        catch (FormatException ex) { ShowError(ex); }
    }

}
