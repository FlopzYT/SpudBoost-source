using GameTweaks.Core;

namespace GameTweaks.Tweaks;

/// <summary>
/// A tweak driven by PowerShell commands, for settings that aren't plain registry values.
/// Reverting restores the Windows default, which is only offered when GameTweaks made the change.
/// </summary>
public sealed class ScriptTweak : Tweak
{
    /// <summary>Must print True when the tweak is in effect.</summary>
    public required string CheckScript { get; init; }
    public required string ApplyScript { get; init; }
    public required string RevertScript { get; init; }

    string Key => "script." + Id;

    public override bool HasBackup => BackupStore.Data.Values.ContainsKey(Key);

    public override bool IsApplied()
    {
        var (code, output) = Shell.PowerShell(CheckScript);
        return code == 0 && output.Trim().Equals("True", StringComparison.OrdinalIgnoreCase);
    }

    protected override void ApplyCore()
    {
        Run(ApplyScript);
        BackupStore.Data.Values[Key] = "applied";
        BackupStore.Save();
    }

    protected override void RevertCore()
    {
        Run(RevertScript);
        BackupStore.Data.Values.Remove(Key);
        BackupStore.Save();
    }

    static void Run(string script)
    {
        var (code, output) = Shell.PowerShell(script);
        if (code != 0) throw new InvalidOperationException(output);
    }
}
