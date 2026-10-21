# Setting up SpudBoost accounts and Premium

SpudBoost accounts run on **Supabase** (free), which handles sign-up, email confirmation, password reset and the database.
Payments for Premium go through **Stripe**: buyers pay on Stripe's own checkout page, and a small server function
switches Premium on for their account. Card details never touch SpudBoost.

You create both accounts yourself. After that, send Claude only these public values:

- the Supabase **Project URL**
- the Supabase **anon / publishable key**
- the Stripe **Payment Link** (and optionally the customer portal link)

**Never share** the Supabase `service_role` key or your Stripe **secret** key (`sk_...`) with anyone, including Claude.
They only go into Supabase's secret settings (step 6).

## 1. Create the Supabase project (about 5 minutes)

1. Go to <https://supabase.com>, sign up and click **New project**. Pick a name (e.g. SpudBoost), a strong database password and the region closest to most players.
2. When it's ready, open **SQL Editor → New query**, paste everything from [`supabase/setup.sql`](supabase/setup.sql) and click **Run**.
   This creates the `accounts` table (username and Premium) and the `cloud_saves` table, with security rules so people can only read their own data.
3. Open **Authentication → Sign In / Providers → Email** and make sure **Email** is enabled and **Confirm email** is on.
4. Open **Authentication → URL Configuration** and set **Site URL** to `https://github.com/FlopzYT/SpudBoost`. This is where the "confirm your email" link lands.
5. Open **Project Settings → API** (or **Data API**) and copy the **Project URL** and the **anon public** key. Send these to Claude.

Supabase's built-in email sender only allows a few emails per hour. Before many people sign up, add your own SMTP
(**Authentication → Emails → SMTP settings**, e.g. with Resend or Brevo, both free to start).

## 2. Create the Premium product in Stripe

1. Sign up at <https://stripe.com>. Stay in **Test mode** (toggle at the top) until everything works.
2. **Product catalog → Add product**: name it "SpudBoost Premium", then set the price.
   - **Recurring** (e.g. monthly) for a subscription, or
   - **One-off** for lifetime Premium.
3. **Payment Links → New**: pick that product. Under **After payment**, show a confirmation like "Premium is active. Go back to SpudBoost."
   Copy the link (`https://buy.stripe.com/...`) and send it to Claude.
4. Optional: **Settings → Billing → Customer portal**, turn it on and copy its link, so subscribers can cancel or update their card from the app.

## 3. Connect Stripe to Supabase (the webhook)

This is what switches Premium on after a payment, and off when a subscription ends.

1. Install the Supabase CLI: `npm install -g supabase`, then run `supabase login`.
2. In this project folder, run `supabase link --project-ref <your project ref>`. The ref is the part before `.supabase.co` in your Project URL.
3. Deploy the function: `supabase functions deploy stripe-webhook --no-verify-jwt`
4. In Stripe: **Developers → Webhooks → Add endpoint**
   - URL: `https://<your project ref>.supabase.co/functions/v1/stripe-webhook`
   - Events: `checkout.session.completed`, `customer.subscription.updated`, `customer.subscription.deleted`
5. Copy the endpoint's **Signing secret** (`whsec_...`) and your **Secret key** (`sk_test_...`, under **Developers → API keys**).
6. Store them in Supabase (never in the app):
   `supabase secrets set STRIPE_SECRET_KEY=sk_test_... STRIPE_WEBHOOK_SECRET=whsec_...`

## 4. Test, then go live

1. Claude puts your Project URL, anon key and Payment Link into [`GameTweaks/Core/AccountConfig.cs`](GameTweaks/Core/AccountConfig.cs) and builds a new version.
2. Create an account in SpudBoost, confirm the email, then click **Get Premium**. Pay with Stripe's test card `4242 4242 4242 4242` (any future date, any CVC).
3. Premium should show as **ACTIVE** in the account window within a few seconds (click **I've paid: refresh** if needed).
4. When it works, switch Stripe to **Live mode**. Make a live Payment Link and a live webhook (repeat steps 2.3 and 3.4–3.6 with live keys), and send Claude the live Payment Link.

## How it fits together

| Piece | Where it lives | Who can change it |
|---|---|---|
| Email, password | Supabase Auth | The user (password reset by email) |
| Username, Premium | `accounts` table | Username at sign-up; Premium **only** by the Stripe webhook |
| Cloud save | `cloud_saves` table | Only that user |
| Payments | Stripe | Stripe |

The app remembers the sign-in on each PC, encrypted for that Windows user. Premium is re-checked with the server at
every start, and keeps working offline for up to 3 days.
