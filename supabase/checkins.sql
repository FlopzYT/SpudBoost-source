-- SpudBoost anonymous daily check-ins. Run once in Supabase: SQL Editor → New query → paste → Run.
-- The app (publishable key) can only ADD today's check-in. Nobody can read check-ins through the API;
-- you see the numbers in the dashboard (Table Editor → daily_active_users, or the SQL editor).

create table if not exists public.checkins (
  install_id uuid not null,                       -- random, made on the user's PC
  day        date not null default (now() at time zone 'utc')::date,
  version    text not null check (char_length(version) between 1 and 20),
  created_at timestamptz not null default now(),
  primary key (install_id, day)                   -- one check-in per install per day
);

alter table public.checkins enable row level security;
drop policy if exists "app can check in for today" on public.checkins;
create policy "app can check in for today" on public.checkins
  for insert to anon
  with check (day = (now() at time zone 'utc')::date);
-- No select/update/delete policies: check-ins can't be read or changed through the API.

-- Active users per day (and how many are on each version), for you in the dashboard only.
create or replace view public.daily_active_users with (security_invoker = true) as
  select day, count(*) as active_users
  from public.checkins
  group by day
  order by day desc;

create or replace view public.versions_today with (security_invoker = true) as
  select version, count(*) as users
  from public.checkins
  where day = (now() at time zone 'utc')::date
  group by version
  order by users desc;

revoke all on public.daily_active_users from anon, authenticated;
revoke all on public.versions_today from anon, authenticated;
