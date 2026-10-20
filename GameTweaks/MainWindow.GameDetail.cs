using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameTweaks.Core;
using GameTweaks.Tweaks;

namespace GameTweaks;

/// <summary>A game's own page: info, launch, how optimized it is and its game-specific tweaks.</summary>
public partial class MainWindow
{
    /// <summary>Game-specific tweaks per library game, created once the game's exe is known.</summary>
    readonly Dictionary<string, List<Tweak>> _libraryTweaks = [];
    LibraryGame? _detailGame;
    GameInfo? _detailInfo;

    /// <summary>System tweaks that matter for every game, counted in each game's "% optimized".</summary>
    static readonly string[] GameStatusTweaks = ["power-plan", "game-mode", "game-dvr", "hags", "game-priority", "windowed-optimizations", "mouse-accel", "core-parking"];

    /// <summary>Games with a full SpudBoost page of their own.</summary>
    static readonly Dictionary<string, (string Page, string Title)> SpudPages = new()
    {
        ["epic-fortnite"] = ("FortnitePage", "Fortnite"),
        ["steam-730"] = ("Game:cs2", "CS2"),
        ["ms-minecraft"] = ("Game:minecraft", "Minecraft"),
    };

    List<Tweak> GetLibraryTweaks(LibraryGame game, string exe)
    {
        if (!_libraryTweaks.TryGetValue(game.Key, out var tweaks))
            _libraryTweaks[game.Key] = tweaks = GameDetails.TweaksFor(game, exe, _settings);
        return tweaks;
    }

    /// <summary>After a library scan: set up tweaks for games whose exe is already known, so Backup &amp; Undo can see them.</summary>
    async Task LoadKnownLibraryTweaksAsync()
    {
        var created = new List<Tweak>();
        foreach (var game in _library)
            if (!_libraryTweaks.ContainsKey(game.Key) && GameDetails.CachedExe(game) is { } exe)
                created.AddRange(GetLibraryTweaks(game, exe));
        await Task.WhenAll(created.Select(t => Task.Run(t.Refresh)));
        if (created.Count > 0) UpdateUndoSummary();
    }

    async void GameTile_Click(object sender, MouseButtonEventArgs e) =>
        await OpenGameDetailAsync((LibraryGame)((FrameworkElement)sender).DataContext);

    async Task OpenGameDetailAsync(LibraryGame game)
    {
        _detailGame = game;
        _detailInfo = null;
        DetailName.Text = game.Name;
        DetailInitial.Text = game.Initial;
        DetailCover.Source = game.Image;
        var art = game.CoverPath != null;
        DetailCover.Stretch = art ? Stretch.UniformToFill : Stretch.Uniform;
        DetailCover.Width = DetailCover.Height = art ? double.NaN : 64;
        DetailDescription.Text = "Loading game info...";
        DetailTags.ItemsSource = new[] { game.Source };
        DetailRelease.Text = DetailDeveloper.Text = "";
        DetailFavorite.IsChecked = _settings.FavoriteGames.Contains(game.Key);
        DetailTweaks.ItemsSource = null;
        DetailTweaksText.Text = "Finding the game's files...";
        DetailMediaTab.Content = "Game Media";
        DetailMediaTab.IsChecked = true;
        ShowDetailTab();

        var spud = SpudPages.GetValueOrDefault(game.Key);
        DetailSpudPageCard.Visibility = spud.Page != null ? Visibility.Visible : Visibility.Collapsed;
        if (spud.Page != null)
        {
            DetailSpudPageTitle.Text = $"SpudBoost {spud.Title} page";
            DetailSpudPageText.Text = spud.Page == "FortnitePage"
                ? "Potato graphics, Legacy Performance mode, NVIDIA/AMD potato textures and every graphics setting in one place."
                : $"Change {spud.Title}'s graphics settings and apply FPS presets without opening the game.";
            DetailSpudPageButton.Content = $"Open {spud.Title} settings  ›";
        }
        var stale = DateTime.Now - _settings.LastAutoClean > TimeSpan.FromDays(7);
        DetailAttention.Visibility = stale ? Visibility.Visible : Visibility.Collapsed;
        DetailBoostText.Text = stale
            ? "You haven't cleaned junk files and caches for over a week. A quick clean before you play frees space and can cut loading stutter."
            : "Junk files were cleaned recently. Run it again any time before a long session.";

        NavigateTo("GameDetailPage");
        UpdateDetailStatus();

        var exeTask = Task.Run(() => GameDetails.FindMainExe(game));
        var info = await GameDetails.GetInfoAsync(game);
        if (_detailGame != game) return;
        _detailInfo = info;
        DetailDescription.Text = info?.Description is { Length: > 0 } d ? d : $"Installed with {game.Source}.";
        DetailTags.ItemsSource = new[] { game.Source }.Concat(info?.Genres ?? []).ToList();
        DetailRelease.Text = info?.ReleaseDate is { Length: > 0 } r ? $"📅  Release date: {r}" : "";
        DetailDeveloper.Text = info?.Developer is { Length: > 0 } dev ? $"🎮  Developer: {dev}" : "";
        DetailMediaTab.Content = info?.Screenshots.Count > 0 ? $"Game Media ({info.Screenshots.Count})" : "Game Media";
        ShowDetailTab();

        var exe = await exeTask;
        if (_detailGame != game) return;
        if (exe == null)
        {
            DetailTweaksText.Text = game.InstallFolder == null
                ? "This game is installed as a Store app, which Windows protects, so per-game Windows settings can't be changed. Use Boost & Play to launch it boosted."
                : "Couldn't find the game's exe in its folder, so per-game settings aren't available.";
            DetailTweaksAll.Visibility = Visibility.Collapsed;
            UpdateDetailStatus();
            return;
        }
        var tweaks = GetLibraryTweaks(game, exe);
        await Task.WhenAll(tweaks.Select(t => Task.Run(t.Refresh)));
        if (_detailGame != game) return;
        DetailTweaksText.Text = $"Windows settings just for {System.IO.Path.GetFileName(exe)}. Each one can be switched back off.";
        DetailTweaks.ItemsSource = tweaks;
        DetailTweaksAll.Visibility = Visibility.Visible;
        UpdateDetailStatus();
    }

    /// <summary>"% optimized": this game's own tweaks, the key system tweaks and Game Boost.</summary>
    void UpdateDetailStatus()
    {
        if (_detailGame == null || DetailPercent == null) return;
        var own = _libraryTweaks.GetValueOrDefault(_detailGame.Key) ?? [];
        var system = _systemTweaks.Where(t => GameStatusTweaks.Contains(t.Id) && t.IsChecked);
        var items = own.Concat(system).Select(t => (t.Name, t.IsOn)).Append((Name: "Game Boost", IsOn: _booster.Enabled)).ToList();
        var on = items.Count(i => i.IsOn);
        var percent = (int)Math.Round(100.0 * on / items.Count);
        DetailPercent.Text = $"{percent}% Optimized";
        DetailBar.ColumnDefinitions[0].Width = new GridLength(percent, GridUnitType.Star);
        DetailBar.ColumnDefinitions[1].Width = new GridLength(100 - percent, GridUnitType.Star);
        var pending = items.Where(i => !i.IsOn).Select(i => i.Name).ToList();
        DetailPending.Text = pending.Count == 0 ? "✓ Fully optimized" : $"{pending.Count} action{(pending.Count == 1 ? "" : "s")} pending";
        DetailPending.ToolTip = pending.Count == 0 ? null : "Still to do:\n• " + string.Join("\n• ", pending);
        DetailActive.Text = $"{own.Count(t => t.IsOn)}/{own.Count} Active";
    }

    async void DetailLaunch_Click(object sender, RoutedEventArgs e)
    {
        if (_detailGame != null) await BoostAndPlayAsync(_detailGame);
    }

    void DetailFavorite_Click(object sender, RoutedEventArgs e)
    {
        if (_detailGame == null) return;
        _settings.FavoriteGames.Remove(_detailGame.Key);
        if (DetailFavorite.IsChecked == true) _settings.FavoriteGames.Add(_detailGame.Key);
        _settings.Save();
        SortLibrary();
        UpdateHome();
    }

    /// <summary>Favourites first, then alphabetical.</summary>
    void SortLibrary()
    {
        _library = _library.OrderByDescending(g => _settings.FavoriteGames.Contains(g.Key)).ThenBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        if (_libraryLoaded) FilterLibrary();
    }

    async void DetailTweaksAll_Click(object sender, RoutedEventArgs e)
    {
        if (_detailGame == null || !_libraryTweaks.TryGetValue(_detailGame.Key, out var tweaks)) return;
        // Exclusive fullscreen hides overlays, so it's left for the user to choose.
        var pending = tweaks.Where(t => !t.IsOn && t.CanToggle && !t.Id.EndsWith("-fso")).ToList();
        if (pending.Count == 0)
        {
            SetStatus(tweaks.Any(t => !t.CanToggle) ? "The rest need admin: click \"Run as admin\"." : "Everything for this game is already on.");
            return;
        }
        await EnsureRestorePointAsync();
        var failures = new List<string>();
        foreach (var t in pending)
        {
            try { await Task.Run(t.Apply); }
            catch (Exception ex) { failures.Add($"{t.Name}: {ex.Message}"); }
            await Task.Run(t.Refresh);
        }
        UpdateUndoSummary();
        SetStatus($"Turned on {pending.Count - failures.Count} tweak(s) for {_detailGame.Name}.");
        if (failures.Count > 0) ShowError(new Exception(string.Join("\n\n", failures)));
    }

    void DetailBoostUp_Click(object sender, RoutedEventArgs e) => NavigateTo("CleanerPage");

    void DetailSpudPage_Click(object sender, RoutedEventArgs e)
    {
        if (_detailGame != null && SpudPages.TryGetValue(_detailGame.Key, out var spud)) NavigateTo(spud.Page);
    }

    void DetailTab_Checked(object sender, RoutedEventArgs e) => ShowDetailTab();

    void ShowDetailTab()
    {
        if (DetailMedia == null || _detailGame == null) return;
        var media = DetailMediaTab.IsChecked == true;
        var shots = _detailInfo?.Screenshots ?? [];
        DetailMedia.ItemsSource = media ? shots : null;
        DetailMedia.Visibility = media && shots.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        DetailEmpty.Visibility = media && shots.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        if (media)
        {
            DetailEmptyIcon.Text = "";
            DetailEmptyTitle.Text = _detailGame.SteamAppId == null ? "No media for this game" : "Loading media...";
            DetailEmptyText.Text = _detailGame.SteamAppId == null ? "Screenshots are only available for Steam games." : "";
            DetailEmptyButton.Visibility = Visibility.Collapsed;
            if (_detailInfo != null || _detailGame.SteamAppId == null)
                DetailEmptyTitle.Text = "No media for this game";
            return;
        }
        if (SpudPages.TryGetValue(_detailGame.Key, out var spud))
        {
            DetailEmptyIcon.Text = "";
            DetailEmptyTitle.Text = $"Full {spud.Title} graphics settings";
            DetailEmptyText.Text = $"SpudBoost can change {spud.Title}'s graphics settings for you, with FPS presets and a backup of your originals.";
            DetailEmptyButton.Content = "Open graphics settings";
        }
        else
        {
            DetailEmptyIcon.Text = "";
            DetailEmptyTitle.Text = "On the Way!";
            DetailEmptyText.Text = "SpudBoost doesn't have graphics settings for this game yet. Send a request to tell us you want them.";
            DetailEmptyButton.Content = "Request settings";
        }
        DetailEmptyButton.Visibility = Visibility.Visible;
    }

    void DetailEmptyButton_Click(object sender, RoutedEventArgs e)
    {
        if (_detailGame == null) return;
        if (SpudPages.TryGetValue(_detailGame.Key, out var spud))
        {
            NavigateTo(spud.Page);
            return;
        }
        // Opens a pre-filled GitHub issue in the browser; nothing is sent until the user submits it there.
        var title = Uri.EscapeDataString($"Graphics settings for {_detailGame.Name}");
        Process.Start(new ProcessStartInfo($"https://github.com/{UpdateChecker.Repository}/issues/new?title={title}") { UseShellExecute = true });
    }

    void DetailScreenshot_Click(object sender, MouseButtonEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not string url || !url.StartsWith("https://")) return;
        Process.Start(new ProcessStartInfo(url.Replace(".600x338", "")) { UseShellExecute = true });
    }
}
