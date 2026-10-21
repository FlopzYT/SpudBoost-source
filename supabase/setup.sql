-- SpudBoost accounts: run this once in your Supabase project (SQL Editor → New query → paste → Run).

-- One row per user, created automatically when someone signs up.
-- Users can read their own row. Nobody can change premium from the app: only the Stripe webhook
-- (which uses the service role key on the server) can.
create table if not exists public.accounts (
  id                 uuid primary key references auth.users (id) on delete cascade,
  username           text not null check (username ~ '^[A-Za-z0-9_]{3,20}$'),
  premium            boolean not null default false,
  premium_until      timestamptz,          -- null with premium = true means lifetime
  stripe_customer_id text unique,
  created_at         timestamptz not null default now()
);
create unique index if not exists accounts_username_lower on public.accounts (lower(username));

alter table public.accounts enable row level security;
drop policy if exists "read own account" on public.accounts;
create policy "read own account" on public.accounts for select using (auth.uid() = id);

create or replace function public.handle_new_user()
returns trigger language plpgsql security definer set search_path = '' as $$
begin
  insert into public.accounts (id, username) values (new.id, new.raw_user_meta_data ->> 'username');
  return new;
end;
$$;

drop trigger if exists on_auth_user_created on auth.users;
create trigger on_auth_user_created
  after insert on auth.users
  for each row execute function public.handle_new_user();

-- Cloud save: one JSON document per user (profiles, crosshairs, lists). Users can only touch their own.
create table if not exists public.cloud_saves (
  user_id    uuid primary key references auth.users (id) on delete cascade,
  data       jsonb not null check (pg_column_size(data) < 512000),
  updated_at timestamptz not null default now()
);

alter table public.cloud_saves enable row level security;
drop policy if exists "own save select" on public.cloud_saves;
drop policy if exists "own save insert" on public.cloud_saves;
drop policy if exists "own save update" on public.cloud_saves;
drop policy if exists "own save delete" on public.cloud_saves;
create policy "own save select" on public.cloud_saves for select using (auth.uid() = user_id);
create policy "own save insert" on public.cloud_saves for insert with check (auth.uid() = user_id);
create policy "own save update" on public.cloud_saves for update using (auth.uid() = user_id) with check (auth.uid() = user_id);
create policy "own save delete" on public.cloud_saves for delete using (auth.uid() = user_id);
