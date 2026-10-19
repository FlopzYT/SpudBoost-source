using System.Net.NetworkInformation;

namespace GameTweaks.Core;

public sealed record PingResult(string Region, string Summary, double Average, bool Best);

/// <summary>Pings Fortnite's per-region latency endpoints to find the best matchmaking region.</summary>
public static class PingTester
{
    static readonly (string Name, string Host)[] Regions =
    [
        ("NA-East", "ping-nae.ds.on.epicgames.com"),
        ("NA-Central", "ping-nac.ds.on.epicgames.com"),
        ("NA-West", "ping-naw.ds.on.epicgames.com"),
        ("Europe", "ping-eu.ds.on.epicgames.com"),
        ("Brazil", "ping-br.ds.on.epicgames.com"),
        ("Asia", "ping-asia.ds.on.epicgames.com"),
        ("Oceania", "ping-oce.ds.on.epicgames.com"),
        ("Middle East", "ping-me.ds.on.epicgames.com"),
    ];

    public static IEnumerable<string> RegionNames => Regions.Select(r => r.Name);

    /// <summary>The ping endpoint for a region name (falls back to NA-East).</summary>
    public static string HostFor(string region) => Regions.FirstOrDefault(r => r.Name == region).Host ?? Regions[0].Host;

    public static async Task<List<PingResult>> RunAsync(int count = 15)
    {
        var results = await Task.WhenAll(Regions.Select(r => Measure(r.Name, r.Host, count)));
        var reachable = results.Where(r => r.Avg >= 0).ToList();
        var best = reachable.Count > 0 ? reachable.MinBy(r => r.Avg + r.Jitter).Name : null;
        return results
            .OrderBy(r => r.Avg < 0 ? double.MaxValue : r.Avg)
            .Select(r => new PingResult(r.Name, r.Summary, r.Avg, r.Name == best))
            .ToList();
    }

    static async Task<(string Name, string Summary, double Avg, double Jitter)> Measure(string name, string host, int count)
    {
        using var ping = new Ping();
        var times = new List<long>();
        var lost = 0;
        for (var i = 0; i < count; i++)
        {
            try
            {
                var reply = await ping.SendPingAsync(host, 1000);
                if (reply.Status == IPStatus.Success) times.Add(reply.RoundtripTime);
                else lost++;
            }
            catch (PingException) { lost++; }
            await Task.Delay(100);
        }

        if (times.Count == 0) return (name, "No reply", -1, 0);
        var jitter = times.Count > 1 ? times.Zip(times.Skip(1), (a, b) => Math.Abs(a - b)).Average() : 0;
        var avg = times.Average();
        var summary = $"{avg:0} ms avg · {times.Min()}–{times.Max()} ms · jitter {jitter:0.0} ms · {lost}/{count} lost";
        return (name, summary, avg, jitter);
    }
}
