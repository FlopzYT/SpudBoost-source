using System.Runtime.InteropServices;

namespace GameTweaks.Core;

/// <summary>
/// Frees RAM that Windows keeps as "standby" cache. Windows normally hands that memory out when needed, but on PCs
/// with little RAM a full standby list can cause stutter while games load new areas. Purging it is the same thing
/// tools like ISLC and RAMMap do. Nothing running is touched (working sets aren't emptied). Needs administrator rights.
/// </summary>
public static class MemoryCleaner
{
    const int SystemMemoryListInformation = 80;
    const int MemoryPurgeStandbyList = 4;

    [StructLayout(LayoutKind.Sequential)]
    struct MemoryListInfo
    {
        public nuint ZeroPageCount, FreePageCount, ModifiedPageCount, ModifiedNoWritePageCount, BadPageCount;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public nuint[] PageCountByPriority;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public nuint[] RepurposedPagesByPriority;
        public nuint ModifiedPageCountPageFile;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct TokenPrivileges
    {
        public int Count;
        public long Luid;
        public int Attributes;
    }

    [DllImport("ntdll.dll")] static extern int NtQuerySystemInformation(int infoClass, ref MemoryListInfo info, int length, out int returned);
    [DllImport("ntdll.dll")] static extern int NtSetSystemInformation(int infoClass, ref int command, int length);
    [DllImport("advapi32.dll", SetLastError = true)] static extern bool OpenProcessToken(IntPtr process, int access, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern bool LookupPrivilegeValue(string? system, string name, out long luid);
    [DllImport("advapi32.dll", SetLastError = true)] static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, ref TokenPrivileges state, int length, IntPtr previous, IntPtr returned);
    [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);

    static bool _privilege;

    /// <summary>The privilege Windows requires for reading and purging the memory lists (admins have it, switched off).</summary>
    static bool EnablePrivilege()
    {
        if (_privilege) return true;
        if (!OpenProcessToken(GetCurrentProcess(), 0x0020 | 0x0008, out var token)) return false; // ADJUST_PRIVILEGES | QUERY
        try
        {
            if (!LookupPrivilegeValue(null, "SeProfileSingleProcessPrivilege", out var luid)) return false;
            var tp = new TokenPrivileges { Count = 1, Luid = luid, Attributes = 2 }; // SE_PRIVILEGE_ENABLED
            _privilege = AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero) && Marshal.GetLastWin32Error() == 0;
            return _privilege;
        }
        finally { CloseHandle(token); }
    }

    /// <summary>Free and standby memory in MB, or null when it can't be read (not admin).</summary>
    public static (long FreeMb, long StandbyMb)? Query()
    {
        if (!Shell.IsAdmin || !EnablePrivilege()) return null;
        var info = new MemoryListInfo { PageCountByPriority = new nuint[8], RepurposedPagesByPriority = new nuint[8] };
        if (NtQuerySystemInformation(SystemMemoryListInformation, ref info, Marshal.SizeOf<MemoryListInfo>(), out _) != 0) return null;
        long page = Environment.SystemPageSize;
        long Mb(ulong pages) => (long)(pages * (ulong)page / (1024 * 1024));
        ulong standby = 0;
        foreach (var p in info.PageCountByPriority) standby += p;
        return (Mb(info.ZeroPageCount + info.FreePageCount), Mb(standby));
    }

    /// <summary>Purges the standby list. Returns how many MB became free, or -1 if it isn't possible.</summary>
    public static long FreeStandby()
    {
        var before = Query();
        if (before == null) return -1;
        var command = MemoryPurgeStandbyList;
        if (NtSetSystemInformation(SystemMemoryListInformation, ref command, sizeof(int)) != 0) return -1;
        var after = Query();
        return after == null ? 0 : Math.Max(0, after.Value.FreeMb - before.Value.FreeMb);
    }

    /// <summary>Auto mode rule (like ISLC): only when free memory is low and there's a big standby cache to give back.</summary>
    public static bool ShouldAutoFree((long FreeMb, long StandbyMb) m) => m.FreeMb < 1024 && m.StandbyMb > 1024;
}
