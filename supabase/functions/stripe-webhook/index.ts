// SpudBoost Premium: Stripe tells this function about payments, and it switches premium on/off on the account.
// Deploy: supabase functions deploy stripe-webhook --no-verify-jwt
// Secrets: supabase secrets set STRIPE_SECRET_KEY=sk_... STRIPE_WEBHOOK_SECRET=whsec_...
// (SUPABASE_URL and SUPABASE_SERVICE_ROLE_KEY are provided to functions automatically.)
import Stripe from "npm:stripe@17";
import { createClient } from "npm:@supabase/supabase-js@2";

const stripe = new Stripe(Deno.env.get("STRIPE_SECRET_KEY")!);
const webhookSecret = Deno.env.get("STRIPE_WEBHOOK_SECRET")!;
const supabase = createClient(Deno.env.get("SUPABASE_URL")!, Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")!);

/** End of the paid period for a subscription, as an ISO date (null if unknown). */
function periodEnd(sub: Stripe.Subscription): string | null {
  // deno-lint-ignore no-explicit-any
  const s = sub as any;
  const end = s.current_period_end ?? s.items?.data?.[0]?.current_period_end;
  return end ? new Date(end * 1000).toISOString() : null;
}

Deno.serve(async (req) => {
  // Only genuine Stripe requests (signed with your webhook secret) are accepted.
  const signature = req.headers.get("stripe-signature");
  const body = await req.text();
  let event: Stripe.Event;
  try {
    event = await stripe.webhooks.constructEventAsync(body, signature ?? "", webhookSecret);
  } catch (err) {
    return new Response(`Webhook signature check failed: ${(err as Error).message}`, { status: 400 });
  }

  if (event.type === "checkout.session.completed") {
    const session = event.data.object as Stripe.Checkout.Session;
    // SpudBoost opens the payment link with ?client_reference_id=<account id>.
    const userId = session.client_reference_id;
    if (userId && session.payment_status !== "unpaid") {
      let until: string | null = null; // one-time payment = lifetime premium
      if (session.subscription) {
        const sub = await stripe.subscriptions.retrieve(session.subscription as string);
        until = periodEnd(sub);
      }
      const { error } = await supabase.from("accounts")
        .update({ premium: true, premium_until: until, stripe_customer_id: session.customer as string | null })
        .eq("id", userId);
      if (error) return new Response(error.message, { status: 500 }); // Stripe retries later
    }
  }

  if (event.type === "customer.subscription.updated" || event.type === "customer.subscription.deleted") {
    const sub = event.data.object as Stripe.Subscription;
    const active = event.type === "customer.subscription.updated" && ["active", "trialing", "past_due"].includes(sub.status);
    const { error } = await supabase.from("accounts")
      .update({ premium: active, premium_until: active ? periodEnd(sub) : null })
      .eq("stripe_customer_id", sub.customer as string);
    if (error) return new Response(error.message, { status: 500 });
  }

  return new Response("ok");
});
