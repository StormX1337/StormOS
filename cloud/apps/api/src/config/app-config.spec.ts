import { describe, expect, it } from 'vitest';
import { loadConfig } from './app-config';

const base = { DATABASE_URL: 'postgresql://localhost/db', JWT_ACCESS_SECRET: 'x'.repeat(32), ENTITLEMENT_PRIVATE_KEY: 'pem' };

describe('loadConfig', () => {
  it('applies safe defaults', () => {
    const config = loadConfig(base);
    expect(config.JWT_ACCESS_TTL_SECONDS).toBe(900);
    expect(config.AI_PROVIDER).toBe('rules');
    expect(config.corsOrigins).toEqual(['http://localhost:3000', 'http://localhost:3001']);
  });

  it('refuses weak or inconsistent configuration', () => {
    expect(() => loadConfig({ ...base, JWT_ACCESS_SECRET: 'short' })).toThrow(/JWT_ACCESS_SECRET/);
    expect(() => loadConfig({ ...base, AI_PROVIDER: 'anthropic' })).toThrow(/ANTHROPIC_API_KEY/);
    expect(() => loadConfig({ ...base, NODE_ENV: 'production', STRIPE_SECRET_KEY: 'sk_test_123' })).toThrow(/test key/);
  });
});
