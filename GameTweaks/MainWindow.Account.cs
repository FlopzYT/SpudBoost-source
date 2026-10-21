using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using GameTweaks.Core;

namespace GameTweaks;

/// <summary>Sign in / create account, Premium status and cloud save.</summary>
public partial class MainWindow
{
    readonly AccountService _account = new();
    readonly DispatcherTimer _cloudTimer = new() { Interval = TimeSpan.FromSeconds(8) };

    /// <summary>True when the signed-in account has Premium (confirmed by the server).</summary>
    bool IsPremium => _account.IsPremium;

    void InitAccount()
    {
        CloudAutoBox.IsChecked = _settings.CloudAutoBackup;
        _account.PropertyChanged += (_, _) => UpdateAccountUi();
        _cloudTimer.Tick += async (_, _) =>
        {
            _cloudTimer.Stop();
            await BackupToCloudAsync(quiet: true);
        };
        UpdateAccountUi();
        // Re-check premium and the username with the server; works offline from the saved state.
        if (_account.IsSignedIn) Loaded += async (_, _) =>
        {
            try { await _account.RefreshAccountAsync(); }
            catch (AccountException) { }
        };
    }

    void UpdateAccountUi()
    {
        var signedIn = _account.IsSignedIn;
        var name = signedIn ? (_account.Username.Length > 0 ? _account.Username : _account.Email) : "";
        AvatarInitial.Text = BigAvatarInitial.Text = signedIn && name.Length > 0 ? name[..1].ToUpperInvariant() : "?";
        AccountName.Text = signedIn ? name : "Sign in";
        AccountSub.Text = signedIn ? (IsPremium ? "Premium" : "Free account") : "Create a free account";
        SidebarPremiumBadge.Visibility = IsPremium ? Visibility.Visible : Visibility.Collapsed;

        SignedOutPanel.Visibility = signedIn ? Visibility.Collapsed : Visibility.Visible;
        SignedInPanel.Visibility = signedIn ? Visibility.Visible : Visibility.Collapsed;
        AccountTitle.Text = signedIn ? "Your account" : CreateTab.IsChecked == true ? "Create your account" : "Sign in to SpudBoost";
        AccountSubtitle.Text = signedIn ? "Signed in on this PC." : "Sync your setup and unlock Premium.";
        if (!signedIn) return;

        AccountShownName.Text = name;
        AccountShownEmail.Text = _account.Email;
        PremiumStateText.Text = IsPremium ? "ACTIVE" : "FREE";
        PremiumText.Text = IsPremium
            ? "Premium is active on your account" + (_account.PremiumUntil is { } until ? $" until {until.ToLocalTime():MMM d, yyyy}." : ".") + " Thanks for supporting SpudBoost!"
            : "Unlock every Premium feature on any PC you sign in on. Payment is handled securely by Stripe in your browser.";
        GetPremiumButton.Visibility = IsPremium ? Visibility.Collapsed : Visibility.Visible;
        RefreshPremiumButton.Visibility = IsPremium ? Visibility.Collapsed : Visibility.Visible;
        ManagePremiumButton.Visibility = IsPremium && AccountConfig.ManageSubscriptionUrl.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---- Opening and closing ----

    void AccountButton_Click(object sender, RoutedEventArgs e) => ShowAccount();

    /// <summary>Opens the account window. A premium feature can pass a reason to show at the top.</summary>
    void ShowAccount(string? reason = null)
    {
        AccountError.Visibility = AccountInfo.Visibility = Visibility.Collapsed;
        if (!_account.Configured) ShowAccountMessage("Accounts aren't switched on in this version of SpudBoost yet.", error: true);
        else if (reason != null) ShowAccountMessage(reason, error: false);
        UpdateAccountUi();
        AccountOverlay.Visibility = Visibility.Visible;
        AccountOverlay.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
        if (!_account.IsSignedIn) (CreateTab.IsChecked == true ? AccountUsername : AccountEmail).Focus();
    }

    void CloseAccount_Click(object sender, RoutedEventArgs e) => CloseAccount();

    void AccountOverlay_BackgroundClick(object sender, MouseButtonEventArgs e) => CloseAccount();

    /// <summary>Clicks inside the card shouldn't close it.</summary>
    void AccountCard_MouseDown(object sender, MouseButtonEventArgs e) => e.Handled = true;

    void CloseAccount()
    {
        AccountPassword.Clear();
        AccountPassword2.Clear();
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(150));
        fade.Completed += (_, _) => AccountOverlay.Visibility = Visibility.Collapsed;
        AccountOverlay.BeginAnimation(OpacityProperty, fade);
    }

    void ShowAccountMessage(string text, bool error)
    {
        AccountError.Visibility = error ? Visibility.Visible : Visibility.Collapsed;
        AccountInfo.Visibility = error ? Visibility.Collapsed : Visibility.Visible;
        (error ? AccountError : AccountInfo).Text = text;
    }

    // ---- Sign in / create account ----

    void AccountMode_Checked(object sender, RoutedEventArgs e)
    {
        if (AccountSubmit == null) return;
        var create = CreateTab.IsChecked == true;
        UsernameRow.Visibility = ConfirmRow.Visibility = create ? Visibility.Visible : Visibility.Collapsed;
        ForgotButton.Visibility = create ? Visibility.Collapsed : Visibility.Visible;
        AccountSubmit.Content = create ? "Create account" : "Sign in";
        AccountError.Visibility = AccountInfo.Visibility = Visibility.Collapsed;
        UpdateAccountUi();
    }

    void AccountPassword_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) AccountSubmit_Click(sender, e);
    }

    async void AccountSubmit_Click(object sender, RoutedEventArgs e)
    {
        var create = CreateTab.IsChecked == true;
        if (create && AccountPassword.Password != AccountPassword2.Password)
        {
            ShowAccountMessage("The two passwords don't match.", error: true);
            return;
        }
        AccountSubmit.IsEnabled = false;
        AccountError.Visibility = AccountInfo.Visibility = Visibility.Collapsed;
        try
        {
            if (create)
            {
                var message = await _account.SignUpAsync(AccountEmail.Text, AccountPassword.Password, AccountUsername.Text);
                if (message != null)
                {
                    // Email confirmation needed: switch to Sign in with the email filled in.
                    SignInTab.IsChecked = true;
                    ShowAccountMessage(message, error: false);
                    return;
                }
            }
            else await _account.SignInAsync(AccountEmail.Text, AccountPassword.Password);

            AccountPassword.Clear();
            AccountPassword2.Clear();
            SetStatus($"Signed in as {_account.Username}.");
            await OfferCloudRestoreAsync();
        }
        catch (AccountException ex) { ShowAccountMessage(ex.Message, error: true); }
        finally { AccountSubmit.IsEnabled = true; }
    }

    async void ForgotPassword_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _account.SendPasswordResetAsync(AccountEmail.Text);
            ShowAccountMessage($"If there's an account for {AccountEmail.Text.Trim()}, a password reset link is on its way.", error: false);
        }
        catch (AccountException ex) { ShowAccountMessage(ex.Message, error: true); }
    }

    async void SignOut_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "Sign out of SpudBoost on this PC? Your settings stay as they are.", "SpudBoost",
                MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        if (_settings.CloudAutoBackup) await BackupToCloudAsync(quiet: true);
        await _account.SignOutAsync();
        SetStatus("Signed out.");
    }

    // ---- Premium ----

    /// <summary>
    /// Use at the start of a premium feature: true when the account has Premium; otherwise opens the account window
    /// explaining why and returns false.
    /// </summary>
    bool RequirePremium(string feature)
    {
        if (IsPremium) return true;
        ShowAccount(_account.IsSignedIn
            ? $"{feature} is a Premium feature. Get Premium below to unlock it."
            : $"{feature} is a Premium feature. Sign in or create an account first, then get Premium.");
        return false;
    }

    void GetPremium_Click(object sender, RoutedEventArgs e)
    {
        if (_account.PremiumCheckoutLink is not { } link)
        {
            ShowAccountMessage("Premium isn't on sale yet. Check back soon!", error: false);
            return;
        }
        Process.Start(new ProcessStartInfo(link) { UseShellExecute = true });
        ShowAccountMessage("Finish the payment in your browser. Premium switches on automatically; click \"I've paid: refresh\" if it doesn't show within a minute.", error: false);
    }

    void ManagePremium_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo(AccountConfig.ManageSubscriptionUrl) { UseShellExecute = true });

    async void RefreshPremium_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _account.RefreshAccountAsync();
            ShowAccountMessage(IsPremium ? "Premium is active. Enjoy!" : "No Premium on this account yet. Payments can take a minute to come through.", error: !IsPremium);
        }
        catch (AccountException ex) { ShowAccountMessage(ex.Message, error: true); }
    }

    // ---- Cloud save ----

    JsonObject BuildCloudSave() => new()
    {
        ["version"] = 1,
        ["profiles"] = JsonSerializer.SerializeToNode(_profiles),
        ["crosshair"] = _crosshair.ExportCode(),
        ["accent"] = _settings.AccentColor,
        ["gameProcesses"] = JsonSerializer.SerializeToNode(_settings.GameProcesses),
        ["backgroundProcesses"] = JsonSerializer.SerializeToNode(_settings.BackgroundProcesses),
        ["appsToClose"] = JsonSerializer.SerializeToNode(_settings.AppsToClose),
        ["favoriteGames"] = JsonSerializer.SerializeToNode(_settings.FavoriteGames),
    };

    /// <summary>Call after anything in the cloud save changes; uploads a few seconds later (changes are batched).</summary>
    void QueueCloudBackup()
    {
        if (!_account.IsSignedIn || !_settings.CloudAutoBackup) return;
        _cloudTimer.Stop();
        _cloudTimer.Start();
    }

    async Task BackupToCloudAsync(bool quiet)
    {
        if (!_account.IsSignedIn) return;
        try
        {
            await _account.UploadAsync(BuildCloudSave());
            if (!quiet) ShowAccountMessage($"Backed up at {DateTime.Now:h:mm tt}.", error: false);
        }
        catch (AccountException ex)
        {
            if (!quiet) ShowAccountMessage(ex.Message, error: true);
        }
    }

    async void CloudBackup_Click(object sender, RoutedEventArgs e) => await BackupToCloudAsync(quiet: false);

    async void CloudRestore_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var (data, saved) = await _account.DownloadAsync();
            if (data == null)
            {
                ShowAccountMessage("There's no cloud save on this account yet. Click Back up now to make one.", error: false);
                return;
            }
            if (MessageBox.Show(this, $"Restore your cloud save from {saved?.ToLocalTime():MMM d, h:mm tt}?\n\nThis brings back your profiles, crosshairs, " +
                    "Game Boost lists, favourites and accent colour. Profiles with the same name are replaced. Tweaks don't change until you load a profile.",
                    "SpudBoost", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            ApplyCloudSave(data);
            ShowAccountMessage("Restored from the cloud.", error: false);
        }
        catch (AccountException ex) { ShowAccountMessage(ex.Message, error: true); }
    }

    /// <summary>After signing in: if the account has a cloud save and this PC has nothing saved yet, offer to bring it back.</summary>
    async Task OfferCloudRestoreAsync()
    {
        try
        {
            var (data, saved) = await _account.DownloadAsync();
            if (data == null)
            {
                await BackupToCloudAsync(quiet: true); // first sign-in: start the cloud save from this PC
                return;
            }
            if (_profiles.Count == 0 && MessageBox.Show(this, $"Your account has a cloud save from {saved?.ToLocalTime():MMM d}. Restore it on this PC?",
                    "SpudBoost", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                ApplyCloudSave(data);
        }
        catch (AccountException) { }
    }

    void ApplyCloudSave(JsonNode data)
    {
        static List<string> Strings(JsonNode? n) =>
            n is JsonArray a ? a.Select(x => (string?)x).OfType<string>().Where(s => s.Length is > 0 and < 100).Take(200).ToList() : [];

        if (data["profiles"] is JsonArray profiles)
        {
            foreach (var p in profiles.Deserialize<List<PersonalProfile>>() ?? [])
            {
                if (string.IsNullOrWhiteSpace(p.Name)) continue;
                _profiles.RemoveAll(x => x.Name.Equals(p.Name, StringComparison.OrdinalIgnoreCase));
                _profiles.Add(p);
            }
            PersonalProfiles.Save(_profiles);
            RefreshProfiles();
        }
        if ((string?)data["crosshair"] is { Length: > 0 } code)
        {
            try
            {
                _crosshair.ImportCode(code);
                CrosshairEditCombo.ItemsSource = _crosshair.EditTargets;
                CrosshairEditCombo.SelectedIndex = 0;
            }
            catch (FormatException) { }
        }
        if (Strings(data["gameProcesses"]) is { Count: > 0 } games) _settings.GameProcesses = games;
        if (Strings(data["backgroundProcesses"]) is { Count: > 0 } background) _settings.BackgroundProcesses = background;
        if (Strings(data["appsToClose"]) is { Count: > 0 } close) _settings.AppsToClose = close;
        _settings.FavoriteGames = Strings(data["favoriteGames"]);
        if ((string?)data["accent"] is { Length: 7 } accent && accent.StartsWith('#'))
        {
            _settings.AccentColor = accent;
            ApplyAccent(accent);
            RefreshAccentList();
        }
        _settings.Save();
        GamesBox.Text = string.Join(Environment.NewLine, _settings.GameProcesses);
        BackgroundBox.Text = string.Join(Environment.NewLine, _settings.BackgroundProcesses);
        AppsToCloseBox.Text = string.Join(Environment.NewLine, _settings.AppsToClose);
        SortLibrary();
        UpdateHome();
        SetStatus("Cloud save restored.");
    }

    void CloudAuto_Click(object sender, RoutedEventArgs e)
    {
        _settings.CloudAutoBackup = CloudAutoBox.IsChecked == true;
        _settings.Save();
        QueueCloudBackup();
    }
}
