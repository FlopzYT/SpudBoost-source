using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using GameTweaks.Boost;
using GameTweaks.Core;
using GameTweaks.Crosshair;
using GameTweaks.Fortnite;
using GameTweaks.Tweaks;

namespace GameTweaks;

public partial class MainWindow : Window
{
    // Page logic for Cleaner, Monitor, Share and Debloater lives in MainWindow.Pages.cs.
    static readonly (string Label, int X, int Y)[] Resolutions =
    [
        ("Common resolutions...", 0, 0),
        ("1920 × 1080 (16:9)", 1920, 1080),
        ("1680 × 1050 (16:10 stretched)", 1680, 1050),
        ("1600 × 900 (16:9)", 1600, 900),
        ("1440 × 1080 (4:3 stretched)", 1440, 1080),
        ("1280 × 960 (4:3 stretched)", 1280, 960),
        ("1280 × 720 (16:9)", 1280, 720),
        ("1024 × 768 (4:3 stretched)", 1024, 768),
        ("800 × 600 (4:3 potato)", 800, 600),
    ];

    readonly AppSettings _settings = AppSettings.Load();
    readonly List<Tweak> _systemTweaks;
    readonly List<Tweak> _networkTweaks;
    readonly List<Tweak> _fortniteTweaks;

    IEnumerable<Tweak> AllTweaks => _systemTweaks.Concat(_networkTweaks).Concat(_fortniteTweaks);

    /// <summary>AMD's Windows driver ships atiadlxx.dll (the AMD Display Library).</summary>
    static bool HasAmdGpu() => File.Exists(Path.Combine(Environment.SystemDirectory, "atiadlxx.dll"));
    readonly DnsTweak _dns;
    readonly GameBooster _booster;
    readonly FortniteSettings _fortnite = new();
    readonly TrayIcon _tray = new();
    readonly CrosshairController _crosshair;
    bool _exiting;

    public MainWindow()
    {
        InitializeComponent();

        _tray.OpenRequested += ShowFromTray;
        _tray.ExitRequested += ExitApp;
        Application.Current.Exit += (_, _) => Cleanup();
        StartWithWindows.RefreshPath();
        TrayBox.IsChecked = _settings.MinimizeToTray;
        StartWithWindowsBox.IsChecked = StartWithWindows.Enabled;
        AutoRestorePointBox.IsChecked = _settings.AutoRestorePoint;
        VersionText.Text = $"SpudBoost {UpdateChecker.CurrentVersion}";

        _systemTweaks = TweakCatalog.System(() => _settings.GameProcesses);
        _networkTweaks = TweakCatalog.Network(_settings.DnsProvider);
        _fortniteTweaks = TweakCatalog.Fortnite();
        FortniteTweakList.ItemsSource = _fortniteTweaks;
        FortniteGpuCard.Visibility = _fortniteTweaks.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        AmdPotatoCard.Visibility = HasAmdGpu() ? Visibility.Visible : Visibility.Collapsed;
        _dns = _networkTweaks.OfType<DnsTweak>().Single();
        SystemList.ItemsSource = _systemTweaks;
        NetworkList.ItemsSource = _networkTweaks;

        _booster = new GameBooster(_settings);
        BoostPage.DataContext = _booster;
        _tray.BoostToggled += on => _booster.Enabled = on;
        _booster.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(GameBooster.Enabled)) UpdateTray(); };
        RaisePriorityBox.IsChecked = _settings.RaiseGamePriority;
        LowerBackgroundBox.IsChecked = _settings.LowerBackgroundApps;
        GamesBox.Text = string.Join(Environment.NewLine, _settings.GameProcesses);
        BackgroundBox.Text = string.Join(Environment.NewLine, _settings.BackgroundProcesses);
        CloseAppsBox.IsChecked = _settings.CloseAppsOnGameStart;
        AppsToCloseBox.Text = string.Join(Environment.NewLine, _settings.AppsToClose);

        DnsCombo.ItemsSource = DnsTweak.Providers.Keys;
        DnsCombo.SelectedItem = _settings.DnsProvider;

        FortnitePage.DataContext = _fortnite;
        WindowModeCombo.ItemsSource = FortniteSettings.WindowModes;
        RenderModeCombo.ItemsSource = FortniteSettings.RenderModes;
        MeshCombo.ItemsSource = FortniteSettings.MeshLevels;
        ResPresetCombo.ItemsSource = Resolutions.Select(r => r.Label);
        ResPresetCombo.SelectedIndex = 0;
        LoadFortnite();

        AdminText.Text = Shell.IsAdmin
            ? "Running as administrator. All tweaks are available."
            : "Some tweaks need administrator rights.";
        AdminButton.Visibility = Shell.IsAdmin ? Visibility.Collapsed : Visibility.Visible;
        BackupPathText.Text = BackupStore.Folder;

        StartupList.ItemsSource = StartupManager.List();
        InitBenchmark();
        _crosshair = new CrosshairController(_settings);
        InitCrosshair();
        InitPages();

        UpdateTray();

        Loaded += async (_, _) =>
        {
            await RefreshAllAsync();
            var health = await Task.Run(HealthCheck.Run);
            HealthList.ItemsSource = health;
            HealthCard.Visibility = health.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        };

        StartUpdateChecks();
    }

    /// <summary>Checks shortly after startup (even when started hidden in the tray), then every 6 hours.</summary>
    void StartUpdateChecks()
    {
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        timer.Tick += async (_, _) =>
        {
            timer.Interval = TimeSpan.FromHours(6);
            await CheckForUpdatesAsync(quiet: true);
        };
        timer.Start();
    }

    void UpdateTray() => _tray.SetStates(_booster.Enabled, _settings.Crosshair.Enabled);

    /// <summary>Closing hides to the tray (so Game Boost keeps running) unless the user chose Exit.</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_exiting && _settings.MinimizeToTray)
        {
            e.Cancel = true;
            Hide();
            if (!_settings.TrayHintShown)
            {
                _tray.ShowHint("Still running here so Game Boost keeps working. Right-click the icon to exit.");
                _settings.TrayHintShown = true;
                _settings.Save();
            }
            return;
        }
        base.OnClosing(e);
    }

    /// <summary>The window is gone for good (Exit, or closed with tray mode off): end the app.</summary>
    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        Application.Current.Shutdown();
    }

    public void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    public void ExitApp()
    {
        _exiting = true;
        // Deferred so the tray menu finishes handling its click before the icon is disposed.
        Dispatcher.BeginInvoke(Close);
    }

    bool _cleanedUp;

    /// <summary>Restores boosted processes and removes the tray icon; runs once on app exit.</summary>
    void Cleanup()
    {
        if (_cleanedUp) return;
        _cleanedUp = true;
        _booster.Shutdown();
        _crosshair.Shutdown();
        _tray.Dispose();
    }

    void SetStatus(string text) => StatusText.Text = text;

    void ShowError(Exception ex)
    {
        SetStatus("Error: " + ex.Message.Split('\n')[0]);
        MessageBox.Show(this, ex.Message, "SpudBoost", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    async Task RefreshAllAsync()
    {
        // Detection runs commands (powercfg, PowerShell), so keep it off the UI thread.
        await Task.Run(() => { foreach (var t in AllTweaks) t.Refresh(); });
        UpdateUndoSummary();
    }

    void UpdateUndoSummary()
    {
        var all = AllTweaks.ToList();
        var count = all.Count(t => t.HasBackup);
        UndoSummary.Text = count == 0
            ? "No tweaks have been applied by SpudBoost yet."
            : $"{count} tweak(s) can be undone and returned to their original values.";
        UpdatePendingCard(all);
    }

    bool _pendingIsRestart;

    /// <summary>Shows the bottom-left prompt while changes are waiting for a restart (or sign-out).</summary>
    void UpdatePendingCard(List<Tweak> all)
    {
        var restart = all.Where(t => t.PendingNote == "Restart required").Select(t => t.Name).ToList();
        var signOut = all.Where(t => t.PendingNote == "Sign out to apply").Select(t => t.Name).ToList();
        if (restart.Count == 0 && signOut.Count == 0)
        {
            PendingCard.Visibility = Visibility.Collapsed;
            return;
        }

        // A restart also signs you out, so it covers both.
        _pendingIsRestart = restart.Count > 0;
        var names = restart.Concat(signOut).ToList();
        PendingTitle.Text = _pendingIsRestart ? "Restart needed" : "Sign out needed";
        PendingText.Text = $"{names.Count} change(s) take effect after you {(_pendingIsRestart ? "restart your PC" : "sign out")}: {string.Join(", ", names)}.";
        PendingButton.Content = _pendingIsRestart ? "Restart now" : "Sign out now";
        PendingCard.Visibility = Visibility.Visible;
    }

    void PendingAction_Click(object sender, RoutedEventArgs e)
    {
        var action = _pendingIsRestart ? "restart your PC" : "sign you out of Windows";
        if (MessageBox.Show(this, $"This will {action} in 10 seconds. Save anything you have open first.\n\nContinue?",
                "SpudBoost", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;

        var (code, output) = _pendingIsRestart
            ? Shell.Run("shutdown", "/r /t 10 /c \"SpudBoost: restarting to apply tweaks\"")
            : Shell.Run("shutdown", "/l");
        if (code != 0) ShowError(new Exception(output));
        else if (_pendingIsRestart) SetStatus("Restarting in 10 seconds. Run \"shutdown /a\" to cancel.");
    }

    // ---- Crosshair ----

    void InitCrosshair()
    {
        CrosshairPage.DataContext = _crosshair;
        CrosshairStyleCombo.ItemsSource = CrosshairRenderer.Styles;
        CrosshairEditCombo.ItemsSource = _crosshair.EditTargets;
        _crosshair.Changed += () => { DrawCrosshairPreview(); UpdateTray(); };
        _tray.CrosshairToggled += on => _crosshair.Enabled = on;
        DrawCrosshairPreview();
    }

    void DrawCrosshairPreview() =>
        CrosshairRenderer.Draw(CrosshairPreview, _crosshair.Edited,
            CrosshairPreview.Width / 2 + _crosshair.OffsetX / 2, CrosshairPreview.Height / 2 + _crosshair.OffsetY / 2);

    /// <summary>Overlays can't draw over exclusive fullscreen, so point out Fortnite settings that would hide the crosshair.</summary>
    void UpdateCrosshairWarning()
    {
        var problems = new List<string>();
        if (FortniteSettings.ConfigExists && _fortnite.WindowMode == 0)
            problems.Add("Fortnite is set to Fullscreen, so the crosshair can't appear over it. Switch Window Mode to \"Windowed Fullscreen\" in Fortnite's display settings (or on the Fortnite Potato page).");
        if (_systemTweaks.Any(t => t.Id == "fortnite-fso" && t.IsOn))
            problems.Add("\"Fortnite: disable fullscreen optimizations\" is on, which forces exclusive fullscreen. Turn it off on the Tweaks page to use the crosshair.");
        CrosshairWarningText.Text = string.Join("\n\n", problems);
        CrosshairWarning.Visibility = problems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    void CrosshairColor_Click(object sender, RoutedEventArgs e) => _crosshair.Color = (string)((Button)sender).Tag;

    void CrosshairRecenter_Click(object sender, RoutedEventArgs e) => _crosshair.ResetPosition();

    // ---- FPS benchmark ----

    void InitBenchmark()
    {
        BenchGameCombo.ItemsSource = _settings.GameProcesses;
        BenchGameCombo.SelectedItem = _settings.GameProcesses.FirstOrDefault(n => System.Diagnostics.Process.GetProcessesByName(n).Length > 0)
                                      ?? _settings.GameProcesses.FirstOrDefault();
        BenchLengthCombo.ItemsSource = new[] { "30 seconds", "60 seconds", "120 seconds" };
        BenchLengthCombo.SelectedIndex = 1;
        PresentMonCard.Visibility = Benchmark.Installed ? Visibility.Collapsed : Visibility.Visible;
        BenchList.ItemsSource = Benchmark.History();
    }

    async void DownloadPresentMon_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            BenchStatus.Text = "Downloading PresentMon...";
            await Benchmark.DownloadAsync();
            PresentMonCard.Visibility = Visibility.Collapsed;
            BenchStatus.Text = "PresentMon is ready.";
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException or IOException)
        {
            BenchStatus.Text = "Download failed: " + ex.Message;
        }
    }

    async void StartBenchmark_Click(object sender, RoutedEventArgs e)
    {
        if (!Benchmark.Installed)
        {
            BenchStatus.Text = "Download PresentMon first (button above).";
            return;
        }
        if (BenchGameCombo.SelectedItem is not string game) return;
        var seconds = new[] { 30, 60, 120 }[Math.Max(0, BenchLengthCombo.SelectedIndex)];
        var label = string.IsNullOrWhiteSpace(BenchLabelBox.Text) ? "Run" : BenchLabelBox.Text.Trim();

        BenchStartButton.IsEnabled = false;
        var remaining = seconds;
        var countdown = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        countdown.Tick += (_, _) => BenchStatus.Text = $"Recording {game}... {Math.Max(0, --remaining)} s left. Keep playing.";
        BenchStatus.Text = $"Recording {game}... {remaining} s left. Switch to the game and play.";
        countdown.Start();
        try
        {
            var result = await Benchmark.RunAsync(game, label, seconds);
            BenchStatus.Text = $"Done: {result.Summary} ({result.Frames:N0} frames).";
            BenchList.ItemsSource = Benchmark.History();
        }
        catch (InvalidOperationException ex) { BenchStatus.Text = ex.Message; }
        finally
        {
            countdown.Stop();
            BenchStartButton.IsEnabled = true;
        }
    }

    void ClearBenchmarks_Click(object sender, RoutedEventArgs e)
    {
        Benchmark.ClearHistory();
        BenchList.ItemsSource = Benchmark.History();
    }

    // ---- Safety restore point ----

    const string RestorePointKey = "restorepoint.created";

    /// <summary>Before the very first tweak on this PC, makes a Windows restore point as a full safety net.</summary>
    async Task EnsureRestorePointAsync()
    {
        if (!_settings.AutoRestorePoint || !Shell.IsAdmin || BackupStore.Data.Values.ContainsKey(RestorePointKey)) return;

        SetStatus("Creating a Windows restore point before the first change (one time only, can take a minute)...");
        var (code, _) = await Task.Run(() =>
            Shell.PowerShell("Checkpoint-Computer -Description 'Before SpudBoost' -RestorePointType MODIFY_SETTINGS"));
        // Record the attempt either way so a PC with System Protection off isn't delayed on every change.
        BackupStore.Data.Values[RestorePointKey] = code == 0 ? DateTime.Now.ToString("s") : "failed";
        BackupStore.Save();
        SetStatus(code == 0
            ? "Restore point created."
            : "Couldn't create a restore point (System Protection may be off). SpudBoost' own undo still works.");
    }

    // ---- Settings ----

    void TrayBox_Click(object sender, RoutedEventArgs e)
    {
        _settings.MinimizeToTray = TrayBox.IsChecked == true;
        _settings.Save();
    }

    void StartWithWindowsBox_Click(object sender, RoutedEventArgs e)
    {
        StartWithWindows.Enabled = StartWithWindowsBox.IsChecked == true;
        SetStatus(StartWithWindows.Enabled ? "SpudBoost will start in the tray when you sign in." : "SpudBoost won't start with Windows.");
    }

    void AutoRestorePointBox_Click(object sender, RoutedEventArgs e)
    {
        _settings.AutoRestorePoint = AutoRestorePointBox.IsChecked == true;
        _settings.Save();
    }

    UpdateInfo? _update;

    async void CheckUpdates_Click(object sender, RoutedEventArgs e) => await CheckForUpdatesAsync(quiet: false);

    async Task CheckForUpdatesAsync(bool quiet)
    {
        if (!UpdateChecker.Configured)
        {
            UpdateText.Text = "Update checks will work once releases are published on GitHub.";
            return;
        }
        try
        {
            _update = await UpdateChecker.CheckAsync();
            UpdateText.Text = _update == null ? "You have the latest version." : $"Version {_update.Version} is available.";
            DownloadUpdateButton.Visibility = _update == null ? Visibility.Collapsed : Visibility.Visible;
            UpdateCard.Visibility = _update == null ? Visibility.Collapsed : Visibility.Visible;
            if (_update == null) return;

            UpdateTitle.Text = $"Update available: v{_update.Version}";
            // Pop up a Windows notification once per new version.
            var version = _update.Version.ToString();
            if (_settings.LastNotifiedUpdate != version)
            {
                _tray.ShowHint($"SpudBoost {version} is out! Click here, then \"Update now\".");
                _settings.LastNotifiedUpdate = version;
                _settings.Save();
            }
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException or System.Text.Json.JsonException
                                       or KeyNotFoundException or InvalidOperationException)
        {
            if (!quiet) UpdateText.Text = "Couldn't check for updates: " + ex.Message;
        }
    }

    void DownloadUpdate_Click(object sender, RoutedEventArgs e) => UpdateNow_Click(sender, e);

    /// <summary>Downloads the new installer, runs it silently and exits; the installer reopens SpudBoost when done.</summary>
    async void UpdateNow_Click(object sender, RoutedEventArgs e)
    {
        if (_update == null) return;
        UpdateNowButton.IsEnabled = DownloadUpdateButton.IsEnabled = false;
        try
        {
            var progress = new Progress<int>(p => UpdateCardText.Text = UpdateText.Text = $"Downloading update... {p}%");
            if (await UpdateChecker.DownloadAndInstallAsync(_update, progress))
            {
                UpdateCardText.Text = UpdateText.Text = "Installing... SpudBoost will reopen in a few seconds.";
                ExitApp();
                return;
            }
            // No installer attached to the release: open the download page instead.
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_update.PageUrl) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException or IOException
                                       or System.ComponentModel.Win32Exception)
        {
            UpdateCardText.Text = UpdateText.Text = "Update failed: " + ex.Message;
        }
        UpdateNowButton.IsEnabled = DownloadUpdateButton.IsEnabled = true;
    }

    // ---- Navigation ----

    void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (Pages == null) return;
        var target = (string)((RadioButton)sender).Tag;
        foreach (FrameworkElement page in Pages.Children)
        {
            var show = page.Name == target;
            page.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (show) AnimatePageIn(page);
        }
        if (target == "FortnitePage") LoadFortnite();
        if (target == "CrosshairPage") UpdateCrosshairWarning();
        OnPageShown(target);
    }

    void RestartAdmin_Click(object sender, RoutedEventArgs e)
    {
        _exiting = true;
        try { Shell.RestartAsAdmin(); }
        catch (System.ComponentModel.Win32Exception)
        {
            _exiting = false;
            SetStatus("Administrator restart was cancelled.");
        }
    }

    // ---- Tweaks ----

    async void TweakToggle_Click(object sender, RoutedEventArgs e)
    {
        var tweak = (Tweak)((ToggleButton)sender).DataContext;
        var turnOn = tweak.IsOn;
        if (turnOn && tweak.ConfirmMessage != null &&
            MessageBox.Show(this, tweak.ConfirmMessage, tweak.Name, MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            tweak.IsOn = false;
            return;
        }
        if (turnOn) await EnsureRestorePointAsync();
        SetStatus($"{(turnOn ? "Applying" : "Reverting")} {tweak.Name}...");
        try
        {
            await Task.Run(() => { if (turnOn) tweak.Apply(); else tweak.Revert(); });
            await Task.Run(tweak.Refresh);
            SetStatus($"{tweak.Name}: {(turnOn ? "applied" : "restored to original")}.{(tweak.PendingNote != null ? $" ({tweak.PendingNote})" : "")}");
        }
        catch (Exception ex)
        {
            ShowError(ex);
            await Task.Run(tweak.Refresh);
        }
        UpdateUndoSummary();
    }

    async void ApplyAllSystem_Click(object sender, RoutedEventArgs e)
    {
        // Tweaks with a security trade-off are never switched on in bulk; they need their own confirmation.
        var pending = _systemTweaks.Where(t => !t.IsOn && t.CanToggle && t.ConfirmMessage == null).ToList();
        if (pending.Count > 0) await EnsureRestorePointAsync();
        SetStatus($"Applying {pending.Count} tweak(s)...");
        var failures = new List<string>();
        foreach (var t in pending)
        {
            try { await Task.Run(t.Apply); }
            catch (Exception ex) { failures.Add($"{t.Name}: {ex.Message}"); }
        }
        await RefreshAllAsync();
        if (failures.Count > 0) ShowError(new Exception(string.Join("\n\n", failures)));
        else if (pending.Count == 0) SetStatus("Every available tweak is already on.");
        else SetStatus($"Applied {pending.Count} tweak(s).{(pending.Any(t => t.PendingNote != null) ? " Some need a restart or sign-out to take effect." : "")}");
    }

    // ---- Startup apps ----

    void StartupToggle_Click(object sender, RoutedEventArgs e)
    {
        var entry = (StartupEntry)((ToggleButton)sender).DataContext;
        var enable = entry.Enabled;
        try
        {
            StartupManager.SetEnabled(entry, enable);
            SetStatus($"{entry.Name} will {(enable ? "" : "no longer ")}start with Windows (from next sign-in).");
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            entry.Enabled = !enable;
            ShowError(ex);
        }
    }

    // ---- Game boost ----

    void BoostOption_Click(object sender, RoutedEventArgs e)
    {
        _settings.RaiseGamePriority = RaisePriorityBox.IsChecked == true;
        _settings.LowerBackgroundApps = LowerBackgroundBox.IsChecked == true;
        _settings.CloseAppsOnGameStart = CloseAppsBox.IsChecked == true;
        _settings.Save();
    }

    async void CloseAppsNow_Click(object sender, RoutedEventArgs e)
    {
        var names = Lines(AppsToCloseBox.Text);
        var skipped = names.Where(ProcessCleaner.IsProtected).ToList();
        SetStatus("Closing unnecessary apps...");
        var closed = await Task.Run(() => ProcessCleaner.Close(names));
        SetStatus((closed.Count == 0 ? "None of the listed apps were running." : $"Closed: {string.Join(", ", closed)}.") +
                  (skipped.Count > 0 ? $" Skipped protected: {string.Join(", ", skipped)}." : ""));
    }

    static List<string> Lines(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(l => l.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? l[..^4] : l)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    void SaveBoostLists_Click(object sender, RoutedEventArgs e)
    {
        _settings.GameProcesses = Lines(GamesBox.Text);
        _settings.BackgroundProcesses = Lines(BackgroundBox.Text);
        _settings.AppsToClose = Lines(AppsToCloseBox.Text);
        _settings.Save();
        SetStatus("Game Boost lists saved.");
    }

    // ---- Network ----

    async void DnsCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DnsCombo.SelectedItem is not string provider || provider == _dns.Provider) return;
        if (_dns.IsOn)
        {
            SetStatus("Turn off \"Fast public DNS\" before switching provider.");
            DnsCombo.SelectedItem = _dns.Provider;
            return;
        }
        _dns.Provider = _settings.DnsProvider = provider;
        _settings.Save();
        await Task.Run(_dns.Refresh);
    }

    async void TestPing_Click(object sender, RoutedEventArgs e)
    {
        PingButton.IsEnabled = false;
        SetStatus("Testing ping to Fortnite regions (about 5 seconds)...");
        try
        {
            var results = await PingTester.RunAsync();
            PingList.ItemsSource = results;
            var best = results.FirstOrDefault(r => r.Best);
            SetStatus(best == null ? "No Fortnite region replied. Check your connection." : $"Best region: {best.Region} ({best.Average:0} ms).");
        }
        finally { PingButton.IsEnabled = true; }
    }

    async void FlushDns_Click(object sender, RoutedEventArgs e)
    {
        var (code, output) = await Task.Run(() => Shell.Run("ipconfig", "/flushdns"));
        SetStatus(code == 0 ? "DNS cache flushed." : output);
    }

    // ---- Fortnite ----

    void LoadFortnite()
    {
        if (!FortniteSettings.ConfigExists)
        {
            FortniteWarning.Visibility = Visibility.Visible;
            FortniteWarningText.Text = $"Fortnite's settings file was not found at {FortniteSettings.ConfigPath}. Launch Fortnite once, then come back.";
            FortniteEditor.IsEnabled = false;
            return;
        }
        FortniteEditor.IsEnabled = true;
        try { _fortnite.Load(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ShowError(ex); }

        var running = FortniteSettings.GameRunning;
        FortniteWarning.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        FortniteWarningText.Text = "Fortnite is running. Close it before saving. It overwrites this file when it exits.";
    }

    void ResPreset_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ResPresetCombo.SelectedIndex <= 0) return;
        var r = Resolutions[ResPresetCombo.SelectedIndex];
        _fortnite.ResX = r.X;
        _fortnite.ResY = r.Y;
        if (r.Label.Contains("stretched")) _fortnite.WindowMode = 0; // stretched only works in fullscreen
        ResPresetCombo.SelectedIndex = 0;
    }

    void UltraPotatoPreset_Click(object sender, RoutedEventArgs e)
    {
        _fortnite.ApplyUltraPotatoPreset();
        SetStatus("Ultra potato loaded: Performance mode, Low mesh, 25% 3D resolution, 1440×1080 stretched. Click Save to Fortnite.");
    }

    async void OpenNvidia_Click(object sender, RoutedEventArgs e)
    {
        var (_, output) = await Task.Run(() => Shell.PowerShell("(Get-AppxPackage -Name NVIDIACorp.NVIDIAControlPanel).PackageFamilyName"));
        if (string.IsNullOrWhiteSpace(output))
        {
            SetStatus("NVIDIA Control Panel isn't installed (no NVIDIA GPU, or get it from the Microsoft Store).");
            return;
        }
        Launch($"shell:AppsFolder\\{output.Trim()}!NVIDIACorp.NVIDIAControlPanel");
    }

    void OpenAmd_Click(object sender, RoutedEventArgs e)
    {
        var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"AMD\CNext\CNext\RadeonSoftware.exe");
        if (File.Exists(exe)) Launch(exe);
        else SetStatus("AMD Software isn't installed (no AMD GPU detected).");
    }

    static void Launch(string target) =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", target) { UseShellExecute = true });

    void CopyLegacyArgs_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(FortniteSettings.LegacyLaunchArgs);
        SetStatus($"Copied \"{FortniteSettings.LegacyLaunchArgs}\". Paste it into the Epic launcher's Additional Command Line Arguments for Fortnite.");
    }

    void PotatoPreset_Click(object sender, RoutedEventArgs e)
    {
        _fortnite.ApplyPotatoPreset();
        SetStatus("Potato preset loaded. Adjust anything you like, then click Save to Fortnite.");
    }

    void CompetitivePreset_Click(object sender, RoutedEventArgs e)
    {
        _fortnite.ApplyCompetitivePreset();
        SetStatus("Competitive preset loaded. Adjust anything you like, then click Save to Fortnite.");
    }

    void ReloadFortnite_Click(object sender, RoutedEventArgs e)
    {
        LoadFortnite();
        SetStatus("Reloaded Fortnite settings from file.");
    }

    void SaveFortnite_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var backup = _fortnite.Save();
            SetStatus($"Saved to Fortnite. Previous file backed up as {Path.GetFileName(backup)}.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException) { ShowError(ex); }
    }

    void RestoreFortnite_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "Put back the Fortnite settings you had before SpudBoost first changed them?",
                "SpudBoost", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            FortniteSettings.RestoreOriginal();
            LoadFortnite();
            SetStatus("Fortnite settings restored to the original.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException) { ShowError(ex); }
    }

    // ---- Backup & undo ----

    async void RestorePoint_Click(object sender, RoutedEventArgs e)
    {
        if (!Shell.IsAdmin)
        {
            SetStatus("Creating a restore point needs administrator rights. Use \"Restart as admin\".");
            return;
        }
        SetStatus("Creating restore point (this can take a minute)...");
        var (code, output) = await Task.Run(() =>
            Shell.PowerShell("Checkpoint-Computer -Description 'SpudBoost' -RestorePointType MODIFY_SETTINGS"));
        if (code == 0) SetStatus("Restore point created (Windows skips it if one was made in the last 24 hours).");
        else ShowError(new Exception("Could not create a restore point. System Protection may be turned off.\n\n" + output));
    }

    async void UndoAll_Click(object sender, RoutedEventArgs e)
    {
        var undoable = AllTweaks.Where(t => t.HasBackup).ToList();
        if (undoable.Count == 0)
        {
            SetStatus("Nothing to undo.");
            return;
        }
        var failures = new List<string>();
        foreach (var t in undoable)
        {
            if (!t.CanToggle) { failures.Add($"{t.Name}: needs administrator rights."); continue; }
            try { await Task.Run(t.Revert); }
            catch (Exception ex) { failures.Add($"{t.Name}: {ex.Message}"); }
        }
        await RefreshAllAsync();
        if (failures.Count > 0) ShowError(new Exception(string.Join("\n\n", failures)));
        else SetStatus($"Undid {undoable.Count} tweak(s).");
    }

    void OpenBackups_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(BackupStore.Folder);
        Shell.OpenFolder(BackupStore.Folder);
    }
}
