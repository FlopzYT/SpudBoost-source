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
    SharedProfile? _previewProfile;
    string? _previewCode;

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

        ProfileNameBox.Text = _settings.ProfileName;
        ProfileNameBox.LostFocus += (_, _) =>
        {
            _settings.ProfileName = ProfileNameBox.Text.Trim();
            _settings.Save();
        };
        RefreshFriends();
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

    // ---- Share profiles ----

    SharedProfile BuildMyProfile() => new()
    {
        Name = _settings.ProfileName,
        Tweaks = ShareTweaksBox.IsChecked == true ? AllTweaks.Where(t => t.IsOn).Select(t => t.Id).ToList() : [],
        Fortnite = ShareFortniteBox.IsChecked == true && Fortnite.FortniteSettings.ConfigExists ? _fortnite.Snapshot() : null,
        Crosshair = ShareCrosshairBox.IsChecked == true ? _crosshair.ExportCode() : null,
    };

    void CopyProfile_Click(object sender, RoutedEventArgs e)
    {
        _settings.ProfileName = ProfileNameBox.Text.Trim();
        _settings.Save();
        var profile = BuildMyProfile();
        Clipboard.SetText(ProfileShare.Encode(profile));
        ProfileCopiedText.Text = $"Copied! ({profile.Summary}) Paste it to your friend.";
    }

    void PasteFriendCode_Click(object sender, RoutedEventArgs e)
    {
        FriendCodeBox.Text = Clipboard.GetText().Trim();
        PreviewFriendCode_Click(sender, e);
    }

    /// <summary>Shows exactly what a code would change before anything is applied.</summary>
    void PreviewFriendCode_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var profile = ProfileShare.Decode(FriendCodeBox.Text);
            _previewProfile = profile;
            _previewCode = FriendCodeBox.Text.Trim();

            var known = AllTweaks.ToDictionary(t => t.Id);
            var toTurnOn = profile.Tweaks.Where(known.ContainsKey).Select(id => known[id]).Where(t => !t.IsOn).ToList();
            var applicable = toTurnOn.Where(t => t.CanToggle && t.ConfirmMessage == null).ToList();
            var skipped = toTurnOn.Except(applicable).ToList();

            var who = string.IsNullOrWhiteSpace(profile.Name) ? "Your friend" : profile.Name;
            PreviewTitle.Text = $"{who}'s profile: {profile.Summary}";
            SaveFriendNameBox.Text = profile.Name;

            ApplyTweaksBox.Content = applicable.Count == 0 ? "No new tweaks to turn on" : $"Turn on {applicable.Count} tweak(s) you don't have yet:";
            ApplyTweaksBox.IsEnabled = applicable.Count > 0;
            ApplyTweaksBox.IsChecked = applicable.Count > 0;
            PreviewTweaks.Text = string.Join(", ", applicable.Select(t => t.Name)) +
                (skipped.Count > 0 ? $"\nSkipped (need admin or your own confirmation on the Tweaks page): {string.Join(", ", skipped.Select(t => t.Name))}" : "");
            ApplyFortniteBox.Visibility = profile.Fortnite != null && Fortnite.FortniteSettings.ConfigExists ? Visibility.Visible : Visibility.Collapsed;
            ApplyCrosshairBox.Visibility = profile.Crosshair != null ? Visibility.Visible : Visibility.Collapsed;
            PreviewPanel.Visibility = Visibility.Visible;
            AnimatePageIn(PreviewPanel);
        }
        catch (FormatException ex)
        {
            PreviewPanel.Visibility = Visibility.Collapsed;
            ShowError(ex);
        }
    }

    async void ApplyFriendCode_Click(object sender, RoutedEventArgs e)
    {
        if (_previewProfile is not { } profile) return;
        await EnsureTweaksCheckedAsync();
        var done = new List<string>();
        var problems = new List<string>();

        if (ApplyTweaksBox.IsChecked == true && ApplyTweaksBox.IsEnabled)
        {
            var known = AllTweaks.ToDictionary(t => t.Id);
            var tweaks = profile.Tweaks.Where(known.ContainsKey).Select(id => known[id])
                .Where(t => !t.IsOn && t.CanToggle && t.ConfirmMessage == null).ToList();
            if (tweaks.Count > 0) await EnsureRestorePointAsync();
            foreach (var t in tweaks)
            {
                try { await Task.Run(t.Apply); }
                catch (Exception ex) { problems.Add($"{t.Name}: {ex.Message.Split('\n')[0]}"); }
            }
            await RefreshAllAsync();
            done.Add($"{tweaks.Count - problems.Count} tweak(s)");
        }

        if (ApplyFortniteBox.IsChecked == true && ApplyFortniteBox.Visibility == Visibility.Visible && profile.Fortnite != null)
        {
            try
            {
                _fortnite.Load();
                _fortnite.ApplySnapshot(profile.Fortnite);
                _fortnite.Save();
                done.Add("Fortnite settings");
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.IO.IOException or UnauthorizedAccessException)
            {
                problems.Add("Fortnite: " + ex.Message);
            }
        }

        if (ApplyCrosshairBox.IsChecked == true && ApplyCrosshairBox.Visibility == Visibility.Visible && profile.Crosshair != null)
        {
            try
            {
                _crosshair.ImportCode(profile.Crosshair);
                CrosshairEditCombo.ItemsSource = _crosshair.EditTargets;
                CrosshairEditCombo.SelectedIndex = 0;
                done.Add("crosshairs");
            }
            catch (FormatException ex) { problems.Add("Crosshair: " + ex.Message); }
        }

        SetStatus(done.Count > 0 ? $"Applied {string.Join(", ", done)}." : "Nothing was applied.");
        if (problems.Count > 0) ShowError(new Exception(string.Join("\n\n", problems)));
    }

    void SaveFriend_Click(object sender, RoutedEventArgs e)
    {
        if (_previewCode == null) return;
        var name = SaveFriendNameBox.Text.Trim();
        if (name.Length == 0)
        {
            SetStatus("Type your friend's name first.");
            return;
        }
        var existing = _settings.Friends.FirstOrDefault(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            existing.Code = _previewCode;
            existing.Updated = DateTime.Now;
        }
        else _settings.Friends.Add(new Friend { Name = name, Code = _previewCode });
        _settings.Save();
        RefreshFriends();
        SetStatus($"{name} {(existing != null ? "updated" : "saved to your friends")}.");
    }

    void RefreshFriends()
    {
        FriendsList.ItemsSource = null;
        FriendsList.ItemsSource = _settings.Friends.OrderBy(f => f.Name).ToList();
        NoFriendsText.Visibility = _settings.Friends.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    static Friend FriendOf(object sender) => (Friend)((FrameworkElement)sender).DataContext;

    void LoadFriend_Click(object sender, RoutedEventArgs e)
    {
        FriendCodeBox.Text = FriendOf(sender).Code;
        PreviewFriendCode_Click(sender, e);
    }

    void CopyFriend_Click(object sender, RoutedEventArgs e)
    {
        var friend = FriendOf(sender);
        Clipboard.SetText(friend.Code);
        SetStatus($"Copied {friend.Name}'s profile code.");
    }

    void RemoveFriend_Click(object sender, RoutedEventArgs e)
    {
        var friend = FriendOf(sender);
        if (MessageBox.Show(this, $"Remove {friend.Name} from your friends?", "SpudBoost", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        _settings.Friends.Remove(friend);
        _settings.Save();
        RefreshFriends();
    }
}
