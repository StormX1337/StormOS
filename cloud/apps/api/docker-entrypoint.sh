#!/bin/sh
# Applies pending database migrations (unless disabled), then starts the API.
set -eu
if [ "${RUN_MIGRATIONS:-true}" = "true" ]; then
  PRISMA="$(ls -d node_modules/.pnpm/prisma@*/node_modules/prisma/build/index.js | head -n 1)"
  node "$PRISMA" migrate deploy --schema node_modules/@storm/database/prisma/schema.prisma
fi
exec node dist/main.js
