using System.Diagnostics;

namespace GameTweaks.Boost;

/// <summary>Closes apps from the user's list. Windows and security processes can never be closed, even if listed.</summary>
public static class ProcessCleaner
{
    static readonly HashSet<string> Protected = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Idle", "Registry", "smss", "csrss", "wininit", "winlogon", "services", "lsass", "lsaiso",
        "svchost", "dwm", "explorer", "fontdrvhost", "sihost", "ctfmon", "audiodg", "conhost", "taskhostw",
        "RuntimeBroker", "StartMenuExperienceHost", "ShellExperienceHost", "SearchHost", "TextInputHost",
        "SecurityHealthService", "SecurityHealthSystray", "MsMpEng", "NisSrv", "MpDefenderCoreService",
        "spoolsv", "dllhost", "WmiPrvSE", "Memory Compression", "MemCompression", "LsaIso", "SgrmBroker",
        "EasyAntiCheat", "EasyAntiCheat_EOS", "BEService", "vgc", "vgtray", "FortniteClient-Win64-Shipping",
        "SpudBoost", "GameTweaks", "nvcontainer", "NVDisplay.Container", "atiesrxx", "atieclxx",
    };

    public static bool IsProtected(string name) => Protected.Contains(name);

    /// <summary>Closes every running process whose name is in <paramref name="names"/>; returns the names closed.</summary>
    public static List<string> Close(IEnumerable<string> names)
    {
        var closed = new List<string>();
        foreach (var name in names.Where(n => !IsProtected(n)))
        foreach (var p in Process.GetProcessesByName(name))
        {
            try
            {
                // Windowed apps are only asked to close, never killed: they may be asking to save your work.
                if (p.MainWindowHandle != IntPtr.Zero)
                {
                    if (p.CloseMainWindow() && p.WaitForExit(1500)) closed.Add(name);
                    continue;
                }
                // Windowless background processes have nothing to save.
                p.Kill();
                closed.Add(name);
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
        }
        return closed.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}
