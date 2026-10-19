using System.Text.RegularExpressions;
using GameTweaks.Core;

namespace GameTweaks.Tweaks;

/// <summary>Switches to the High performance power plan, re-creating it if Windows hid it.</summary>
public sealed partial class PowerPlanTweak : Tweak
{
    const string HighPerformance = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
    const string OriginalKey = "power.original";
    const string CreatedKey = "power.created";

    [GeneratedRegex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex GuidPattern();

    public override bool HasBackup => BackupStore.Data.Values.ContainsKey(OriginalKey);

    static string ActiveScheme()
    {
        var (_, output) = Shell.Run("powercfg", "/getactivescheme");
        return GuidPattern().Match(output).Value.ToLowerInvariant();
    }

    public override bool IsApplied()
    {
        var active = ActiveScheme();
        return active == HighPerformance ||
               (BackupStore.Data.Values.TryGetValue(CreatedKey, out var created) && active == created);
    }

    protected override void ApplyCore()
    {
        if (!HasBackup)
        {
            BackupStore.Data.Values[OriginalKey] = ActiveScheme();
            BackupStore.Save();
        }

        if (Shell.Run("powercfg", $"/setactive {HighPerformance}").ExitCode == 0) return;

        // Some PCs (Modern Standby laptops) hide the plan; duplicating it brings it back.
        var (code, output) = Shell.Run("powercfg", $"-duplicatescheme {HighPerformance}");
        var created = GuidPattern().Match(output).Value.ToLowerInvariant();
        if (code != 0 || created.Length == 0)
            throw new InvalidOperationException("This PC does not offer a High performance power plan.\n" + output);

        BackupStore.Data.Values[CreatedKey] = created;
        BackupStore.Save();
        Shell.Run("powercfg", $"/setactive {created}");
    }

    protected override void RevertCore()
    {
        var (code, output) = Shell.Run("powercfg", $"/setactive {BackupStore.Data.Values[OriginalKey]}");
        if (code != 0) throw new InvalidOperationException("Could not restore the previous power plan.\n" + output);
        BackupStore.Data.Values.Remove(OriginalKey);
        BackupStore.Save();
    }
}
