using System.Globalization;
using Microsoft.Win32;

namespace GameTweaks.Core;

public sealed record RegValue(RegistryHive Hive, string Key, string Name, RegistryValueKind Kind, object Value);

public static class RegistryHelper
{
    static RegistryKey Root(RegistryHive hive) => RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);

    public static object? Read(RegistryHive hive, string key, string name)
    {
        using var root = Root(hive);
        using var k = root.OpenSubKey(key);
        return k?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
    }

    public static string[] SubKeyNames(RegistryHive hive, string key)
    {
        using var root = Root(hive);
        using var k = root.OpenSubKey(key);
        return k?.GetSubKeyNames() ?? [];
    }

    public static bool Matches(RegValue v) => Same(Read(v.Hive, v.Key, v.Name), v.Value);

    public static void Write(RegValue v)
    {
        using var root = Root(v.Hive);
        using var k = root.CreateSubKey(v.Key, true);
        k.SetValue(v.Name, v.Value, v.Kind);
    }

    public static RegistryBackup Capture(RegValue v)
    {
        using var root = Root(v.Hive);
        using var k = root.OpenSubKey(v.Key);
        var current = k?.GetValue(v.Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        var backup = new RegistryBackup { Hive = v.Hive.ToString(), Key = v.Key, Name = v.Name, Existed = current != null };
        if (current != null)
        {
            var kind = k!.GetValueKind(v.Name);
            backup.Kind = kind.ToString();
            backup.Value = Serialize(current, kind);
        }
        return backup;
    }

    public static void Restore(RegistryBackup b)
    {
        var hive = Enum.Parse<RegistryHive>(b.Hive);
        using var root = Root(hive);
        if (!b.Existed)
        {
            using var k = root.OpenSubKey(b.Key, true);
            k?.DeleteValue(b.Name, false);
            return;
        }
        var kind = Enum.Parse<RegistryValueKind>(b.Kind);
        using var key = root.CreateSubKey(b.Key, true);
        key.SetValue(b.Name, Deserialize(b.Value ?? "", kind), kind);
    }

    static bool Same(object? a, object? b) =>
        a != null && b != null &&
        string.Equals(Convert.ToString(a, CultureInfo.InvariantCulture), Convert.ToString(b, CultureInfo.InvariantCulture),
            StringComparison.OrdinalIgnoreCase);

    static string Serialize(object value, RegistryValueKind kind) => kind switch
    {
        RegistryValueKind.Binary => Convert.ToBase64String((byte[])value),
        RegistryValueKind.MultiString => string.Join("\0", (string[])value),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "",
    };

    static object Deserialize(string value, RegistryValueKind kind) => kind switch
    {
        RegistryValueKind.DWord => int.Parse(value, CultureInfo.InvariantCulture),
        RegistryValueKind.QWord => long.Parse(value, CultureInfo.InvariantCulture),
        RegistryValueKind.Binary => Convert.FromBase64String(value),
        RegistryValueKind.MultiString => value.Split('\0'),
        _ => value,
    };
}
