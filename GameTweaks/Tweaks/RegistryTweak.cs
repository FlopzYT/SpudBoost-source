using GameTweaks.Core;

namespace GameTweaks.Tweaks;

/// <summary>A tweak made of one or more registry values; originals are captured before the first write.</summary>
public sealed class RegistryTweak : Tweak
{
    public required IReadOnlyList<RegValue> Values { get; init; }

    /// <summary>Optional command to run after writing or restoring (e.g. refresh a setting).</summary>
    public Action? AfterChange { get; init; }

    public override bool HasBackup => BackupStore.Data.Registry.ContainsKey(Id);

    public override bool IsApplied() => Values.Count > 0 && Values.All(RegistryHelper.Matches);

    protected override void ApplyCore()
    {
        if (Values.Count == 0)
            throw new InvalidOperationException("Nothing to change on this PC (no matching devices were found).");

        if (!HasBackup)
        {
            BackupStore.Data.Registry[Id] = Values.Select(RegistryHelper.Capture).ToList();
            BackupStore.Save();
        }
        foreach (var v in Values) RegistryHelper.Write(v);
        AfterChange?.Invoke();
    }

    protected override void RevertCore()
    {
        foreach (var b in BackupStore.Data.Registry[Id]) RegistryHelper.Restore(b);
        BackupStore.Data.Registry.Remove(Id);
        BackupStore.Save();
        AfterChange?.Invoke();
    }
}
