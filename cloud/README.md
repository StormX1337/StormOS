# STORM Cloud

Optional backend for STORM OS: accounts, licenses, Stripe billing, sync, profile distribution, releases,
announcements, AI analysis, the web portal and Storm Admin.

```bash
pnpm install && pnpm build     # types → validation → database → api, web, admin
pnpm typecheck
pnpm test                      # TEST_DATABASE_URL=postgresql://… enables the API end-to-end suite
```

| Path | Description |
|---|---|
| `apps/api` | NestJS API (`/api/v1`), socket.io `/realtime` |
| `apps/web` | Next.js portal (port 3000) |
| `apps/admin` | Storm Admin (port 3001) |
| `packages/*` | shared types, Zod validation, Prisma database, UI components |
| `infrastructure/` | Dockerfiles, docker compose stack, nginx |
| `.env.example` | every configuration variable, documented |

Documentation: [../docs/CLOUD.md](../docs/CLOUD.md), [../docs/API.md](../docs/API.md),
[../docs/DEPLOYMENT.md](../docs/DEPLOYMENT.md).
