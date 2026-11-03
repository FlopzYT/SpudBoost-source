using GameTweaks.Tweaks;

namespace GameTweaks;

/// <summary>Features that were removed but may still be switched on for some people, so they get undone.</summary>
public partial class MainWindow
{
    /// <summary>Tweaks that no longer appear anywhere. Never switched on again (e.g. from an old saved profile).</summary>
    static readonly HashSet<string> RetiredTweaks = ["nvidia-potato"];

    /// <summary>Reads every tweak's state, then undoes retired tweaks that SpudBoost had applied.</summary>
    async Task StartupRefreshAsync()
    {
        // Accounts were removed in 1.6.1: delete the saved sign-in from anyone who had signed in.
        try { System.IO.File.Delete(System.IO.Path.Combine(Core.BackupStore.Folder, "account.dat")); }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { }

        await RefreshAllAsync();
        var stuck = AllTweaks.Where(t => RetiredTweaks.Contains(t.Id) && t.HasBackup).ToList();
        if (stuck.Count == 0) return;
        if (!stuck.All(t => t.CanToggle))
        {
            SetStatus("Potato textures have been removed from SpudBoost. Click Run as admin once and SpudBoost will put your normal Fortnite textures back.");
            return;
        }
        foreach (var t in stuck)
        {
            try
            {
                await Task.Run(t.Revert);
                await Task.Run(t.Refresh);
            }
            catch (Exception ex) { ShowError(ex); return; }
        }
        UpdateUndoSummary();
        _tray.ShowHint("Potato textures were removed from SpudBoost, so your normal Fortnite textures are back. Restart Fortnite to see them.");
    }
}
