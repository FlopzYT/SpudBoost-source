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

    // ---- Background chores: weekly clean, driver reminder ----

    readonly DispatcherTimer _choreTimer = new() { Interval = TimeSpan.FromMinutes(1) };
    int _choreTicks;

    void StartBackgroundChores()
    {
        AutoCleanBox.IsChecked = _settings.AutoCleanWeekly;
        _choreTimer.Tick += async (_, _) => await RunChoresAsync();
        _choreTimer.Start();
    }

    async Task RunChoresAsync()
    {
        _choreTicks++;

        // Once a day (only after the current Terms were accepted): anonymous check-in.
        if (_settings.TermsAcceptedVersion >= TermsVersion) await CheckIn.SendIfDueAsync(_settings);

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

    // ---- Sideways tweak rows ----

    /// <summary>‹ › buttons: slide the row about two tiles.</summary>
    void RowScroll_Click(object sender, RoutedEventArgs e)
    {
        var button = (FrameworkElement)sender;
        DependencyObject? node = button;
        while (node != null && node is not GroupItem) node = VisualTreeHelper.GetParent(node);
        if (node is not GroupItem group || group.Template.FindName("RowScroll", group) is not ScrollViewer row) return;
        SmoothScroll(row, row.HorizontalOffset + int.Parse((string)button.Tag) * 524);
    }

    /// <summary>Shift + wheel scrolls the row sideways; a plain wheel scrolls the page as usual.</summary>
    void RowScroll_MouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        var row = (ScrollViewer)sender;
        e.Handled = true;
        if (System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Shift))
        {
            SmoothScroll(row, row.HorizontalOffset - e.Delta * 1.5);
            return;
        }
        // A nested ScrollViewer swallows the wheel, so hand it to the page.
        if (VisualTreeHelper.GetParent(row) is UIElement parent)
            parent.RaiseEvent(new System.Windows.Input.MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = MouseWheelEvent, Source = row });
    }

    static void SmoothScroll(ScrollViewer row, double target)
    {
        target = Math.Clamp(target, 0, row.ScrollableWidth);
        var from = row.HorizontalOffset;
        var start = DateTime.Now;
        EventHandler? tick = null;
        tick = (_, _) =>
        {
            var t = Math.Min(1, (DateTime.Now - start).TotalMilliseconds / 320);
            row.ScrollToHorizontalOffset(from + (target - from) * (1 - Math.Pow(1 - t, 3)));
            if (t >= 1) CompositionTarget.Rendering -= tick;
        };
        CompositionTarget.Rendering += tick;
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

        if (page == "CleanerPage") await UpdateRamStatusAsync();
        if (page == "CleanerPage" && !_cleanerScanned) await ScanCleanerAsync();
        if (page == "DebloatPage" && !_debloatLoaded) await LoadDebloatAsync();
        if (page == "LibraryPage" && !_libraryLoaded) await LoadLibraryAsync();
        if (page == "MonitorPage") RefreshSessions();
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
        _settings.LastAutoClean = DateTime.Now; // also counts as the last Boost-Up clean
        _settings.JunkCleanedBytes += freed;
        _settings.Save();
        await ScanCleanerAsync();
        CleanerTotal.Text = $"Freed {Cleaner.FormatSize(freed)}." + (problems.Count > 0 ? " " + string.Join(" ", problems) : "");
        SetStatus(CleanerTotal.Text);
    }

    // ---- RAM ----

    async Task UpdateRamStatusAsync()
    {
        AutoRamBox.IsChecked = _settings.AutoFreeRam;
        AutoRamBox.IsEnabled = FreeRamButton.IsEnabled = Shell.IsAdmin;
        if (!Shell.IsAdmin)
        {
            RamStatus.Text = "Needs administrator rights: click Run as admin at the top.";
            return;
        }
        var m = await Task.Run(MemoryCleaner.Query);
        RamStatus.Text = m is { } v
            ? $"{v.FreeMb / 1024.0:0.0} GB free right now · {v.StandbyMb / 1024.0:0.0} GB held in Windows' standby cache"
            : "Couldn't read memory information on this PC.";
    }

    async void FreeRam_Click(object sender, RoutedEventArgs e)
    {
        FreeRamButton.IsEnabled = false;
        var freed = await Task.Run(MemoryCleaner.FreeStandby);
        FreeRamButton.IsEnabled = true;
        SetStatus(freed < 0 ? "Couldn't free RAM (needs administrator rights)." : $"Freed {freed:N0} MB of RAM.");
        _settings.RamFreedMb += Math.Max(0, freed);
        _settings.Save();
        await UpdateRamStatusAsync();
    }

    void AutoRam_Click(object sender, RoutedEventArgs e)
    {
        _settings.AutoFreeRam = AutoRamBox.IsChecked == true;
        _settings.Save();
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
