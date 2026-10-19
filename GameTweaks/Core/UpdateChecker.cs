using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace GameTweaks.Core;

public sealed record UpdateInfo(Version Version, string PageUrl, string? InstallerUrl);

/// <summary>Checks GitHub Releases for a newer version and installs it in one click.</summary>
public static class UpdateChecker
{
    /// <summary>The GitHub repository releases are published to, as "owner/name". Empty = update checks off.</summary>
    public const string Repository = "lincman6000/SpudBoost";

    public static Version CurrentVersion { get; } =
        Assembly.GetExecutingAssembly().GetName().Version is { } v ? new Version(v.Major, v.Minor, v.Build) : new Version(1, 0, 0);

    public static bool Configured => Repository.Length > 0;

    static HttpClient NewClient(TimeSpan timeout)
    {
        var http = new HttpClient { Timeout = timeout };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"SpudBoost/{CurrentVersion}");
        return http;
    }

    /// <summary>Returns the newer release, or null when up to date (or not configured).</summary>
    public static async Task<UpdateInfo?> CheckAsync()
    {
        if (!Configured) return null;
        using var http = NewClient(TimeSpan.FromSeconds(15));
        using var doc = JsonDocument.Parse(await http.GetStringAsync($"https://api.github.com/repos/{Repository}/releases/latest"));
        var root = doc.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        var page = root.GetProperty("html_url").GetString() ?? $"https://github.com/{Repository}/releases";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var latest) || latest <= CurrentVersion) return null;

        // The installer asset is named SpudBoost-Setup-x.y.z.exe
        string? installer = null;
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? "";
            if (name.Contains("-Setup-", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                installer = asset.GetProperty("browser_download_url").GetString();
        }
        return new UpdateInfo(latest, page, installer);
    }

    /// <summary>
    /// Downloads the new installer and starts it in silent update mode. The installer closes this copy,
    /// installs over it (keeping all settings) and reopens SpudBoost. Returns false if there's no installer to download.
    /// </summary>
    public static async Task<bool> DownloadAndInstallAsync(UpdateInfo update, IProgress<int>? progress = null)
    {
        if (update.InstallerUrl == null) return false;
        var path = Path.Combine(Path.GetTempPath(), $"SpudBoost-Setup-{update.Version}.exe");

        using (var http = NewClient(TimeSpan.FromMinutes(10)))
        using (var response = await http.GetAsync(update.InstallerUrl, HttpCompletionOption.ResponseHeadersRead))
        {
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? 0;
            await using var source = await response.Content.ReadAsStreamAsync();
            await using var target = File.Create(path);
            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await source.ReadAsync(buffer)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read));
                done += read;
                if (total > 0) progress?.Report((int)(done * 100 / total));
            }
        }

        Process.Start(new ProcessStartInfo(path, "/SILENT /SUPPRESSMSGBOXES /NORESTART") { UseShellExecute = true });
        return true;
    }
}
