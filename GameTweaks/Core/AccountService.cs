using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace GameTweaks.Core;

/// <summary>A problem the user can understand (wrong password, no internet, email not confirmed...).</summary>
public sealed class AccountException(string message) : Exception(message);

/// <summary>What's remembered between launches. Saved encrypted for this Windows user only (DPAPI); the password never is.</summary>
public sealed class SavedSession
{
    public string RefreshToken { get; set; } = "";
    public string UserId { get; set; } = "";
    public string Email { get; set; } = "";
    public string Username { get; set; } = "";
    public bool Premium { get; set; }
    public DateTime? PremiumUntil { get; set; }
    /// <summary>Last time premium was confirmed with the server.</summary>
    public DateTime PremiumCheckedAt { get; set; }
}

/// <summary>
/// SpudBoost accounts on Supabase: sign up, sign in, password reset, premium status and cloud save.
/// Talks to Supabase's standard REST endpoints over HTTPS; see ACCOUNTS-SETUP.md for the database side.
/// </summary>
public sealed class AccountService : ObservableObject
{
    /// <summary>Without internet, a premium account keeps premium for this long after the last successful check.</summary>
    static readonly TimeSpan OfflineGrace = TimeSpan.FromDays(3);

    static string SessionFile => Path.Combine(BackupStore.Folder, "account.dat");
    static readonly byte[] Entropy = Encoding.UTF8.GetBytes("SpudBoost.Account.v1");

    readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
    SavedSession? _session;
    string? _accessToken;
    DateTime _accessExpires;

    public AccountService()
    {
        _session = LoadSession();
    }

    public bool Configured => AccountConfig.Configured;
    public bool IsSignedIn => _session != null;
    public string Username => _session?.Username ?? "";
    public string Email => _session?.Email ?? "";
    public string UserId => _session?.UserId ?? "";

    /// <summary>
    /// Premium as last confirmed by the server. Only the server (Stripe webhook) can grant it; offline it holds for
    /// a few days so a dropped connection doesn't lock anyone out mid-game.
    /// </summary>
    public bool IsPremium => _session is { Premium: true } s &&
                             (s.PremiumUntil == null || s.PremiumUntil > DateTime.UtcNow) &&
                             DateTime.UtcNow - s.PremiumCheckedAt < OfflineGrace;

    public DateTime? PremiumUntil => _session?.PremiumUntil;

    void Changed()
    {
        OnPropertyChanged(nameof(IsSignedIn));
        OnPropertyChanged(nameof(Username));
        OnPropertyChanged(nameof(Email));
        OnPropertyChanged(nameof(IsPremium));
        OnPropertyChanged(nameof(PremiumUntil));
    }

    // ---- Sign up / sign in ----

    static readonly Regex UsernamePattern = new("^[A-Za-z0-9_]{3,20}$");

    /// <summary>Returns a message when the account needs email confirmation first; null when signed straight in.</summary>
    public async Task<string?> SignUpAsync(string email, string password, string username)
    {
        email = email.Trim();
        username = username.Trim();
        if (!UsernamePattern.IsMatch(username))
            throw new AccountException("Usernames are 3–20 characters: letters, numbers and _ only.");
        if (!email.Contains('@') || email.Length < 5) throw new AccountException("That doesn't look like an email address.");
        if (password.Length < 8) throw new AccountException("Use a password of at least 8 characters.");

        var body = new JsonObject
        {
            ["email"] = email,
            ["password"] = password,
            ["data"] = new JsonObject { ["username"] = username },
        };
        var json = await SendAsync(HttpMethod.Post, "/auth/v1/signup", body, auth: false);
        if (json?["access_token"] != null)
        {
            await SignedInAsync(json);
            return null;
        }
        return $"Almost done! We sent a confirmation link to {email}. Click it, then sign in here.";
    }

    public async Task SignInAsync(string email, string password)
    {
        if (email.Trim().Length == 0 || password.Length == 0) throw new AccountException("Enter your email and password.");
        var body = new JsonObject { ["email"] = email.Trim(), ["password"] = password };
        var json = await SendAsync(HttpMethod.Post, "/auth/v1/token?grant_type=password", body, auth: false);
        await SignedInAsync(json!);
    }

    public async Task SendPasswordResetAsync(string email)
    {
        if (!email.Contains('@')) throw new AccountException("Enter the email address of your account first.");
        await SendAsync(HttpMethod.Post, "/auth/v1/recover", new JsonObject { ["email"] = email.Trim() }, auth: false);
    }

    public async Task SignOutAsync()
    {
        try
        {
            if (_session != null && await EnsureAccessTokenAsync()) await SendAsync(HttpMethod.Post, "/auth/v1/logout", null, auth: true);
        }
        catch (AccountException) { } // signing out locally is what matters
        _session = null;
        _accessToken = null;
        try { File.Delete(SessionFile); } catch (IOException) { }
        Changed();
    }

    async Task SignedInAsync(JsonNode json)
    {
        TakeTokens(json);
        var user = json["user"];
        _session = new SavedSession
        {
            RefreshToken = (string?)json["refresh_token"] ?? "",
            UserId = (string?)user?["id"] ?? "",
            Email = (string?)user?["email"] ?? "",
            Username = (string?)user?["user_metadata"]?["username"] ?? "",
        };
        SaveSession();
        await RefreshAccountAsync();
    }

    void TakeTokens(JsonNode json)
    {
        _accessToken = (string?)json["access_token"];
        var seconds = (int?)json["expires_in"] ?? 3600;
        _accessExpires = DateTime.UtcNow.AddSeconds(seconds - 60);
        if (_session != null && (string?)json["refresh_token"] is { Length: > 0 } refresh)
        {
            _session.RefreshToken = refresh; // Supabase rotates refresh tokens
            SaveSession();
        }
    }

    /// <summary>Gets a fresh access token from the saved refresh token. False when the sign-in has expired.</summary>
    async Task<bool> EnsureAccessTokenAsync()
    {
        if (_session == null) return false;
        if (_accessToken != null && DateTime.UtcNow < _accessExpires) return true;
        try
        {
            var json = await SendAsync(HttpMethod.Post, "/auth/v1/token?grant_type=refresh_token",
                new JsonObject { ["refresh_token"] = _session.RefreshToken }, auth: false);
            TakeTokens(json!);
            return true;
        }
        catch (AccountException ex) when (ex.Message.StartsWith("Your sign-in"))
        {
            // Refresh token revoked or expired: sign out locally.
            await SignOutAsync();
            return false;
        }
    }

    // ---- Account row (username, premium) ----

    /// <summary>Reads the account row from the server: username and premium. Call at startup and after a purchase.</summary>
    public async Task RefreshAccountAsync()
    {
        if (!await EnsureAccessTokenAsync()) return;
        var rows = await SendAsync(HttpMethod.Get, $"/rest/v1/accounts?select=username,premium,premium_until&id=eq.{_session!.UserId}", null, auth: true);
        if (rows is JsonArray { Count: > 0 } arr && arr[0] is { } row)
        {
            if ((string?)row["username"] is { Length: > 0 } name) _session.Username = name;
            _session.Premium = (bool?)row["premium"] ?? false;
            _session.PremiumUntil = DateTime.TryParse((string?)row["premium_until"], null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var until) ? until : null;
            _session.PremiumCheckedAt = DateTime.UtcNow;
            SaveSession();
        }
        Changed();
    }

    /// <summary>Stripe checkout for this account. The user id travels as client_reference_id so the webhook knows who paid.</summary>
    public string? PremiumCheckoutLink =>
        AccountConfig.PremiumCheckoutUrl.Length == 0 || _session == null ? null
            : $"{AccountConfig.PremiumCheckoutUrl}?client_reference_id={Uri.EscapeDataString(_session.UserId)}&prefilled_email={Uri.EscapeDataString(_session.Email)}";

    // ---- Cloud save ----

    public async Task UploadAsync(JsonNode data)
    {
        if (!await EnsureAccessTokenAsync()) throw new AccountException("Sign in first.");
        var body = new JsonObject { ["user_id"] = _session!.UserId, ["data"] = data, ["updated_at"] = DateTime.UtcNow.ToString("o") };
        await SendAsync(HttpMethod.Post, "/rest/v1/cloud_saves?on_conflict=user_id", body, auth: true,
            prefer: "resolution=merge-duplicates,return=minimal");
    }

    /// <summary>The cloud save and when it was made, or (null, null) when there isn't one yet.</summary>
    public async Task<(JsonNode? Data, DateTime? Saved)> DownloadAsync()
    {
        if (!await EnsureAccessTokenAsync()) throw new AccountException("Sign in first.");
        var rows = await SendAsync(HttpMethod.Get, $"/rest/v1/cloud_saves?select=data,updated_at&user_id=eq.{_session!.UserId}", null, auth: true);
        if (rows is not JsonArray { Count: > 0 } arr || arr[0] is not { } row) return (null, null);
        return (row["data"]?.DeepClone(), DateTime.TryParse((string?)row["updated_at"], out var t) ? t : null);
    }

    // ---- HTTP ----

    async Task<JsonNode?> SendAsync(HttpMethod method, string path, JsonNode? body, bool auth, string? prefer = null)
    {
        if (!Configured) throw new AccountException("Accounts aren't set up in this version of SpudBoost yet.");
        using var request = new HttpRequestMessage(method, AccountConfig.SupabaseUrl.TrimEnd('/') + path);
        request.Headers.Add("apikey", AccountConfig.AnonKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth ? _accessToken : AccountConfig.AnonKey);
        if (prefer != null) request.Headers.Add("Prefer", prefer);
        if (body != null) request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try { response = await _http.SendAsync(request); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new AccountException("Couldn't reach the SpudBoost account server. Check your internet connection and try again.");
        }
        using (response)
        {
            var text = await response.Content.ReadAsStringAsync();
            if (response.IsSuccessStatusCode) return text.Length == 0 ? null : JsonNode.Parse(text);
            throw new AccountException(FriendlyError(response.StatusCode, text, path));
        }
    }

    static string FriendlyError(HttpStatusCode status, string text, string path)
    {
        string raw = "";
        try
        {
            var json = JsonNode.Parse(text);
            raw = ((string?)json?["error_code"] ?? "") + " " + ((string?)json?["msg"] ?? (string?)json?["error_description"] ?? (string?)json?["message"] ?? "");
        }
        catch (JsonException) { }
        var r = raw.ToLowerInvariant();
        if (r.Contains("invalid_credentials") || r.Contains("invalid login")) return "Wrong email or password.";
        if (r.Contains("email_not_confirmed") || r.Contains("not confirmed")) return "Confirm your email first: click the link we sent you, then sign in.";
        if (r.Contains("user_already_exists") || r.Contains("already registered")) return "There's already an account with that email. Sign in instead.";
        if (r.Contains("weak_password")) return "That password is too weak. Use at least 8 characters with a mix of letters and numbers.";
        // The accounts table rejects a taken (or invalid) username, which fails the whole sign-up.
        if ((r.Contains("duplicate key") && r.Contains("username")) || r.Contains("database error saving new user"))
            return "That username is already taken. Try another.";
        if (r.Contains("over_email_send_rate_limit") || status == (HttpStatusCode)429) return "Too many tries. Wait a minute and try again.";
        if (path.Contains("grant_type=refresh_token")) return "Your sign-in has expired. Sign in again.";
        return $"The account server said: {(raw.Trim().Length > 0 ? raw.Trim() : status.ToString())}";
    }

    // ---- Session file (encrypted for this Windows user) ----

    static SavedSession? LoadSession()
    {
        try
        {
            if (!File.Exists(SessionFile)) return null;
            var plain = ProtectedData.Unprotect(File.ReadAllBytes(SessionFile), Entropy, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<SavedSession>(plain);
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or IOException) { return null; }
    }

    void SaveSession()
    {
        if (_session == null) return;
        try
        {
            Directory.CreateDirectory(BackupStore.Folder);
            File.WriteAllBytes(SessionFile, ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(_session), Entropy, DataProtectionScope.CurrentUser));
        }
        catch (Exception ex) when (ex is CryptographicException or IOException) { }
    }
}
