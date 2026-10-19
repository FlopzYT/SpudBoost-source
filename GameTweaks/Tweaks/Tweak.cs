using System.Diagnostics;
using System.Globalization;
using GameTweaks.Core;

namespace GameTweaks.Tweaks;

/// <summary>What has to happen before a tweak takes effect.</summary>
public enum TakesEffect { Immediately, AfterRestart, AfterSignOut }

/// <summary>A reversible system change shown as one toggle in the UI.</summary>
public abstract class Tweak : ObservableObject
{
    static readonly DateTime BootTime = DateTime.UtcNow - TimeSpan.FromMilliseconds(Environment.TickCount64);

    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public bool RequiresAdmin { get; init; }
    public TakesEffect TakesEffect { get; init; }

    /// <summary>When set, the user must confirm this warning before the tweak is switched on.</summary>
    public string? ConfirmMessage { get; init; }

    public bool CanToggle => !RequiresAdmin || Shell.IsAdmin;

    bool _isOn;
    /// <summary>Bound to the toggle; the click handler applies or reverts, then calls <see cref="Refresh"/>.</summary>
    public bool IsOn { get => _isOn; set => SetField(ref _isOn, value); }

    string? _pendingNote;
    /// <summary>"Restart required" / "Sign out to apply" while a change made here hasn't taken effect yet; otherwise null.</summary>
    public string? PendingNote { get => _pendingNote; private set => SetField(ref _pendingNote, value); }

    public abstract bool IsApplied();
    protected abstract void ApplyCore();
    protected abstract void RevertCore();

    /// <summary>True when SpudBoost holds the original state and can undo this tweak.</summary>
    public abstract bool HasBackup { get; }

    string ChangedKey => "changed." + Id;

    public void Apply()
    {
        ApplyCore();
        MarkChanged();
    }

    public void Revert()
    {
        if (!HasBackup)
            throw new InvalidOperationException(
                $"\"{Name}\" was already set before SpudBoost changed it, so there is no original value to restore.");
        RevertCore();
        MarkChanged();
    }

    void MarkChanged()
    {
        if (TakesEffect == TakesEffect.Immediately) return;
        BackupStore.Data.Values[ChangedKey] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
        BackupStore.Save();
    }

    public void Refresh()
    {
        bool on;
        try { on = IsApplied(); }
        catch { on = false; }
        IsOn = on;
        PendingNote = ComputePendingNote();
    }

    string? ComputePendingNote()
    {
        if (TakesEffect == TakesEffect.Immediately ||
            !BackupStore.Data.Values.TryGetValue(ChangedKey, out var raw) ||
            !DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var changed))
            return null;

        return TakesEffect == TakesEffect.AfterRestart
            ? changed > BootTime ? "Restart required" : null
            : changed > SignInTime() ? "Sign out to apply" : null;
    }

    /// <summary>When the current Windows session started, approximated by the oldest explorer.exe.</summary>
    static DateTime SignInTime()
    {
        try
        {
            var shells = Process.GetProcessesByName("explorer");
            if (shells.Length > 0) return shells.Min(p => p.StartTime).ToUniversalTime();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
        return BootTime;
    }
}
