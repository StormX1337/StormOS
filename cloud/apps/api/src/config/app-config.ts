import { z } from 'zod';

const bool = z
  .enum(['true', 'false', '1', '0'])
  .transform((value) => value === 'true' || value === '1');

const envSchema = z.object({
  NODE_ENV: z.enum(['development', 'test', 'production']).default('development'),
  PORT: z.coerce.number().int().min(1).max(65535).default(4000),
  DATABASE_URL: z.string().min(1),
  REDIS_URL: z.string().url().optional(),
  TRUST_PROXY: bool.default('false'),
  CORS_ORIGINS: z.string().default('http://localhost:3000,http://localhost:3001'),
  WEB_ORIGIN: z.string().url().default('http://localhost:3000'),

  JWT_ACCESS_SECRET: z.string().min(32, 'JWT_ACCESS_SECRET must be at least 32 characters.'),
  JWT_ACCESS_TTL_SECONDS: z.coerce.number().int().min(60).max(3600).default(900),
  REFRESH_TOKEN_TTL_DAYS: z.coerce.number().int().min(1).max(90).default(30),

  /** PKCS#8 PEM of the P-256 key that signs entitlement tokens (literal PEM, "\n"-escaped PEM, or base64 of the PEM). */
  ENTITLEMENT_PRIVATE_KEY: z.string().min(1),
  ENTITLEMENT_TOKEN_TTL_HOURS: z.coerce.number().int().min(1).max(24 * 14).default(72),
  MAX_DEVICES_PER_USER: z.coerce.number().int().min(1).max(100).default(10),

  STRIPE_SECRET_KEY: z.string().startsWith('sk_').optional(),
  STRIPE_WEBHOOK_SECRET: z.string().startsWith('whsec_').optional(),
  STRIPE_PRICE_PRO_MONTH: z.string().optional(),
  STRIPE_PRICE_PRO_YEAR: z.string().optional(),
  STRIPE_PRICE_ULTIMATE_MONTH: z.string().optional(),
  STRIPE_PRICE_ULTIMATE_YEAR: z.string().optional(),

  AI_PROVIDER: z.enum(['rules', 'anthropic']).default('rules'),
  ANTHROPIC_API_KEY: z.string().optional(),
  ANTHROPIC_MODEL: z.string().default('claude-opus-5'),
  AI_DAILY_LIMIT: z.coerce.number().int().min(1).max(10_000).default(50),
});

export type AppConfig = Readonly<z.infer<typeof envSchema> & { corsOrigins: readonly string[] }>;

export const APP_CONFIG = Symbol('APP_CONFIG');

/** Validates the environment once at startup; the process refuses to start with an unsafe configuration. */
export function loadConfig(env: NodeJS.ProcessEnv = process.env): AppConfig {
  const parsed = envSchema.safeParse(env);
  if (!parsed.success) {
    const details = parsed.error.issues.map((issue) => `${issue.path.join('.')}: ${issue.message}`).join('; ');
    throw new Error(`Invalid configuration: ${details}`);
  }

  const config = parsed.data;
  if (config.NODE_ENV === 'production' && config.STRIPE_SECRET_KEY?.startsWith('sk_test_')) {
    throw new Error('Invalid configuration: a Stripe test key is configured in production.');
  }

  if (config.AI_PROVIDER === 'anthropic' && !config.ANTHROPIC_API_KEY) {
    throw new Error('Invalid configuration: AI_PROVIDER=anthropic requires ANTHROPIC_API_KEY.');
  }

  const corsOrigins = config.CORS_ORIGINS.split(',').map((origin) => origin.trim()).filter(Boolean);
  return Object.freeze({ ...config, corsOrigins });
}
