using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using GameTweaks.Core;
using GameTweaks.Fortnite;
using GameTweaks.Tweaks;

namespace GameTweaks;

/// <summary>One-click presets and the first-run welcome screen.</summary>
public partial class MainWindow
{
    void InitPresets()
    {
        PresetList.ItemsSource = Presets.All;
        WelcomePresetList.ItemsSource = Presets.All;
        if (!_settings.WelcomeShown)
        {
            WelcomeOverlay.Visibility = Visibility.Visible;
            WelcomeAdminNote.Visibility = Shell.IsAdmin ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    // ---- "What's new" after an update ----

    /// <summary>
    /// Shows the release notes once, the first time a new version runs. A brand-new install gets the welcome screen instead.
    /// </summary>
    async void InitWhatsNew()
    {
        var current = UpdateChecker.CurrentVersion;
        var last = _settings.LastSeenVersion;
        if (last == current.ToString()) return;

        var freshInstall = last == null && !_settings.WelcomeShown;
        _settings.LastSeenVersion = current.ToString();
        _settings.Save();
        if (freshInstall) return;

        var notes = await UpdateChecker.GetReleaseNotesAsync(current);
        var lines = notes != null ? ReleaseNotes.Parse(notes) : [];
        if (lines.Count == 0)
            lines = [new ReleaseNoteLine("SpudBoost was updated. The full list of changes is on GitHub.", false, false)];

        WhatsNewTitle.Text = $"What's new in SpudBoost {current}";
        WhatsNewList.ItemsSource = lines;
        WhatsNewMoreButton.Visibility = UpdateChecker.Configured ? Visibility.Visible : Visibility.Collapsed;
        WhatsNewOverlay.Visibility = Visibility.Visible;
        WhatsNewOverlay.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250)));
    }

    void CloseWhatsNew_Click(object sender, RoutedEventArgs e)
    {
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(200));
        fade.Completed += (_, _) => WhatsNewOverlay.Visibility = Visibility.Collapsed;
        WhatsNewOverlay.BeginAnimation(OpacityProperty, fade);
    }

    void WhatsNewMore_Click(object sender, RoutedEventArgs e) =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(UpdateChecker.ReleasePageUrl(UpdateChecker.CurrentVersion)) { UseShellExecute = true });

    async void Preset_Click(object sender, RoutedEventArgs e)
    {
        var preset = (Preset)((FrameworkElement)sender).DataContext;
        if (MessageBox.Show(this, $"Apply the {preset.Name} preset?\n\n{preset.Summary}\n\nEverything it changes can be undone from Backup & Undo.",
                "SpudBoost", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        CloseWelcome();
        await ApplyPresetAsync(preset);
    }

    void SkipWelcome_Click(object sender, RoutedEventArgs e) => CloseWelcome();

    void CloseWelcome()
    {
        if (WelcomeOverlay.Visibility != Visibility.Visible) return;
        _settings.WelcomeShown = true;
        _settings.Save();
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(200));
        fade.Completed += (_, _) => WelcomeOverlay.Visibility = Visibility.Collapsed;
        WelcomeOverlay.BeginAnimation(OpacityProperty, fade);
    }

    async Task ApplyPresetAsync(Preset preset)
    {
        await EnsureTweaksCheckedAsync();
        var known = AllTweaks.ToDictionary(t => t.Id);
        var wanted = preset.TweakIds.Where(known.ContainsKey).Select(id => known[id]).Where(t => !t.IsOn).ToList();
        var applicable = wanted.Where(t => t.CanToggle && t.ConfirmMessage == null).ToList();
        var needAdmin = wanted.Where(t => t.RequiresAdmin && !Shell.IsAdmin).ToList();

        var failures = new List<string>();
        if (applicable.Count > 0) await EnsureRestorePointAsync();
        var done = 0;
        foreach (var t in applicable)
        {
            SetStatus($"{preset.Name} preset: {t.Name} ({++done}/{applicable.Count})...");
            try { await Task.Run(t.Apply); }
            catch (Exception ex) { failures.Add($"{t.Name}: {ex.Message.Split('\n')[0]}"); }
        }
        await RefreshAllAsync();

        var fortniteNote = "";
        if (preset.FortnitePreset != null && FortniteSettings.ConfigExists)
        {
            if (FortniteSettings.GameRunning) fortniteNote = " Fortnite settings skipped (close Fortnite and apply again).";
            else
            {
                try
                {
                    _fortnite.Load();
                    if (preset.FortnitePreset == "ultra") _fortnite.ApplyUltraPotatoPreset();
                    else _fortnite.ApplyCompetitivePreset();
                    _fortnite.Save();
                    fortniteNote = " Fortnite settings updated.";
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.IO.IOException or UnauthorizedAccessException)
                {
                    failures.Add("Fortnite: " + ex.Message);
                }
            }
        }

        var summary = $"{preset.Name} preset applied: {applicable.Count - failures.Count(f => !f.StartsWith("Fortnite"))} tweak(s) turned on.{fortniteNote}";
        if (needAdmin.Count > 0) summary += $" {needAdmin.Count} more need admin: click \"Run as admin\" and apply the preset again.";
        SetStatus(summary);
        if (failures.Count > 0) ShowError(new Exception(string.Join("\n\n", failures)));
        else MessageBox.Show(this, summary, "SpudBoost", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
