# Deployment

## Desktop

Release builds are produced by `.github/workflows/release.yml` (tag `vX.Y.Z` or manual dispatch):
tests → publish app, service and CLI (self-contained, `win-x64` and `win-arm64`) → optional code signing → WiX MSI →
SHA-256 files → draft GitHub release. Publish the MSI URL, SHA-256 and size in Storm Admin › Releases so that
installed clients see the update. Code signing: add `SIGNING_PFX_BASE64` and `SIGNING_PFX_PASSWORD` repository
secrets (or adapt `scripts/publish.ps1` to Azure Trusted Signing). Sign every release — the service's trust check
additionally binds clients to the service's signer when the service is signed.

Enterprise deployment: `msiexec /i StormOS-1.2.0-x64.msi /qn` (per-machine). Uninstall: `msiexec /x {ProductCode} /qn`
or *Settings › Apps*. User data in `%LOCALAPPDATA%\StormOS` and `%ProgramData%\StormOS` is kept on uninstall.

## Cloud (Docker Compose)

```bash
cd cloud
cp .env.example .env                  # set POSTGRES_PASSWORD, JWT_ACCESS_SECRET, ENTITLEMENT_PRIVATE_KEY, origins, Stripe …
mkdir -p infrastructure/nginx/certs   # fullchain.pem + privkey.pem for api./www./admin. hosts
docker compose -f infrastructure/docker/docker-compose.yml --env-file .env up -d --build
docker compose -f infrastructure/docker/docker-compose.yml exec api node node_modules/@storm/database/dist/seed.js   # optional, with SEED_ADMIN_*
```

- PostgreSQL and Redis sit on an internal network; only nginx publishes ports 80/443.
- The API container applies pending migrations on start (`RUN_MIGRATIONS=false` to disable, e.g. when several API
  replicas start at once — then run migrations as a one-off job).
- Set `TRUST_PROXY=true` behind nginx so rate limits and audit entries use the real client IP.
- Adjust hostnames in `infrastructure/nginx/nginx.conf`; restrict `admin.` further (VPN, IP allow-list or SSO).

### Production checklist

- Unique secrets from a secret manager; `JWT_ACCESS_SECRET` ≥ 32 random bytes; P-256 entitlement key generated
  offline and backed up (rotating it invalidates outstanding license tokens after at most 72 h).
- Live Stripe keys (the API refuses `sk_test_` keys when `NODE_ENV=production`), webhook secret per endpoint.
- Daily PostgreSQL backups with tested restores; Redis holds only caches and counters.
- Monitor `GET /api/v1/health`, container health checks and the `Http`/`Audit` logs; alert on 5xx rate and
  `auth.refresh_reuse_detected` audit entries.
- Keep `AI_PROVIDER=rules` unless an Anthropic API key and a data-processing agreement are in place.
