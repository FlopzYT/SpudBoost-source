using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows.Threading;

namespace GameTweaks.Core;

/// <summary>One play session: how long a game ran and how hard the PC worked.</summary>
public sealed class PlaySession
{
    public string Game { get; set; } = "";
    public DateTime Start { get; set; }
    public double Minutes { get; set; }
    public double? AvgCpu { get; set; }
    public double? AvgGpuLoad { get; set; }
    public double? PeakGpuTemp { get; set; }

    public string When => Start.Date == DateTime.Today ? $"Today {Start:h:mm tt}"
        : Start.Date == DateTime.Today.AddDays(-1) ? $"Yesterday {Start:h:mm tt}" : Start.ToString("ddd MMM d, h:mm tt");
    public string Length => SessionTracker.FormatMinutes(Minutes);
    public string Details => string.Join(" · ", new[]
    {
        AvgCpu is { } c ? $"CPU {c:0}%" : null,
        AvgGpuLoad is { } g ? $"GPU {g:0}%" : null,
        PeakGpuTemp is { } t ? $"peak {t:0} °C" : null,
    }.Where(s => s != null));
    public bool RanHot => PeakGpuTemp >= SessionTracker.HotGpuTemp;

    /// <summary>Background apps that used the most CPU during the session (name → average % of the whole CPU).</summary>
    public List<BackgroundHog> Hogs { get; set; } = [];
    public bool HasHogs => Hogs.Count > 0;
    public string HogsText => "Busiest background apps: " + string.Join(", ", Hogs.Select(h => $"{h.Name} {h.Cpu:0.#}%"));
}

public sealed class BackgroundHog
{
    public string Name { get; set; } = "";
    public double Cpu { get; set; }
}

/// <summary>
/// Notices when a game from the Game Boost list is running and records the session. Light on purpose:
/// CPU every 10 seconds, GPU (nvidia-smi) once a minute, and nothing at all while no game is running.
/// </summary>
public sealed class SessionTracker
{
    public const double HotGpuTemp = 85;
    const int MaxSessions = 300;
    static string FilePath => Path.Combine(BackupStore.Folder, "sessions.json");

    readonly AppSettings _settings;
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(10) };
    readonly SystemMonitor _monitor = new();

    string? _game;
    DateTime _start;
    int _ticks;
    readonly List<double> _cpu = [], _gpu = [];
    double? _peakTemp;
    bool _warned;

    public List<PlaySession> Sessions { get; } = Load();

    /// <summary>Raised once per session when the GPU reaches <see cref="HotGpuTemp"/>.</summary>
    public event Action<string, double>? GpuHot;
    /// <summary>Raised when a session ends (and was long enough to keep).</summary>
    public event Action<PlaySession>? SessionEnded;
    public event Action<string>? SessionStarted;

    public string? CurrentGame => _game;
    public DateTime CurrentStart => _start;

    public SessionTracker(AppSettings settings)
    {
        _settings = settings;
        _timer.Tick += async (_, _) => await TickAsync();
        _timer.Start();
    }

    async Task TickAsync()
    {
        if (_game == null)
        {
            var found = _settings.GameProcesses.FirstOrDefault(n => IsRunning(n));
            if (found == null) return;
            _game = found;
            _start = DateTime.Now;
            _ticks = 0;
            _cpu.Clear();
            _gpu.Clear();
            _peakTemp = null;
            _warned = false;
            await _monitor.UpdateAsync(includeGpu: false); // primes the CPU counter
            SessionStarted?.Invoke(found);
            return;
        }

        if (!IsRunning(_game))
        {
            End();
            return;
        }

        _ticks++;
        await Task.Run(SampleBackground);
        var readGpu = _ticks % 6 == 1;
        await _monitor.UpdateAsync(includeGpu: readGpu);
        if (_monitor.CpuPercent is { } cpu) _cpu.Add(cpu);
        if (!readGpu) return;
        if (_monitor.GpuLoadPercent is { } load) _gpu.Add(load);
        if (_monitor.GpuTempC is { } temp)
        {
            _peakTemp = Math.Max(_peakTemp ?? 0, temp);
            if (temp >= HotGpuTemp && !_warned)
            {
                _warned = true;
                GpuHot?.Invoke(_game, temp);
            }
        }
    }

    // ---- Background app spotter: CPU time per app between samples ----

    /// <summary>Never reported: the idle counter, Windows' own core and SpudBoost.</summary>
    static readonly HashSet<string> NotHogs = new(StringComparer.OrdinalIgnoreCase)
    {
        "Idle", "System", "Registry", "Memory Compression", "MemCompression", "Secure System", "dwm", "csrss", "audiodg",
        "SpudBoost", "GameTweaks", "smss", "wininit", "services", "lsass", "Interrupts",
    };

    Dictionary<int, (string Name, TimeSpan Cpu)> _lastCpu = [];
    DateTime _lastSample;
    readonly Dictionary<string, double> _hogSeconds = new(StringComparer.OrdinalIgnoreCase);
    double _sampledSeconds;

    void SampleBackground()
    {
        var now = DateTime.UtcNow;
        var current = new Dictionary<int, (string, TimeSpan)>();
        foreach (var p in System.Diagnostics.Process.GetProcesses())
        {
            using (p)
            {
                try { current[p.Id] = (p.ProcessName, p.TotalProcessorTime); }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { }
            }
        }
        if (_lastCpu.Count > 0)
        {
            var wall = (now - _lastSample).TotalSeconds * Environment.ProcessorCount;
            if (wall > 0)
            {
                foreach (var (pid, (name, cpu)) in current)
                {
                    if (NotHogs.Contains(name) || name.Equals(_game, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!_lastCpu.TryGetValue(pid, out var before) || before.Name != name) continue;
                    var used = (cpu - before.Cpu).TotalSeconds;
                    if (used > 0) _hogSeconds[name] = _hogSeconds.GetValueOrDefault(name) + used;
                }
                _sampledSeconds += wall;
            }
        }
        _lastCpu = current;
        _lastSample = now;
    }

    /// <summary>Top 3 background apps that averaged at least 1% of the whole CPU.</summary>
    List<BackgroundHog> TopHogs() =>
        _sampledSeconds <= 0 ? [] : _hogSeconds
            .Select(kv => new BackgroundHog { Name = kv.Key, Cpu = Math.Round(100 * kv.Value / _sampledSeconds, 1) })
            .Where(h => h.Cpu >= 1)
            .OrderByDescending(h => h.Cpu)
            .Take(3)
            .ToList();

    static bool IsRunning(string name)
    {
        var procs = Process.GetProcessesByName(name);
        foreach (var p in procs) p.Dispose();
        return procs.Length > 0;
    }

    void End()
    {
        var session = new PlaySession
        {
            Game = _game!,
            Start = _start,
            Minutes = Math.Round((DateTime.Now - _start).TotalMinutes, 1),
            AvgCpu = _cpu.Count > 0 ? _cpu.Average() : null,
            AvgGpuLoad = _gpu.Count > 0 ? _gpu.Average() : null,
            PeakGpuTemp = _peakTemp,
            Hogs = TopHogs(),
        };
        _lastCpu = [];
        _hogSeconds.Clear();
        _sampledSeconds = 0;
        _game = null;
        if (session.Minutes < 1) return; // a quick open-and-close isn't a session
        Sessions.Insert(0, session);
        if (Sessions.Count > MaxSessions) Sessions.RemoveRange(MaxSessions, Sessions.Count - MaxSessions);
        Save();
        SessionEnded?.Invoke(session);
    }

    /// <summary>Saves the session in progress (on exit).</summary>
    public void Shutdown()
    {
        _timer.Stop();
        if (_game != null) End();
    }

    public void Clear()
    {
        Sessions.Clear();
        Save();
    }

    public double MinutesSince(DateTime since) =>
        Sessions.Where(s => s.Start >= since).Sum(s => s.Minutes) +
        (_game != null && _start >= since ? (DateTime.Now - _start).TotalMinutes : 0);

    public static string FormatMinutes(double minutes) =>
        minutes < 60 ? $"{minutes:0} min" : $"{(int)(minutes / 60)} h {minutes % 60:00} min";

    static List<PlaySession> Load()
    {
        try
        {
            if (File.Exists(FilePath)) return JsonSerializer.Deserialize<List<PlaySession>>(File.ReadAllText(FilePath)) ?? [];
        }
        catch (Exception ex) when (ex is JsonException or IOException) { }
        return [];
    }

    void Save()
    {
        try
        {
            Directory.CreateDirectory(BackupStore.Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Sessions));
        }
        catch (IOException) { }
    }
}
