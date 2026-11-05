using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

namespace GameTweaks.Core;

/// <summary>
/// Live FPS for the stats overlay. Runs Intel PresentMon in the background, which watches Windows' own graphics
/// events (ETW) for every app. Nothing is injected into games, so it's safe with anti-cheat. Needs admin rights.
/// </summary>
public sealed class LiveFps : IDisposable
{
    const string SessionName = "SpudBoostLive";
    static readonly TimeSpan Window = TimeSpan.FromSeconds(2);

    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);

    readonly object _lock = new();
    // Recent frames per process: (when the frame arrived, milliseconds since that process's previous frame)
    readonly Dictionary<int, Queue<(DateTime At, double Ms)>> _frames = [];
    Process? _process;

    public bool Running => _process is { HasExited: false };

    /// <summary>Why FPS can't be shown, or null when it's working.</summary>
    public string? Problem { get; private set; }

    public void Start()
    {
        if (Running) return;
        if (!Shell.IsAdmin) { Problem = "needs admin"; return; }
        if (!Benchmark.Installed) { Problem = "PresentMon not downloaded"; return; }
        Problem = null;
        try
        {
            var info = new ProcessStartInfo(Benchmark.ExePath,
                $"--output_stdout --v1_metrics --no_console_stats --no_track_display --no_track_input --no_track_gpu " +
                $"--exclude SpudBoost.exe --session_name {SessionName} --stop_existing_session")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            var process = Process.Start(info)!;
            _process = process;
            ChildProcessJob.Add(process); // PresentMon closes with SpudBoost, even if SpudBoost crashes
            process.ErrorDataReceived += (_, _) => { };
            process.BeginErrorReadLine();
            _ = Task.Run(() => Read(process));
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Problem = "couldn't start PresentMon";
        }
    }

    void Read(Process process)
    {
        try
        {
            var header = process.StandardOutput.ReadLine()?.Split(',') ?? [];
            var pidColumn = Array.FindIndex(header, h => h.Equals("ProcessID", StringComparison.OrdinalIgnoreCase));
            var msColumn = Array.FindIndex(header, h => h.Equals("MsBetweenPresents", StringComparison.OrdinalIgnoreCase));
            if (pidColumn < 0 || msColumn < 0) return;

            while (process.StandardOutput.ReadLine() is { } line)
            {
                var cells = line.Split(',');
                if (cells.Length <= Math.Max(pidColumn, msColumn)) continue;
                if (!int.TryParse(cells[pidColumn], out var pid)) continue;
                if (!double.TryParse(cells[msColumn], NumberStyles.Float, CultureInfo.InvariantCulture, out var ms) || ms <= 0) continue;

                var now = DateTime.UtcNow;
                lock (_lock)
                {
                    if (!_frames.TryGetValue(pid, out var queue)) _frames[pid] = queue = new();
                    queue.Enqueue((now, ms));
                    while (queue.Count > 0 && now - queue.Peek().At > Window) queue.Dequeue();
                }
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ObjectDisposedException) { }
    }

    /// <summary>FPS and 1% low of the app in front over the last 2 seconds, or null if it isn't drawing frames.</summary>
    public (double Fps, double Low1)? ForegroundFps()
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out var pid);
        double[] times;
        lock (_lock)
        {
            var cutoff = DateTime.UtcNow - Window;
            foreach (var key in _frames.Keys.ToList())
            {
                var queue = _frames[key];
                while (queue.Count > 0 && queue.Peek().At < cutoff) queue.Dequeue();
                if (queue.Count == 0) _frames.Remove(key);
            }
            if (!_frames.TryGetValue((int)pid, out var frames) || frames.Count < 5) return null;
            times = frames.Select(f => f.Ms).ToArray();
        }
        var slowest = times.OrderByDescending(t => t).Take(Math.Max(1, times.Length / 100)).Average();
        return (1000.0 / times.Average(), 1000.0 / slowest);
    }

    public void Stop()
    {
        var process = _process;
        _process = null;
        if (process != null)
        {
            try { if (!process.HasExited) process.Kill(); } catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            process.Dispose();
            // Make sure the Windows trace session is gone too.
            _ = Task.Run(() => Shell.Run(Benchmark.ExePath, $"--session_name {SessionName} --terminate_existing_session"));
        }
        lock (_lock) _frames.Clear();
    }

    public void Dispose() => Stop();
}

/// <summary>Ties child processes to SpudBoost so Windows closes them when SpudBoost exits for any reason.</summary>
static class ChildProcessJob
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateJobObject(IntPtr attributes, string? name);
    [DllImport("kernel32.dll")] static extern bool SetInformationJobObject(IntPtr job, int infoClass, ref ExtendedLimitInformation info, int size);
    [DllImport("kernel32.dll")] static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [StructLayout(LayoutKind.Sequential)]
    struct BasicLimitInformation
    {
        public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass, SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct IoCounters { public ulong a, b, c, d, e, f; }

    [StructLayout(LayoutKind.Sequential)]
    struct ExtendedLimitInformation
    {
        public BasicLimitInformation Basic;
        public IoCounters Io;
        public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
    }

    static readonly Lazy<IntPtr> Job = new(() =>
    {
        var job = CreateJobObject(IntPtr.Zero, null);
        var info = new ExtendedLimitInformation { Basic = { LimitFlags = 0x2000 } }; // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
        SetInformationJobObject(job, 9, ref info, Marshal.SizeOf<ExtendedLimitInformation>());
        return job;
    });

    public static void Add(Process process)
    {
        try { AssignProcessToJobObject(Job.Value, process.Handle); } catch (InvalidOperationException) { }
    }
}
