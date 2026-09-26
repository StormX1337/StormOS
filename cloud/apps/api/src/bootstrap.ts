import type { INestApplication } from '@nestjs/common';
import type { NestExpressApplication } from '@nestjs/platform-express';
import helmet from 'helmet';
import { APP_CONFIG, type AppConfig } from './config/app-config';
import { RealtimeAdapter } from './realtime/realtime.adapter';

/** Shared HTTP hardening for the server and the end-to-end tests. */
export function configureApp(app: NestExpressApplication): INestApplication {
  const config = app.get<AppConfig>(APP_CONFIG);
  app.setGlobalPrefix('api/v1');
  app.use(helmet({ contentSecurityPolicy: { directives: { defaultSrc: ["'none'"], frameAncestors: ["'none'"] } }, crossOriginResourcePolicy: { policy: 'same-site' } }));
  app.enableCors({ origin: [...config.corsOrigins], methods: ['GET', 'POST', 'PUT', 'PATCH', 'DELETE'], allowedHeaders: ['Authorization', 'Content-Type', 'Idempotency-Key'], maxAge: 600 });
  app.useBodyParser('json', { limit: '256kb' });
  if (config.TRUST_PROXY) {
    app.set('trust proxy', 1);
  }

  app.disable('x-powered-by');
  app.useWebSocketAdapter(new RealtimeAdapter(app, config.corsOrigins));
  app.enableShutdownHooks();
  return app;
}
