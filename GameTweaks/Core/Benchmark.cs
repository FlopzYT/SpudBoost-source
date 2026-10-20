using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace GameTweaks.Core;

public sealed record BenchmarkResult(
    DateTime Date, string Game, string Label, int Seconds, int Frames, double AvgFps, double Low1, double Low01)
{
    public string Summary => $"{AvgFps:0} avg · {Low1:0} 1% low · {Low01:0} 0.1% low";
    public string When => Date.ToString("MMM d, h:mm tt", CultureInfo.CurrentCulture);

    /// <summary>Change versus the first run of the same game, e.g. "+12% avg · +8% 1% low vs Before tweaks".</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string Comparison { get; set; } = "";
}

/// <summary>
/// Measures real in-game FPS with Intel PresentMon (open source, MIT), which records every frame a game presents.
/// PresentMon is downloaded on first use.
/// </summary>
public static class Benchmark
{
    const string Version = "2.6.0";
    const string DownloadUrl = $"https://github.com/GameTechDev/PresentMon/releases/download/v{Version}/PresentMon-{Version}-x64.exe";

    public static string ExePath { get; } = Path.Combine(BackupStore.Folder, "Tools", "PresentMon", "PresentMon.exe");
    static string HistoryPath => Path.Combine(BackupStore.Folder, "benchmarks.json");

    public static bool Installed => File.Exists(ExePath);

    public static async Task DownloadAsync()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ExePath)!);
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("SpudBoost");
        var bytes = await http.GetByteArrayAsync(DownloadUrl);
        await File.WriteAllBytesAsync(ExePath, bytes);
    }

    /// <summary>Records <paramref name="seconds"/> of frames from the running game and returns the stats.</summary>
    public static async Task<BenchmarkResult> RunAsync(string processName, string label, int seconds)
    {
        if (!Shell.IsAdmin)
            throw new InvalidOperationException("Measuring FPS needs administrator rights. Use \"Run as admin\" first.");
        if (Process.GetProcessesByName(processName).Length == 0)
            throw new InvalidOperationException($"{processName} isn't running. Start the game and get into a match first.");

        var csv = Path.Combine(Path.GetTempPath(), $"SpudBoost-bench-{Guid.NewGuid():N}.csv");
        try
        {
            var args = $"--process_name \"{processName}.exe\" --output_file \"{csv}\" --timed {seconds} --terminate_after_timed " +
                       "--v1_metrics --no_console_stats --stop_existing_session --session_name SpudBoostBench";
            var (code, output) = await Task.Run(() => Shell.Run(ExePath, args));
            if (!File.Exists(csv))
                throw new InvalidOperationException($"PresentMon didn't record any frames (exit code {code}).\n{output}");

            var frameTimes = ReadFrameTimes(csv);
            if (frameTimes.Count < 30)
                throw new InvalidOperationException("Too few frames were recorded. Make sure the game is in focus and not minimized while measuring.");

            var result = Compute(processName, label, seconds, frameTimes);
            Save(result);
            return result;
        }
        finally
        {
            try { File.Delete(csv); } catch (IOException) { }
        }
    }

    static List<double> ReadFrameTimes(string csv)
    {
        using var reader = new StreamReader(csv);
        var header = reader.ReadLine()?.Split(',') ?? [];
        var column = Array.FindIndex(header, h => h.Equals("MsBetweenPresents", StringComparison.OrdinalIgnoreCase));
        if (column < 0) throw new InvalidOperationException("Unexpected PresentMon output (no MsBetweenPresents column).");

        var times = new List<double>();
        while (reader.ReadLine() is { } line)
        {
            var cells = line.Split(',');
            if (cells.Length > column &&
                double.TryParse(cells[column], NumberStyles.Float, CultureInfo.InvariantCulture, out var ms) && ms > 0)
                times.Add(ms);
        }
        return times;
    }

    /// <summary>Average FPS, plus "1% low" = FPS averaged over the slowest 1% of frames (same for 0.1%).</summary>
    static BenchmarkResult Compute(string game, string label, int seconds, List<double> frameTimes)
    {
        var slowestFirst = frameTimes.OrderByDescending(t => t).ToList();
        double LowFps(double fraction) => 1000.0 / slowestFirst.Take(Math.Max(1, (int)(slowestFirst.Count * fraction))).Average();
        return new BenchmarkResult(DateTime.Now, game, label, seconds, frameTimes.Count,
            1000.0 / frameTimes.Average(), LowFps(0.01), LowFps(0.001));
    }

    public static List<BenchmarkResult> History()
    {
        var results = Load();
        // Newest first; each game's oldest run is its baseline.
        foreach (var result in results)
        {
            var baseline = results.LastOrDefault(r => r.Game == result.Game);
            if (baseline == null || ReferenceEquals(baseline, result)) continue;
            static string Pct(double now, double before) => before <= 0 ? "?" : $"{(now / before - 1) * 100:+0;-0;0}%";
            result.Comparison = $"{Pct(result.AvgFps, baseline.AvgFps)} avg · {Pct(result.Low1, baseline.Low1)} 1% low vs \"{baseline.Label}\"";
        }
        return results;
    }

    static List<BenchmarkResult> Load()
    {
        try
        {
            if (File.Exists(HistoryPath))
                return JsonSerializer.Deserialize<List<BenchmarkResult>>(File.ReadAllText(HistoryPath)) ?? [];
        }
        catch (JsonException) { }
        return [];
    }

    static void Save(BenchmarkResult result)
    {
        var all = Load();
        all.Insert(0, result);
        Directory.CreateDirectory(BackupStore.Folder);
        File.WriteAllText(HistoryPath, JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static void ClearHistory()
    {
        if (File.Exists(HistoryPath)) File.Delete(HistoryPath);
    }
}
