using System.IO;
using Microsoft.Win32;

namespace GameTweaks.Core;

/// <summary>One program that launches when you sign in.</summary>
public sealed class StartupEntry : ObservableObject
{
    public required string Name { get; init; }
    public required string Command { get; init; }
    public required string Scope { get; init; }
    public required RegistryHive ApprovedHive { get; init; }
    public required string ApprovedKey { get; init; }
    public bool Protected { get; init; }
    public bool Heavy { get; init; }

    public bool CanToggle => !Protected && (ApprovedHive == RegistryHive.CurrentUser || Shell.IsAdmin);

    bool _enabled;
    public bool Enabled { get => _enabled; set => SetField(ref _enabled, value); }
}

/// <summary>
/// Lists and toggles startup programs the same way Task Manager's Startup tab does (the StartupApproved keys),
/// so nothing is deleted and every change can also be undone from Task Manager.
/// </summary>
public static class StartupManager
{
    const string Approved = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved";

    static readonly string[] ProtectedNames = ["SecurityHealth", "Windows Defender", "RtkAudUService", "SpudBoost", "GameTweaks"];

    /// <summary>Known to use noticeable CPU, memory or GPU while running in the background.</summary>
    static readonly string[] HeavyNames =
    [
        "SignalRgb", "Medal", "VoicemodV3", "DubbingAI", "Opera GX", "FilmoraTray", "Lunar Client", "RobloxPlayerBeta",
        "MicrosoftEdgeAutoLaunch", "iCUE", "Corsair", "NZXT", "Razer", "Wallpaper", "Lively", "OneDrive", "Teams", "Spotify",
        "Overwolf", "Outplayed", "Insights", "Adobe", "CCXProcess", "Dropbox", "Google Drive", "Opera Browser Assistant",
    ];

    static bool Matches(string name, IEnumerable<string> list) =>
        list.Any(n => name.StartsWith(n, StringComparison.OrdinalIgnoreCase));

    public static List<StartupEntry> List()
    {
        var entries = new List<StartupEntry>();
        AddRunKey(entries, RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", "Run", "You");
        AddRunKey(entries, RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "Run", "All users");
        AddRunKey(entries, RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", "Run32", "All users");
        AddFolder(entries, Environment.GetFolderPath(Environment.SpecialFolder.Startup), RegistryHive.CurrentUser, "You");
        AddFolder(entries, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), RegistryHive.LocalMachine, "All users");
        return entries.OrderByDescending(e => e.Heavy).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    static void AddRunKey(List<StartupEntry> list, RegistryHive hive, string runKey, string approvedSub, string scope)
    {
        using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        using var key = root.OpenSubKey(runKey);
        if (key == null) return;
        foreach (var name in key.GetValueNames().Where(n => n.Length > 0))
            list.Add(Create(name, key.GetValue(name)?.ToString() ?? "", scope, hive, $@"{Approved}\{approvedSub}"));
    }

    static void AddFolder(List<StartupEntry> list, string folder, RegistryHive hive, string scope)
    {
        if (!Directory.Exists(folder)) return;
        foreach (var file in Directory.GetFiles(folder).Where(f => !f.EndsWith("desktop.ini", StringComparison.OrdinalIgnoreCase)))
            list.Add(Create(Path.GetFileName(file), file, scope, hive, $@"{Approved}\StartupFolder"));
    }

    static StartupEntry Create(string name, string command, string scope, RegistryHive hive, string approvedKey)
    {
        var entry = new StartupEntry
        {
            Name = name,
            Command = command,
            Scope = scope,
            ApprovedHive = hive,
            ApprovedKey = approvedKey,
            Protected = Matches(name, ProtectedNames),
            Heavy = Matches(name, HeavyNames),
        };
        // Low bit set in the first byte means disabled (0x03/0x07); missing means enabled.
        entry.Enabled = RegistryHelper.Read(hive, approvedKey, name) is not byte[] { Length: > 0 } data || (data[0] & 1) == 0;
        return entry;
    }

    /// <summary>
    /// Turns off enabled startup entries whose name or program matches one of <paramref name="names"/> (e.g. "OneDrive").
    /// Protected entries and ones that need admin rights we don't have are skipped. Returns the entries turned off.
    /// </summary>
    public static List<string> DisableMatching(IEnumerable<string> names)
    {
        var list = names.Where(n => n.Length >= 3).ToList();
        var disabled = new List<string>();
        foreach (var entry in List().Where(e => e.Enabled && e.CanToggle))
        {
            var exe = ExeName(entry.Command);
            if (!list.Any(n => entry.Name.StartsWith(n, StringComparison.OrdinalIgnoreCase) ||
                               exe.Equals(n, StringComparison.OrdinalIgnoreCase))) continue;
            try
            {
                SetEnabled(entry, false);
                disabled.Add(entry.Name);
            }
            catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or System.Security.SecurityException) { }
        }
        return disabled;
    }

    /// <summary>Program name without folder or .exe from a startup command line.</summary>
    static string ExeName(string command)
    {
        var c = command.Trim();
        var path = c.StartsWith('"') ? c[1..].Split('"')[0] : c.Split(".exe", StringSplitOptions.None)[0] + ".exe";
        try { return Path.GetFileNameWithoutExtension(path); }
        catch (ArgumentException) { return ""; }
    }

    public static void SetEnabled(StartupEntry entry, bool enabled)
    {
        if (entry.Protected) throw new InvalidOperationException($"{entry.Name} is needed by Windows and can't be disabled here.");
        var data = new byte[12];
        data[0] = enabled ? (byte)0x02 : (byte)0x03;
        if (!enabled) BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(data, 4);
        RegistryHelper.Write(new RegValue(entry.ApprovedHive, entry.ApprovedKey, entry.Name, RegistryValueKind.Binary, data));
        entry.Enabled = enabled;
    }
}
