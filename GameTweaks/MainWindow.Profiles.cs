using System.IO;
using System.Windows;
using GameTweaks.Core;
using GameTweaks.Fortnite;
using GameTweaks.Games;
using GameTweaks.Tweaks;

namespace GameTweaks;

/// <summary>My profiles: save the current setup under a name and switch back to it later.</summary>
public partial class MainWindow
{
    List<PersonalProfile> _profiles = PersonalProfiles.Load();

    void RefreshProfiles()
    {
        ProfileList.ItemsSource = null;
        ProfileList.ItemsSource = _profiles.OrderByDescending(p => p.Saved).ToList();
        NoProfilesText.Visibility = _profiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Captures what's set right now, based on the boxes ticked on the page.</summary>
    async Task<PersonalProfile> CaptureProfileAsync(string name)
    {
        await EnsureTweaksCheckedAsync();
        var profile = new PersonalProfile { Name = name, IncludesTweaks = ProfileTweaksBox.IsChecked == true };
        if (profile.IncludesTweaks) profile.Tweaks = AllTweaks.Where(t => t.IsOn).Select(t => t.Id).ToList();

        if (ProfileFortniteBox.IsChecked == true && FortniteSettings.ConfigExists)
        {
            var fortnite = new FortniteSettings();
            fortnite.Load();
            profile.Fortnite = fortnite.Snapshot();
        }
        if (ProfileCrosshairBox.IsChecked == true) profile.Crosshair = _crosshair.ExportCode();
        if (ProfileGamesBox.IsChecked == true)
            foreach (var game in GameCatalog.All().Where(g => g.Installed))
            {
                game.Load();
                if (game.Loaded.Count > 0) profile.Games[game.Id] = game.Snapshot();
            }
        return profile;
    }

    async void SaveProfile_Click(object sender, RoutedEventArgs e)
    {
        var name = NewProfileName.Text.Trim();
        if (name.Length == 0)
        {
            SetStatus("Give the profile a name first (e.g. \"Tournament\" or \"Max FPS\").");
            return;
        }
        var existing = _profiles.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existing != null && MessageBox.Show(this, $"Replace your saved \"{existing.Name}\" profile?", "SpudBoost",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        var profile = await CaptureProfileAsync(name);
        if (existing != null) _profiles.Remove(existing);
        _profiles.Add(profile);
        PersonalProfiles.Save(_profiles);
        RefreshProfiles();
        NewProfileName.Text = "";
        SetStatus($"Saved profile \"{name}\": {profile.Summary}.");
    }

    async void UpdateProfile_Click(object sender, RoutedEventArgs e)
    {
        var old = (PersonalProfile)((FrameworkElement)sender).DataContext;
        if (MessageBox.Show(this, $"Replace \"{old.Name}\" with your current setup?", "SpudBoost",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        var profile = await CaptureProfileAsync(old.Name);
        _profiles[_profiles.IndexOf(old)] = profile;
        PersonalProfiles.Save(_profiles);
        RefreshProfiles();
        SetStatus($"Updated profile \"{old.Name}\".");
    }

    void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        var profile = (PersonalProfile)((FrameworkElement)sender).DataContext;
        if (MessageBox.Show(this, $"Delete the \"{profile.Name}\" profile? This doesn't change any settings.", "SpudBoost",
                MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        _profiles.Remove(profile);
        PersonalProfiles.Save(_profiles);
        RefreshProfiles();
    }

    // ---- Publish and install .spudboost configs ----

    void PublishProfile_Click(object sender, RoutedEventArgs e)
    {
        var profile = (PersonalProfile)((FrameworkElement)sender).DataContext;
        var invalid = Path.GetInvalidFileNameChars();
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = $"Publish \"{profile.Name}\"",
            FileName = new string(profile.Name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()) + PersonalProfiles.FileExtension,
            Filter = $"SpudBoost config (*{PersonalProfiles.FileExtension})|*{PersonalProfiles.FileExtension}",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            PersonalProfiles.Export(profile, dialog.FileName);
            SetStatus($"Published to {dialog.FileName}. Send it to anyone; they double-click it to install it in SpudBoost.");
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{dialog.FileName}\"");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ShowError(ex); }
    }

    void InstallConfig_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Install a SpudBoost config",
            Filter = $"SpudBoost config (*{PersonalProfiles.FileExtension})|*{PersonalProfiles.FileExtension}",
        };
        if (dialog.ShowDialog(this) == true) InstallConfig(dialog.FileName);
    }

    /// <summary>Adds a config file to My profiles (nothing is applied), then offers to load it with the usual preview.</summary>
    public async void InstallConfig(string path)
    {
        PersonalProfile profile;
        try { profile = PersonalProfiles.Import(path); }
        catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException)
        {
            ShowError(ex);
            return;
        }

        var name = profile.Name;
        for (var i = 2; _profiles.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)); i++) name = $"{profile.Name} ({i})";
        profile.Name = name;
        _profiles.Add(profile);
        PersonalProfiles.Save(_profiles);
        RefreshProfiles();
        ShowFromTray();
        NavigateTo("ProfilesPage");

        if (MessageBox.Show(this, $"Installed the config \"{name}\" ({profile.Summary}).\n\nLoad it now? You'll see exactly what changes first.",
                "SpudBoost", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            await LoadProfileAsync(profile);
    }

    /// <summary>Selects a sidebar page by its tag (e.g. "ProfilesPage").</summary>
    void NavigateTo(string tag)
    {
        foreach (var nav in NavList.Children.OfType<System.Windows.Controls.RadioButton>())
            if ((string)nav.Tag == tag) nav.IsChecked = true;
    }

    /// <summary>A config double-clicked while SpudBoost was already running is handed over through this file.</summary>
    public static string PendingImportFile => Path.Combine(BackupStore.Folder, "pending-import.txt");

    void CheckPendingImport()
    {
        string path;
        try
        {
            if (!File.Exists(PendingImportFile)) return;
            path = File.ReadAllText(PendingImportFile).Trim();
            File.Delete(PendingImportFile);
        }
        catch (IOException) { return; }
        if (path.Length > 0) InstallConfig(path);
    }

    async void LoadProfile_Click(object sender, RoutedEventArgs e) =>
        await LoadProfileAsync((PersonalProfile)((FrameworkElement)sender).DataContext);

    /// <summary>Previews the changes, then switches tweaks and settings to match the profile.</summary>
    async Task LoadProfileAsync(PersonalProfile profile)
    {
        await EnsureTweaksCheckedAsync();

        var all = AllTweaks.ToList();
        var wanted = profile.Tweaks.ToHashSet();
        var toOn = profile.IncludesTweaks ? all.Where(t => wanted.Contains(t.Id) && !t.IsOn).ToList() : [];
        // Only undo tweaks SpudBoost itself applied; settings Windows already had are left alone.
        var toOff = profile.IncludesTweaks ? all.Where(t => !wanted.Contains(t.Id) && t.IsOn && t.HasBackup).ToList() : [];
        var skipped = toOn.Where(t => !t.CanToggle || t.ConfirmMessage != null).Concat(toOff.Where(t => !t.CanToggle)).ToList();
        toOn = toOn.Except(skipped).ToList();
        toOff = toOff.Except(skipped).ToList();

        var lines = new List<string>();
        if (toOn.Count > 0) lines.Add("Turn on: " + string.Join(", ", toOn.Select(t => t.Name)));
        if (toOff.Count > 0) lines.Add("Turn off (undo): " + string.Join(", ", toOff.Select(t => t.Name)));
        if (profile.Fortnite != null) lines.Add("Put back saved Fortnite settings");
        if (profile.Crosshair != null) lines.Add("Put back saved crosshairs");
        foreach (var id in profile.Games.Keys) lines.Add($"Put back saved {_games.FirstOrDefault(g => g.Id == id)?.Name ?? id} settings");
        if (skipped.Count > 0) lines.Add("Skipped (need admin or your own confirmation): " + string.Join(", ", skipped.Select(t => t.Name)));
        if (lines.Count == 0)
        {
            SetStatus($"You're already on \"{profile.Name}\". Nothing to change.");
            return;
        }
        if (MessageBox.Show(this, $"Load \"{profile.Name}\"?\n\n• " + string.Join("\n• ", lines), "SpudBoost",
                MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;

        var problems = new List<string>();
        if (toOn.Count > 0) await EnsureRestorePointAsync();
        foreach (var t in toOn)
        {
            try { await Task.Run(t.Apply); }
            catch (Exception ex) { problems.Add($"{t.Name}: {ex.Message.Split('\n')[0]}"); }
        }
        foreach (var t in toOff)
        {
            try { await Task.Run(t.Revert); }
            catch (Exception ex) { problems.Add($"{t.Name}: {ex.Message.Split('\n')[0]}"); }
        }
        await RefreshAllAsync();

        if (profile.Fortnite != null && FortniteSettings.ConfigExists)
        {
            try
            {
                _fortnite.Load();
                _fortnite.ApplySnapshot(profile.Fortnite);
                _fortnite.Save();
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException) { problems.Add("Fortnite: " + ex.Message); }
        }
        if (profile.Crosshair != null)
        {
            try
            {
                _crosshair.ImportCode(profile.Crosshair);
                CrosshairEditCombo.ItemsSource = _crosshair.EditTargets;
                CrosshairEditCombo.SelectedIndex = 0;
            }
            catch (FormatException ex) { problems.Add("Crosshair: " + ex.Message); }
        }
        foreach (var (id, values) in profile.Games)
        {
            var game = GameCatalog.All().FirstOrDefault(g => g.Id == id);
            if (game == null || !game.Installed) continue;
            try
            {
                game.Load();
                game.ApplySnapshot(values);
                game.Save();
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException) { problems.Add($"{game.Name}: {ex.Message}"); }
        }
        if (_currentGame != null) LoadCurrentGame();

        SetStatus($"Loaded profile \"{profile.Name}\".");
        if (problems.Count > 0) ShowError(new Exception("Some parts couldn't be applied:\n\n" + string.Join("\n\n", problems)));
    }
}
