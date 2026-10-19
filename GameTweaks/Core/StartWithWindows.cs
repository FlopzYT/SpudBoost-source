using Microsoft.Win32;

namespace GameTweaks.Core;

/// <summary>The "start SpudBoost when I sign in" option (HKCU Run entry, starts hidden in the tray).</summary>
public static class StartWithWindows
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "SpudBoost";
    /// <summary>The entry's name before the app was renamed; moved over automatically.</summary>
    const string LegacyValueName = "GameTweaks";

    static string Command => $"\"{Environment.ProcessPath}\" {App.MinimizedArg}";

    public static bool Enabled
    {
        get => RegistryHelper.Read(RegistryHive.CurrentUser, RunKey, ValueName) is string;
        set
        {
            if (value)
                RegistryHelper.Write(new RegValue(RegistryHive.CurrentUser, RunKey, ValueName, RegistryValueKind.String, Command));
            else
            {
                using var root = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
                using var key = root.OpenSubKey(RunKey, true);
                key?.DeleteValue(ValueName, false);
            }
        }
    }

    /// <summary>Points the entry at this exe if it was moved (e.g. a new release in another folder).</summary>
    public static void RefreshPath()
    {
        if (RegistryHelper.Read(RegistryHive.CurrentUser, RunKey, LegacyValueName) is string)
        {
            using var root = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
            using var key = root.OpenSubKey(RunKey, true);
            key?.DeleteValue(LegacyValueName, false);
            Enabled = true;
        }
        if (Enabled && RegistryHelper.Read(RegistryHive.CurrentUser, RunKey, ValueName) as string != Command)
            Enabled = true;
    }
}
