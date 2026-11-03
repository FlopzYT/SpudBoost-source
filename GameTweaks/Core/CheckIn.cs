using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace GameTweaks.Core;

/// <summary>
/// Anonymous daily check-in so the developer can count active users. Once a day it sends a random install ID
/// (generated on this PC, not tied to the person or the hardware) and the SpudBoost version. Nothing else.
/// Users can turn it off in Settings. The database only accepts new rows; nobody can read them with the app's key.
/// </summary>
public static class CheckIn
{
    const string Endpoint = "https://vizfsimjepggldfsixei.supabase.co/rest/v1/checkins";
    /// <summary>Supabase publishable key: can only insert check-ins (row-level security), never read them.</summary>
    const string PublishableKey = "sb_publishable_tBiCqwR5MJVIdkkamor-lA_7bAcFwAu";

    static bool _running;
    /// <summary>After a failed try (offline, server down), wait an hour before trying again.</summary>
    static DateTime _nextTry = DateTime.MinValue;

    /// <summary>Sends today's check-in if it's switched on and hasn't been sent today. Never throws.</summary>
    public static async Task SendIfDueAsync(AppSettings settings)
    {
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        if (!settings.UsageCheckIn || settings.LastCheckInDay == today || _running || DateTime.UtcNow < _nextTry) return;
        _running = true;
        _nextTry = DateTime.UtcNow.AddHours(1);
        try
        {
            if (settings.InstallId == Guid.Empty)
            {
                settings.InstallId = Guid.NewGuid();
                settings.Save();
            }
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
            request.Headers.Add("apikey", PublishableKey);
            request.Headers.Add("Prefer", "return=minimal");
            var body = JsonSerializer.Serialize(new { install_id = settings.InstallId, version = UpdateChecker.CurrentVersion.ToString() });
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            using var response = await http.SendAsync(request);
            // 201 = counted; 409 = already counted today (e.g. from another start). Either way, done for today.
            if (response.IsSuccessStatusCode || (int)response.StatusCode == 409)
            {
                settings.LastCheckInDay = today;
                settings.Save();
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { } // offline: try again later
        finally { _running = false; }
    }
}
