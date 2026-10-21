namespace GameTweaks.Core;

/// <summary>
/// Where SpudBoost accounts live. Fill these in from your Supabase project (Project Settings → API) and your
/// Stripe Payment Link; see ACCOUNTS-SETUP.md. All three are public values that are safe to ship in the app:
/// the anon key can only do what the database's row-level security allows, and premium can only be switched
/// on by the Stripe webhook on the server.
/// </summary>
public static class AccountConfig
{
    /// <summary>e.g. "https://abcdefghijklmnop.supabase.co"</summary>
    public const string SupabaseUrl = "https://vizfsimjepggldfsixei.supabase.co";

    /// <summary>The project's anon / publishable key (never the service_role key).</summary>
    public const string AnonKey = "sb_publishable_tBiCqwR5MJVIdkkamor-lA_7bAcFwAu";

    /// <summary>Stripe Payment Link for SpudBoost Premium, e.g. "https://buy.stripe.com/xxxx".</summary>
    public const string PremiumCheckoutUrl = "";

    /// <summary>Stripe customer portal link where people manage or cancel their subscription (optional).</summary>
    public const string ManageSubscriptionUrl = "";

    public static bool Configured => SupabaseUrl.StartsWith("https://") && AnonKey.Length > 20;
}
