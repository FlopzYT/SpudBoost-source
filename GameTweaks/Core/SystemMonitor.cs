using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

namespace GameTweaks.Core;

/// <summary>Live CPU, RAM and (on NVIDIA) GPU readings. Values are null when not available on this PC.</summary>
public sealed class SystemMonitor : ObservableObject
{
    [StructLayout(LayoutKind.Sequential)]
    struct FileTime { public uint Low, High; public ulong Value => ((ulong)High << 32) | Low; }

    [StructLayout(LayoutKind.Sequential)]
    struct MemoryStatus
    {
        public uint Length, MemoryLoad;
        public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll")] static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);
    [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);

    static readonly string NvidiaSmi = Path.Combine(Environment.SystemDirectory, "nvidia-smi.exe");
    public static bool HasNvidiaSmi => File.Exists(NvidiaSmi);

    ulong _lastIdle, _lastTotal;

    string _cpu = "…", _ram = "…", _ramDetail = "", _gpu = "n/a", _gpuTemp = "n/a", _gpuClock = "n/a", _vram = "n/a", _gpuPower = "n/a", _gpuName = "";
    public string Cpu { get => _cpu; private set => SetField(ref _cpu, value); }
    public string Ram { get => _ram; private set => SetField(ref _ram, value); }
    public string RamDetail { get => _ramDetail; private set => SetField(ref _ramDetail, value); }
    public string Gpu { get => _gpu; private set => SetField(ref _gpu, value); }
    public string GpuTemp { get => _gpuTemp; private set => SetField(ref _gpuTemp, value); }
    public string GpuClock { get => _gpuClock; private set => SetField(ref _gpuClock, value); }
    public string Vram { get => _vram; private set => SetField(ref _vram, value); }
    public string GpuPower { get => _gpuPower; private set => SetField(ref _gpuPower, value); }
    public string GpuName { get => _gpuName; private set => SetField(ref _gpuName, value); }

    /// <summary>Takes one reading. CPU % is measured since the previous call.</summary>
    public async Task UpdateAsync()
    {
        if (GetSystemTimes(out var idle, out var kernel, out var user))
        {
            var total = kernel.Value + user.Value; // kernel time includes idle time
            if (_lastTotal > 0 && total > _lastTotal)
            {
                var busy = 1.0 - (double)(idle.Value - _lastIdle) / (total - _lastTotal);
                Cpu = $"{Math.Clamp(busy, 0, 1):P0}";
            }
            _lastIdle = idle.Value;
            _lastTotal = total;
        }

        var mem = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (GlobalMemoryStatusEx(ref mem))
        {
            var used = (mem.TotalPhys - mem.AvailPhys) / (double)(1L << 30);
            Ram = $"{mem.MemoryLoad}%";
            RamDetail = $"{used:0.0} of {mem.TotalPhys / (double)(1L << 30):0} GB in use";
        }

        if (HasNvidiaSmi) await ReadNvidiaAsync();
    }

    async Task ReadNvidiaAsync()
    {
        var (code, output) = await Task.Run(() => Shell.Run(NvidiaSmi,
            "--query-gpu=name,utilization.gpu,temperature.gpu,clocks.gr,memory.used,memory.total,power.draw --format=csv,noheader,nounits"));
        if (code != 0) return;
        var parts = output.Split('\n')[0].Split(',').Select(p => p.Trim()).ToArray();
        if (parts.Length < 7) return;
        string Num(string s, string unit, string format = "0") =>
            double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v.ToString(format, CultureInfo.CurrentCulture) + unit : "n/a";
        GpuName = parts[0];
        Gpu = Num(parts[1], "%");
        GpuTemp = Num(parts[2], " °C");
        GpuClock = Num(parts[3], " MHz");
        Vram = double.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var usedMb) &&
               double.TryParse(parts[5], NumberStyles.Float, CultureInfo.InvariantCulture, out var totalMb)
            ? $"{usedMb / 1024:0.0} / {totalMb / 1024:0} GB" : "n/a";
        GpuPower = Num(parts[6], " W");
    }
}
