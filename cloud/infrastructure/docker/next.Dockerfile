# syntax=docker/dockerfile:1.7
# Builds the web portal or the admin panel. Build context: the cloud/ directory.
#   docker build -f infrastructure/docker/next.Dockerfile --build-arg APP=web .
ARG APP=web

FROM node:22-bookworm-slim AS build
ARG APP
ENV PNPM_HOME=/pnpm PATH=/pnpm:$PATH CI=true NEXT_TELEMETRY_DISABLED=1
RUN corepack enable
WORKDIR /repo
COPY package.json pnpm-lock.yaml pnpm-workspace.yaml tsconfig.base.json ./
COPY packages ./packages
COPY apps ./apps
RUN pnpm install --frozen-lockfile --filter @storm/${APP}...
RUN pnpm --filter @storm/${APP}... build

FROM node:22-bookworm-slim AS runtime
ARG APP
RUN apt-get update && apt-get install -y --no-install-recommends tini && rm -rf /var/lib/apt/lists/*
ENV NODE_ENV=production NEXT_TELEMETRY_DISABLED=1 HOSTNAME=0.0.0.0 PORT=3000
WORKDIR /app
COPY --from=build --chown=node:node /repo/apps/${APP}/.next/standalone ./
COPY --from=build --chown=node:node /repo/apps/${APP}/.next/static ./apps/${APP}/.next/static
RUN printf '#!/bin/sh\nexec node apps/%s/server.js\n' "${APP}" > /app/start.sh && chmod +x /app/start.sh
USER node
EXPOSE 3000
ENTRYPOINT ["/usr/bin/tini", "--"]
CMD ["/app/start.sh"]
