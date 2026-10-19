using GameTweaks.Core;

namespace GameTweaks.Tweaks;

/// <summary>
/// Turns off advanced network adapter properties (by display name) on every connected physical adapter,
/// remembering each original value. Names vary by vendor, so properties an adapter doesn't have are skipped.
/// </summary>
public sealed class AdapterPropertyTweak : Tweak
{
    /// <summary>Advanced property display names to switch to Off/Disabled.</summary>
    public required string[] Properties { get; init; }

    /// <summary>Also stop Windows powering the adapter down to save energy.</summary>
    public bool PowerManagement { get; init; }

    const string PowerProperty = "@power";
    string Prefix => $"nic.{Id}.";

    public override bool HasBackup => BackupStore.Data.Values.Keys.Any(k => k.StartsWith(Prefix));

    static string Quote(string s) => "'" + s.Replace("'", "''") + "'";

    /// <summary>Rows of (adapter, property, current value, off value) for every matching property.</summary>
    List<(string Adapter, string Property, string Current, string Off)> Query()
    {
        var script =
            "$names = @(" + string.Join(",", Properties.Select(Quote)) + ")\n" +
            "Get-NetAdapter -Physical | Where-Object Status -eq 'Up' | ForEach-Object {\n" +
            "  $a = $_.Name\n" +
            "  Get-NetAdapterAdvancedProperty -Name $a -ErrorAction SilentlyContinue | Where-Object { $names -contains $_.DisplayName } | ForEach-Object {\n" +
            "    $off = $_.ValidDisplayValues | Where-Object { $_ -match '^(Off|Disabled)$' } | Select-Object -First 1\n" +
            "    if ($off) { \"$a|$($_.DisplayName)|$($_.DisplayValue)|$off\" }\n" +
            "  }\n" +
            (PowerManagement
                ? "  $pm = Get-NetAdapterPowerManagement -Name $a -ErrorAction SilentlyContinue\n" +
                  "  if ($pm -and \"$($pm.AllowComputerToTurnOffDevice)\" -in 'Enabled','Disabled') { \"$a|" + PowerProperty + "|$($pm.AllowComputerToTurnOffDevice)|Disabled\" }\n"
                : "") +
            "}";
        var (code, output) = Shell.PowerShell(script);
        if (code != 0) throw new InvalidOperationException(output);
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(l => l.Split('|'))
            .Where(p => p.Length == 4)
            .Select(p => (p[0], p[1], p[2], p[3]))
            .ToList();
    }

    static string SetCommand(string adapter, string property, string value) => property == PowerProperty
        ? $"Set-NetAdapterPowerManagement -Name {Quote(adapter)} -AllowComputerToTurnOffDevice {value} -NoRestart"
        : $"Set-NetAdapterAdvancedProperty -Name {Quote(adapter)} -DisplayName {Quote(property)} -DisplayValue {Quote(value)} -NoRestart";

    /// <summary>Runs the changes, then restarts each touched adapter once (a few seconds offline).</summary>
    static void Run(IEnumerable<(string Adapter, string Command)> changes)
    {
        var list = changes.ToList();
        if (list.Count == 0) return;
        var script = string.Join("\n", list.Select(c => c.Command)) + "\n" +
                     string.Join("\n", list.Select(c => c.Adapter).Distinct().Select(a => $"Restart-NetAdapter -Name {Quote(a)}"));
        var (code, output) = Shell.PowerShell(script);
        if (code != 0) throw new InvalidOperationException(output);
    }

    public override bool IsApplied()
    {
        var rows = Query();
        return rows.Count > 0 && rows.All(r => string.Equals(r.Current, r.Off, StringComparison.OrdinalIgnoreCase));
    }

    protected override void ApplyCore()
    {
        var pending = Query().Where(r => !string.Equals(r.Current, r.Off, StringComparison.OrdinalIgnoreCase)).ToList();
        if (pending.Count == 0) return;

        foreach (var r in pending) BackupStore.Data.Values.TryAdd($"{Prefix}{r.Adapter}|{r.Property}", r.Current);
        BackupStore.Save();
        Run(pending.Select(r => (r.Adapter, SetCommand(r.Adapter, r.Property, r.Off))));
    }

    protected override void RevertCore()
    {
        var entries = BackupStore.Data.Values.Where(kv => kv.Key.StartsWith(Prefix)).ToList();
        Run(entries.Select(kv =>
        {
            var parts = kv.Key[Prefix.Length..].Split('|', 2);
            return (parts[0], SetCommand(parts[0], parts[1], kv.Value));
        }));
        foreach (var kv in entries) BackupStore.Data.Values.Remove(kv.Key);
        BackupStore.Save();
    }
}
