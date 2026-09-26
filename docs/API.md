# STORM Cloud REST API (`/api/v1`)

JSON over HTTPS. Authenticated endpoints need `Authorization: Bearer <access token>`. Errors always look like
`{ "statusCode": 400, "code": "validation_failed", "message": "email: Invalid email" }` with codes
`validation_failed`, `unauthorized`, `forbidden`, `not_found`, `conflict`, `entitlement_required` (402),
`rate_limited` (429), `service_unavailable`, `internal`. Rate limit headers: `X-RateLimit-Limit|Remaining|Reset`.

## Auth

| Method | Path | Auth | Body → Response |
|---|---|---|---|
| POST | `/auth/register` | – | `{email, password}` → `201 {tokens:{accessToken, refreshToken, expiresIn}, email}` (409 if taken) |
| POST | `/auth/login` | – | `{email, password}` → `{tokens, email}` (401 wrong credentials, 403 disabled) |
| POST | `/auth/refresh` | – | `{refreshToken}` → `{accessToken, refreshToken, expiresIn}` (rotates; reuse revokes the family) |
| POST | `/auth/logout` | – | `{refreshToken}` → 204 |
| GET | `/auth/me` | ✓ | → `{id, email, role, entitlements}` |

Passwords: 10–128 characters. Auth routes are limited to 10 requests/minute per IP (refresh: 30).

## Devices and entitlements

| Method | Path | Auth | Notes |
|---|---|---|---|
| POST | `/devices` | ✓ | `{name, hardware, platform: "windows"}` → `201 {id, name}`; max 10 active PCs |
| GET | `/devices` | ✓ | active PCs |
| DELETE | `/devices/{id}` | ✓ | 204 |
| POST | `/devices/{id}/entitlements` | ✓ | → `{token, tier, features, expiresAt}` (ES256 license token) |
| GET | `/entitlements/me` | ✓ | → `{tier, features, source, expiresAt}` |
| GET | `/system/entitlement-key` | – | → `{algorithm: "ES256", keyId, publicKeyPem}` |

## Billing

| Method | Path | Auth | Notes |
|---|---|---|---|
| POST | `/billing/checkout` | ✓ | `{tier: "pro"\|"ultimate", interval: "month"\|"year"}` → `{url}`; optional `Idempotency-Key` header |
| POST | `/billing/portal` | ✓ | → `{url}` |
| POST | `/billing/webhook` | Stripe signature | → `{received: true[, duplicate: true]}` |

## Sync

| Method | Path | Auth | Notes |
|---|---|---|---|
| POST | `/benchmarks` | ✓ + `cloud.sync` | desktop `BenchmarkResult` JSON → `201 {id}` (idempotent by result id) |
| GET | `/benchmarks?type=&page=&pageSize=` | ✓ | paged |
| GET/DELETE | `/benchmarks/{id}` | ✓ | |
| POST | `/sessions` | ✓ + `cloud.sync` | desktop `GameSession` JSON → `201 {id}` |
| GET | `/sessions?page=&pageSize=` | ✓ | paged |
| DELETE | `/sessions/{id}` | ✓ | |

## Content

| Method | Path | Auth | Notes |
|---|---|---|---|
| GET | `/profiles` | – | published game profiles (cached 5 min) |
| GET | `/releases/latest?channel=stable\|beta&arch=x64\|arm64` | – | `{version, channel, url, sha256, notes, publishedAt, sizeBytes}`; the beta channel also returns newer stable releases; 404 when none |
| GET | `/system/announcements` | – | active announcements |
| GET | `/health` | – | `{status, database, redis}`; 503 when degraded |

## AI

| Method | Path | Auth | Notes |
|---|---|---|---|
| POST | `/ai/analyze` | ✓ + `ai.analysis` | `AnalysisWindow` → `{recommendations: [{id, title, why, evidence[], risk, advice, action?, confidence, source}]}`; daily quota |

## Admin (`role: admin`; `support` has read access)

| Method | Path | Notes |
|---|---|---|
| GET | `/admin/stats` | counts, subscriptions by tier |
| GET | `/admin/users?q=&page=` | search by e-mail |
| GET | `/admin/users/{id}` | devices, subscriptions, entitlements |
| PATCH | `/admin/users/{id}` | `{role?, disabled?, compTier?, compExpiresAt?}` (admins cannot change their own role/status; disabling revokes sessions) |
| GET | `/admin/audit?q=&page=` | audit log (filter by action prefix) |
| GET/POST | `/admin/releases`, DELETE `/admin/releases/{id}` | `{version, channel, arch, url (https), sha256, sizeBytes, notes?, publishedAt?}` |
| GET/POST | `/admin/announcements`, DELETE `/admin/announcements/{id}` | `{title, body, severity, active, startsAt?, endsAt?}` |
| GET | `/admin/profiles`, `/admin/profiles/{id}` | |
| PUT | `/admin/profiles/{id}` | full profile JSON (validated) |
| PATCH | `/admin/profiles/{id}/publish` | `{published}` |
