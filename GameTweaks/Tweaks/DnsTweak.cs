using GameTweaks.Core;
using Microsoft.Win32;

namespace GameTweaks.Tweaks;

/// <summary>Points every connected physical adapter at a public DNS provider (IPv4).</summary>
public sealed class DnsTweak : Tweak
{
    public static readonly IReadOnlyDictionary<string, string[]> Providers = new Dictionary<string, string[]>
    {
        ["Cloudflare"] = ["1.1.1.1", "1.0.0.1"],
        ["Google"] = ["8.8.8.8", "8.8.4.4"],
        ["Quad9"] = ["9.9.9.9", "149.112.112.112"],
    };

    const string Prefix = "dns.";
    const string InterfacesKey = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces";

    public string Provider { get; set; } = "Cloudflare";

    public override bool HasBackup => BackupStore.Data.Values.Keys.Any(k => k.StartsWith(Prefix));

    /// <summary>Connected physical adapters as (ifIndex, interface GUID).</summary>
    static List<(string Index, string Guid)> ActiveAdapters()
    {
        var (code, output) = Shell.PowerShell(
            "Get-NetAdapter -Physical | Where-Object Status -eq 'Up' | ForEach-Object { \"$($_.ifIndex)|$($_.InterfaceGuid)\" }");
        if (code != 0) throw new InvalidOperationException(output);
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(l => l.Split('|'))
            .Where(p => p.Length == 2)
            .Select(p => (p[0], p[1]))
            .ToList();
    }

    /// <summary>Static DNS servers set on an adapter; empty means it gets DNS from DHCP.</summary>
    static string StaticServers(string guid) =>
        RegistryHelper.Read(RegistryHive.LocalMachine, $@"{InterfacesKey}\{guid}", "NameServer") as string ?? "";

    static string Normalize(string servers) =>
        string.Join(",", servers.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries));

    public override bool IsApplied()
    {
        var wanted = string.Join(",", Providers[Provider]);
        var adapters = ActiveAdapters();
        return adapters.Count > 0 && adapters.All(a => Normalize(StaticServers(a.Guid)) == wanted);
    }

    protected override void ApplyCore()
    {
        var adapters = ActiveAdapters();
        if (adapters.Count == 0) throw new InvalidOperationException("No connected network adapters were found.");

        foreach (var (index, guid) in adapters)
            BackupStore.Data.Values.TryAdd(Prefix + index, Normalize(StaticServers(guid)));
        BackupStore.Save();

        var servers = string.Join(",", Providers[Provider].Select(s => $"'{s}'"));
        var script = string.Join("\n", adapters.Select(a =>
            $"Set-DnsClientServerAddress -InterfaceIndex {a.Index} -ServerAddresses @({servers})"));
        Run(script + "\nClear-DnsClientCache");
    }

    protected override void RevertCore()
    {
        var entries = BackupStore.Data.Values.Where(kv => kv.Key.StartsWith(Prefix)).ToList();
        var script = string.Join("\n", entries.Select(kv =>
        {
            var index = kv.Key[Prefix.Length..];
            return kv.Value.Length == 0
                ? $"Set-DnsClientServerAddress -InterfaceIndex {index} -ResetServerAddresses"
                : $"Set-DnsClientServerAddress -InterfaceIndex {index} -ServerAddresses @({string.Join(",", kv.Value.Split(',').Select(s => $"'{s}'"))})";
        }));
        Run(script + "\nClear-DnsClientCache");

        foreach (var kv in entries) BackupStore.Data.Values.Remove(kv.Key);
        BackupStore.Save();
    }

    static void Run(string script)
    {
        var (code, output) = Shell.PowerShell(script);
        if (code != 0) throw new InvalidOperationException(output);
    }
}
