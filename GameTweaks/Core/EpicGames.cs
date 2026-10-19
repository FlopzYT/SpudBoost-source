using System.IO;
using System.Text.Json;

namespace GameTweaks.Core;

/// <summary>Finds where Epic Games titles are installed on this PC, from the Epic launcher's install manifests.</summary>
public static class EpicGames
{
    static readonly string ManifestFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Epic\EpicGamesLauncher\Data\Manifests");

    /// <summary>Full path to FortniteClient-Win64-Shipping.exe, or null if Fortnite isn't installed.</summary>
    public static string? FortniteExe { get; } = FindFortniteExe();

    static string? FindFortniteExe()
    {
        var candidates = new List<string>();
        if (Directory.Exists(ManifestFolder))
        {
            foreach (var file in Directory.GetFiles(ManifestFolder, "*.item"))
            {
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(file));
                    var root = doc.RootElement;
                    if (root.TryGetProperty("AppName", out var app) && app.GetString() == "Fortnite" &&
                        root.TryGetProperty("InstallLocation", out var location) && location.GetString() is { Length: > 0 } dir)
                        candidates.Add(dir);
                }
                catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { }
            }
        }
        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Epic Games\Fortnite"));

        return candidates
            .Select(dir => Path.Combine(dir, @"FortniteGame\Binaries\Win64\FortniteClient-Win64-Shipping.exe"))
            .FirstOrDefault(File.Exists);
    }
}
