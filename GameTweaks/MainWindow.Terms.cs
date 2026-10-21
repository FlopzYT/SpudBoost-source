using System.IO;
using System.Windows;
using System.Windows.Media.Animation;
using GameTweaks.Core;

namespace GameTweaks;

/// <summary>Terms of Service: must be accepted on first launch, and again whenever <see cref="TermsVersion"/> goes up.</summary>
public partial class MainWindow
{
    /// <summary>Raise this when Terms.md changes in a way people need to accept again.</summary>
    public const int TermsVersion = 1;

    static List<ReleaseNoteLine> LoadTerms()
    {
        var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Terms.md"));
        if (resource == null) return [];
        using var reader = new StreamReader(resource.Stream);
        // The title is already shown above the text.
        return ReleaseNotes.Parse(reader.ReadToEnd()).Where(l => l.Text != "SpudBoost Terms of Service").ToList();
    }

    void InitTerms()
    {
        if (_settings.TermsAcceptedVersion >= TermsVersion) return;
        ShowTerms(mustAccept: true);
    }

    void ShowTerms(bool mustAccept)
    {
        TermsList.ItemsSource = LoadTerms();
        TermsButtons.Visibility = mustAccept ? Visibility.Visible : Visibility.Collapsed;
        TermsCloseButton.Visibility = mustAccept ? Visibility.Collapsed : Visibility.Visible;
        TermsSubtitle.Text = !mustAccept
            ? $"You accepted these terms on {_settings.TermsAcceptedAt:MMMM d, yyyy}."
            : _settings.TermsAcceptedVersion > 0
                ? "We've updated our terms. Please read and accept them to keep using SpudBoost."
                : "Please read and accept these terms to use SpudBoost.";
        TermsAgreeBox.IsChecked = false;
        TermsAcceptButton.IsEnabled = false;
        TermsOverlay.Visibility = Visibility.Visible;
        TermsOverlay.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
        // Always start at the top (the window laying itself out can leave it scrolled down a little).
        Dispatcher.BeginInvoke(TermsScroll.ScrollToHome, System.Windows.Threading.DispatcherPriority.Loaded);
        TermsScroll.ScrollToHome();
    }

    void TermsAgree_Click(object sender, RoutedEventArgs e) => TermsAcceptButton.IsEnabled = TermsAgreeBox.IsChecked == true;

    void TermsAccept_Click(object sender, RoutedEventArgs e)
    {
        _settings.TermsAcceptedVersion = TermsVersion;
        _settings.TermsAcceptedAt = DateTime.Now;
        _settings.Save();
        HideTerms();
    }

    void TermsDecline_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "SpudBoost can't be used without accepting the Terms of Service. Exit SpudBoost?", "SpudBoost",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            ExitApp();
    }

    void TermsClose_Click(object sender, RoutedEventArgs e) => HideTerms();

    void ViewTerms_Click(object sender, RoutedEventArgs e) => ShowTerms(mustAccept: false);

    void HideTerms()
    {
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(180));
        fade.Completed += (_, _) => TermsOverlay.Visibility = Visibility.Collapsed;
        TermsOverlay.BeginAnimation(OpacityProperty, fade);
    }
}
