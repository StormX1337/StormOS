# STORM Cloud

STORM OS works fully offline. The cloud adds accounts, licenses, sync, profile distribution, update metadata,
announcements and optional AI analysis. All cloud features are opt-in (*Settings › STORM Cloud* and *Privacy*).

## Components (`cloud/`)

| Package | Tech | Purpose |
|---|---|---|
| `apps/api` | NestJS 11, Prisma 6, PostgreSQL, optional Redis, Stripe, socket.io | REST API `/api/v1`, realtime channel `/realtime` |
| `apps/web` | Next.js 15, React 19, Tailwind 4 | product site, pricing/checkout, downloads, account |
| `apps/admin` | Next.js 15 | Storm Admin: users, grants, roles, releases, announcements, profiles, audit log |
| `packages/types` | TypeScript | shared contracts, tiers and features |
| `packages/validation` | Zod | every input schema (also used by seeding and admin) |
| `packages/database` | Prisma | schema, migrations, generated client, seed |
| `packages/ui` | React + CVA | shadcn/ui-style components and design tokens |

## Licensing

Tiers: **Free** (monitoring, game detection, profiles), **Pro** (+ benchmarks, overlay, network diagnostics,
advanced optimizations, cloud sync, extended history), **Ultimate** (+ AI analysis, advanced profiles).
Entitlements are computed **server-side** from Stripe subscriptions (active/trialing, or past-due within a 7-day grace
period) and complimentary grants set in Storm Admin.

The desktop app registers the PC (`POST /devices`) and requests a license token
(`POST /devices/{id}/entitlements`): an ES256-signed compact JWS
`{iss: "storm-cloud", sub, did, tier, features, iat, nbf, exp}` bound to the device, valid for 72 hours (never beyond
the paid period plus grace). The app verifies it offline with the public key from
`GET /system/entitlement-key` (configured as `Licensing:PublicKeyPem`), so licenses keep working without
connectivity until expiry. `Licensing:Mode = Community` (default for self-built binaries) enables all local features
without an account.

## Billing (Stripe)

Checkout (`POST /billing/checkout`) and the customer portal (`POST /billing/portal`) are Stripe-hosted; card data
never touches STORM servers. Webhooks (`POST /billing/webhook`) are verified against the raw body and processed
exactly once: a transaction takes a PostgreSQL advisory lock on the event id, skips events already marked processed
and records the event only when processing committed. Handled events: `checkout.session.completed` (links the
Stripe customer), `customer.subscription.created|updated|deleted` (upserts the subscription; unknown prices are
ignored). Entitlement changes are pushed to connected clients (`entitlements.changed`).

Configure prices with `STRIPE_PRICE_PRO_MONTH`, `…_PRO_YEAR`, `…_ULTIMATE_MONTH`, `…_ULTIMATE_YEAR` and point a Stripe
webhook endpoint at `https://api.<domain>/api/v1/billing/webhook` with those event types.

## Sync

With *cloud sync* enabled (Pro+), completed benchmarks and game sessions are uploaded (`POST /benchmarks`,
`POST /sessions`). Uploads are idempotent per client id. Only measurements and hardware names are sent — no
usernames, file paths or process lists.

## AI analysis

`POST /ai/analyze` (Ultimate) receives an aggregated `AnalysisWindow` (averages, peaks, frame statistics, power
plan name, game name). The server always runs the deterministic rule engine (identical to the desktop rules). With
`AI_PROVIDER=anthropic`, Claude additionally produces recommendations through a schema-constrained response; each
one is re-validated, **every evidence line must quote a submitted measurement**, ids are namespaced `ai-…`, and
actions are restricted to reversible allow-listed rules (`power.plan`, `windows.game-mode`, `windows.game-dvr`,
`windows.hags`, `windows.windowed-optimizations`, `background.lower-priority`). The desktop app shows AI results
with their source and still asks for confirmation before any change. Requests are limited per user per day
(`AI_DAILY_LIMIT`).

## Realtime

socket.io namespace `/realtime`; the access token is sent in the handshake (`auth.token`). Events:
`entitlements.changed`, `announcement`. Unauthenticated sockets are disconnected.

## Local development

```bash
cd cloud
pnpm install
cp .env.example .env            # fill JWT_ACCESS_SECRET, ENTITLEMENT_PRIVATE_KEY, DATABASE_URL
pnpm build
pnpm --filter @storm/database exec prisma migrate deploy
SEED_ADMIN_EMAIL=you@example.com SEED_ADMIN_PASSWORD='a long passphrase' pnpm db:seed
node --env-file=.env apps/api/dist/main.js            # API on :4000
STORM_API_URL=http://localhost:4000 pnpm dev:web      # :3000
STORM_API_URL=http://localhost:4000 pnpm dev:admin    # :3001
```

Tests: `pnpm test` (unit tests everywhere; the API end-to-end suite runs when `TEST_DATABASE_URL` points to a
disposable PostgreSQL database with migrations applied — it truncates all tables).
