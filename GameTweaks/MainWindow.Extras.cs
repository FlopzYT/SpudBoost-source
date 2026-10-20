using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using GameTweaks.Core;
using GameTweaks.Tweaks;

namespace GameTweaks;

/// <summary>Library (Boost &amp; Play), Spud Score, play sessions and accent colours.</summary>
public partial class MainWindow
{
    // ---- Library ----

    List<LibraryGame> _library = [];
    bool _libraryLoaded;

    async Task LoadLibraryAsync()
    {
        _libraryLoaded = true;
        LibraryStatus.Text = "Looking for games...";
        _library = await Task.Run(GameLibrary.Scan);
        FilterLibrary();
    }

    void FilterLibrary()
    {
        var query = LibrarySearchBox.Text.Trim();
        LibrarySearchHint.Visibility = query.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var shown = _library.Where(g => query.Length == 0 || g.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToList();
        LibraryList.ItemsSource = shown;
        LibraryStatus.Text = _library.Count == 0
            ? "No Steam, Epic Games or Minecraft games found on this PC."
            : shown.Count == 0 ? "No games match your search." : $"{_library.Count} game{(_library.Count == 1 ? "" : "s")} installed.";
    }

    void LibrarySearch_TextChanged(object sender, TextChangedEventArgs e) { if (_libraryLoaded) FilterLibrary(); }

    async void LibraryRefresh_Click(object sender, RoutedEventArgs e) => await LoadLibraryAsync();

    void PlayOnly_Click(object sender, RoutedEventArgs e)
    {
        var game = (LibraryGame)((FrameworkElement)sender).DataContext;
        if (Launch(game)) SetStatus($"Starting {game.Name}...");
    }

    async void BoostPlay_Click(object sender, RoutedEventArgs e)
    {
        var game = (LibraryGame)((FrameworkElement)sender).DataContext;
        _booster.Enabled = true;
        var closed = _settings.AppsToClose.Count > 0 ? await Task.Run(() => Boost.ProcessCleaner.Close(_settings.AppsToClose.ToList())) : [];
        if (!Launch(game)) return;
        SetStatus($"Boost & Play: Game Boost on{(closed.Count > 0 ? $", closed {string.Join(", ", closed)}" : "")}. Starting {game.Name}...");
        if (game.InstallFolder != null) await LearnGameProcessAsync(game);
    }

    bool Launch(LibraryGame game)
    {
        try
        {
            GameLibrary.Launch(game);
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            ShowError(new Exception($"Couldn't start {game.Name}. Is {game.Source} installed?\n\n{ex.Message}"));
            return false;
        }
    }

    /// <summary>
    /// Watches for up to 3 minutes for the game's process to appear (launchers take a while), then adds it to
    /// the Game Boost list so it's boosted and its sessions are recorded from now on.
    /// </summary>
    async Task LearnGameProcessAsync(LibraryGame game)
    {
        for (var i = 0; i < 36; i++)
        {
            await Task.Delay(TimeSpan.FromSeconds(5));
            var name = await Task.Run(() => GameLibrary.FindGameProcess(game.InstallFolder!));
            if (name == null) continue;
            if (_settings.GameProcesses.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                SetStatus($"{game.Name} is running and boosted.");
                return;
            }
            // Give the game a few more seconds: the first process is sometimes a short-lived pre-launcher.
            await Task.Delay(TimeSpan.FromSeconds(10));
            var settled = await Task.Run(() => GameLibrary.FindGameProcess(game.InstallFolder!)) ?? name;
            _settings.GameProcesses.Add(settled);
            _settings.Save();
            GamesBox.Text = string.Join(Environment.NewLine, _settings.GameProcesses);
            SetStatus($"{game.Name} is running. Added {settled} to Game Boost, so it's boosted every time from now on.");
            return;
        }
    }

    // ---- Spud Score ----

    IReadOnlyList<HealthItem> _health = [];
    SpudScoreResult? _score;

    void UpdateScore()
    {
        if (ScoreNumber == null || AllTweaks.All(t => !t.IsChecked)) return;
        var result = SpudScore.Compute(_systemTweaks.Concat(_networkTweaks), _health, _booster.Enabled);
        var previous = _score?.Score;
        _score = result;
        ScoreRank.Text = $"{result.Emoji}  {result.Rank}";
        ScoreNext.Text = "Next: " + result.NextStep;
        ScoreFixButton.Visibility = result.NextTweak is { CanToggle: true } || (result.NextTweak == null && !_booster.Enabled)
            ? Visibility.Visible : Visibility.Collapsed;
        AnimateScore(previous ?? 0, result.Score);
    }

    void AnimateScore(int from, int to)
    {
        if (from == to && ScoreNumber.Text == to.ToString()) return;
        var start = DateTime.Now;
        var duration = TimeSpan.FromMilliseconds(from == to ? 1 : 700);
        EventHandler? tick = null;
        tick = (_, _) =>
        {
            var t = Math.Min(1, (DateTime.Now - start) / duration);
            var eased = 1 - Math.Pow(1 - t, 3);
            var value = from + (to - from) * eased;
            ScoreNumber.Text = Math.Round(value).ToString();
            ScoreArc.Data = Arc(value / 100);
            if (t >= 1) CompositionTarget.Rendering -= tick;
        };
        CompositionTarget.Rendering += tick;
    }

    /// <summary>Circle outline from 12 o'clock, clockwise, for a 74 px ring with a 7 px stroke.</summary>
    static Geometry Arc(double fraction)
    {
        fraction = Math.Clamp(fraction, 0, 0.9999);
        const double r = 33.5, c = 37;
        var angle = fraction * 2 * Math.PI;
        var end = new Point(c + r * Math.Sin(angle), c - r * Math.Cos(angle));
        var figure = new PathFigure { StartPoint = new Point(c, c - r), IsClosed = false };
        figure.Segments.Add(new ArcSegment(end, new Size(r, r), 0, fraction > 0.5, SweepDirection.Clockwise, true));
        var geometry = new PathGeometry([figure]);
        geometry.Freeze();
        return geometry;
    }

    async void ScoreFix_Click(object sender, RoutedEventArgs e)
    {
        if (_score?.NextTweak is { } tweak)
        {
            if (tweak.ConfirmMessage != null) return;
            await EnsureRestorePointAsync();
            SetStatus($"Applying {tweak.Name}...");
            try
            {
                await Task.Run(tweak.Apply);
                SetStatus($"{tweak.Name}: applied.");
            }
            catch (Exception ex) { ShowError(ex); }
            await Task.Run(tweak.Refresh);
            UpdateUndoSummary();
        }
        else if (!_booster.Enabled) _booster.Enabled = true;
        UpdateScore();
    }

    // ---- Play sessions ----

    SessionTracker? _sessions;

    void InitSessions()
    {
        _sessions = new SessionTracker(_settings);
        HeatAlertBox.IsChecked = _settings.GpuHeatAlerts;
        _sessions.SessionStarted += _ => RefreshSessions();
        _sessions.SessionEnded += s =>
        {
            RefreshSessions();
            if (s.Minutes >= 10) _tray.ShowHint($"{s.Game}: played {s.Length}." + (s.PeakGpuTemp is { } t ? $" GPU peaked at {t:0} °C." : ""));
        };
        _sessions.GpuHot += (game, temp) =>
        {
            if (_settings.GpuHeatAlerts)
                _tray.ShowHint($"Your GPU hit {temp:0} °C in {game}. That's hot: check that the fans spin, clean out dust, or cap your FPS to cool it down.");
        };
        Application.Current.Exit += (_, _) => _sessions.Shutdown();
        RefreshSessions();
    }

    void RefreshSessions()
    {
        if (_sessions == null) return;
        var weekAgo = DateTime.Now.AddDays(-7);
        PlayToday.Text = SessionTracker.FormatMinutes(_sessions.MinutesSince(DateTime.Today));
        PlayWeek.Text = SessionTracker.FormatMinutes(_sessions.MinutesSince(weekAgo));
        PlayTop.Text = _sessions.Sessions.Where(s => s.Start >= weekAgo).GroupBy(s => s.Game)
            .OrderByDescending(g => g.Sum(s => s.Minutes)).Select(g => g.Key).FirstOrDefault() ?? "–";
        SessionList.ItemsSource = _sessions.Sessions.Take(30).ToList();
        NoSessionsText.Visibility = _sessions.Sessions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        SessionNow.Visibility = _sessions.CurrentGame != null ? Visibility.Visible : Visibility.Collapsed;
        if (_sessions.CurrentGame != null) SessionNow.Text = $"● Playing {_sessions.CurrentGame} since {_sessions.CurrentStart:h:mm tt}";
    }

    void ClearSessions_Click(object sender, RoutedEventArgs e)
    {
        if (_sessions == null || MessageBox.Show(this, "Delete your play session history?", "SpudBoost", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        _sessions.Clear();
        RefreshSessions();
    }

    void HeatAlertBox_Click(object sender, RoutedEventArgs e)
    {
        _settings.GpuHeatAlerts = HeatAlertBox.IsChecked == true;
        _settings.Save();
    }

    // ---- Accent colour ----

    sealed record AccentChoice(string Name, string Hex)
    {
        public Brush Brush => new SolidColorBrush((Color)ColorConverter.ConvertFromString(Hex));
        public Brush Border { get; init; } = Brushes.Transparent;
    }

    static readonly (string Name, string Hex)[] Accents =
    [
        ("Spud orange", "#FF7A1A"), ("Ketchup red", "#FF4D5E"), ("Slime green", "#3DDC84"), ("Ice blue", "#3DA9FF"),
        ("Grape purple", "#A970FF"), ("Bubblegum pink", "#FF5FB4"), ("Golden fries", "#FFC21A"), ("Mint", "#2EE6C5"),
    ];

    void InitAccent()
    {
        ApplyAccent(_settings.AccentColor);
        RefreshAccentList();
    }

    void RefreshAccentList() =>
        AccentList.ItemsSource = Accents.Select(a => new AccentChoice(a.Name, a.Hex)
        {
            Border = a.Hex.Equals(_settings.AccentColor, StringComparison.OrdinalIgnoreCase) ? Brushes.White : Brushes.Transparent,
        }).ToList();

    void Accent_Click(object sender, RoutedEventArgs e)
    {
        var hex = (string)((FrameworkElement)sender).Tag;
        _settings.AccentColor = hex;
        _settings.Save();
        ApplyAccent(hex);
        RefreshAccentList();
    }

    /// <summary>Swaps the accent brushes; every control uses them as dynamic resources, so the change is instant.</summary>
    static void ApplyAccent(string hex)
    {
        Color color;
        try { color = (Color)ColorConverter.ConvertFromString(hex); }
        catch (FormatException) { return; }
        var resources = Application.Current.Resources;
        resources["AccentBrush"] = Frozen(new SolidColorBrush(color));
        // The dim version is the accent blended 20% into the dark background, used for selected items.
        var bg = Color.FromRgb(0x17, 0x1A, 0x22);
        Color Mix(double t) => Color.FromRgb((byte)(bg.R + (color.R - bg.R) * t), (byte)(bg.G + (color.G - bg.G) * t), (byte)(bg.B + (color.B - bg.B) * t));
        resources["AccentDimBrush"] = Frozen(new SolidColorBrush(color == Color.FromRgb(0xFF, 0x7A, 0x1A) ? Color.FromRgb(0x33, 0x24, 0x1A) : Mix(0.2)));
    }

    static SolidColorBrush Frozen(SolidColorBrush b)
    {
        b.Freeze();
        return b;
    }
}
