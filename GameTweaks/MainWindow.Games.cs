using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using GameTweaks.Core;
using GameTweaks.Games;
using GameTweaks.Tweaks;
using Microsoft.Win32;

namespace GameTweaks;

/// <summary>Roblox, Minecraft and CS2 pages: presets, editable settings and per-game Windows tweaks.</summary>
public partial class MainWindow
{
    readonly List<GameProfile> _games = GameCatalog.All();
    readonly List<Tweak> _gameTweaks = [];
    GameProfile? _currentGame;

    /// <summary>Builds each game's "use the high-performance GPU" tweak from the executables found on this PC.</summary>
    void InitGames()
    {
        foreach (var game in _games)
        {
            var exes = game.FindExes();
            if (exes.Count == 0) continue;
            _gameTweaks.Add(new RegistryTweak
            {
                Id = $"gpu-{game.Id}",
                Name = $"{game.Name}: use the high-performance GPU",
                Description = $"Tells Windows to always run {game.Name} on your dedicated graphics card. Big gain on laptops; harmless on desktops.",
                Values = exes.Select(exe => new RegValue(RegistryHive.CurrentUser, @"Software\Microsoft\DirectX\UserGpuPreferences",
                    exe, RegistryValueKind.String, "GpuPreference=2;")).ToList(),
                Category = "Games",
            });
        }
    }

    void ShowGame(string id)
    {
        var game = _games.First(g => g.Id == id);
        _currentGame = game;
        GameTitle.Text = $"{game.Emoji}  {game.Name}";
        GameDescription.Text = game.Description;
        GamePresetList.ItemsSource = game.PresetNames;
        GameTweakList.ItemsSource = _gameTweaks.Where(t => t.Id == $"gpu-{game.Id}").ToList();
        GameBoostSwitch.IsChecked = game.Processes.All(p => _settings.GameProcesses.Contains(p, StringComparer.OrdinalIgnoreCase));
        LoadCurrentGame();
    }

    void LoadCurrentGame()
    {
        if (_currentGame is not { } game) return;
        try { game.Load(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ShowError(ex); }

        GameSettingList.ItemsSource = game.Loaded;
        GameEditor.IsEnabled = game.Loaded.Count > 0;
        var warning = game.Loaded.Count == 0 ? game.Status
            : game.Running ? $"{game.Name} is running. Close it before saving; it rewrites its settings when it exits."
            : game.Status;
        GameWarningText.Text = warning;
        GameWarning.Visibility = string.IsNullOrEmpty(warning) ? Visibility.Collapsed : Visibility.Visible;
    }

    void GamePreset_Click(object sender, RoutedEventArgs e)
    {
        if (_currentGame is not { } game) return;
        var preset = (string)((Button)sender).DataContext;
        game.ApplyPreset(preset);
        SetStatus($"{preset} loaded for {game.Name}. Adjust anything you like, then click Save to game.");
    }

    void SaveGame_Click(object sender, RoutedEventArgs e)
    {
        if (_currentGame is not { } game) return;
        try
        {
            game.Save();
            SetStatus($"Saved to {game.Name} (backup taken). Start the game to see it.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException) { ShowError(ex); }
    }

    void ReloadGame_Click(object sender, RoutedEventArgs e)
    {
        LoadCurrentGame();
        SetStatus("Reloaded from the game's settings file.");
    }

    void RestoreGame_Click(object sender, RoutedEventArgs e)
    {
        if (_currentGame is not { } game) return;
        if (MessageBox.Show(this, $"Put back {game.Name}'s settings from before SpudBoost first changed them?", "SpudBoost",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            game.RestoreOriginal();
            LoadCurrentGame();
            SetStatus($"{game.Name} settings restored to the original.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException) { ShowError(ex); }
    }

    /// <summary>Adds or removes the game's processes from the Game Boost list.</summary>
    void GameBoostSwitch_Click(object sender, RoutedEventArgs e)
    {
        if (_currentGame is not { } game) return;
        var on = ((ToggleButton)sender).IsChecked == true;
        foreach (var process in game.Processes)
        {
            _settings.GameProcesses.RemoveAll(p => p.Equals(process, StringComparison.OrdinalIgnoreCase));
            if (on) _settings.GameProcesses.Add(process);
        }
        _settings.Save();
        GamesBox.Text = string.Join(Environment.NewLine, _settings.GameProcesses);
        SetStatus(on ? $"{game.Name} added to Game Boost." : $"{game.Name} removed from Game Boost.");
    }
}
