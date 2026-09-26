import { generateKeyPairSync, verify } from 'node:crypto';
import { describe, expect, it } from 'vitest';
import type { AppConfig } from '../config/app-config';
import { EntitlementSigner, readPem } from './entitlement-signer';
import { resolveTier } from './entitlements.service';

const key = generateKeyPairSync('ec', { namedCurve: 'prime256v1' }).privateKey.export({ type: 'pkcs8', format: 'pem' }).toString();

describe('EntitlementSigner', () => {
  it('produces ES256 compact JWS verifiable with the published key (desktop verifier format)', () => {
    const signer = new EntitlementSigner({ ENTITLEMENT_PRIVATE_KEY: key } as AppConfig);
    const token = signer.sign({ sub: 'u1', did: 'd1', tier: 'pro', features: ['overlay'], iat: 1, nbf: 1, exp: 2 });
    const [header, payload, signature] = token.split('.');
    expect(JSON.parse(Buffer.from(header!, 'base64url').toString())).toMatchObject({ alg: 'ES256', typ: 'JWT' });
    expect(JSON.parse(Buffer.from(payload!, 'base64url').toString())).toMatchObject({ iss: 'storm-cloud', did: 'd1', tier: 'pro', features: ['overlay'] });
    const ok = verify('sha256', Buffer.from(`${header}.${payload}`), { key: signer.publicKeyPem, dsaEncoding: 'ieee-p1363' }, Buffer.from(signature!, 'base64url'));
    expect(ok).toBe(true);
    expect(Buffer.from(signature!, 'base64url')).toHaveLength(64);
  });

  it('accepts base64 and escaped PEM, rejects non P-256 keys', () => {
    expect(readPem(Buffer.from(key).toString('base64'))).toBe(key);
    expect(readPem(key.replace(/\n/g, '\\n')).trim()).toBe(key.trim());
    const rsa = generateKeyPairSync('rsa', { modulusLength: 2048 }).privateKey.export({ type: 'pkcs8', format: 'pem' }).toString();
    expect(() => new EntitlementSigner({ ENTITLEMENT_PRIVATE_KEY: rsa } as AppConfig)).toThrow(/P-256/);
  });
});

describe('resolveTier', () => {
  const now = new Date('2026-09-26T12:00:00Z');
  const future = new Date('2026-10-26T12:00:00Z');
  const past = new Date('2026-09-01T12:00:00Z');

  it('defaults to free', () => {
    expect(resolveTier([], { tier: null, expiresAt: null }, now)).toEqual({ tier: 'free', source: 'free', expiresAt: null });
  });

  it('uses the best active subscription and ignores expired or canceled ones', () => {
    const result = resolveTier(
      [
        { tier: 'ULTIMATE', status: 'CANCELED', currentPeriodEnd: future },
        { tier: 'PRO', status: 'ACTIVE', currentPeriodEnd: future },
        { tier: 'ULTIMATE', status: 'ACTIVE', currentPeriodEnd: past },
      ],
      { tier: null, expiresAt: null },
      now,
    );
    expect(result).toEqual({ tier: 'pro', source: 'subscription', expiresAt: future });
  });

  it('keeps past-due subscriptions during the grace period only', () => {
    const recent = new Date('2026-09-24T12:00:00Z');
    expect(resolveTier([{ tier: 'PRO', status: 'PAST_DUE', currentPeriodEnd: recent }], { tier: null, expiresAt: null }, now).tier).toBe('pro');
    expect(resolveTier([{ tier: 'PRO', status: 'PAST_DUE', currentPeriodEnd: past }], { tier: null, expiresAt: null }, now).tier).toBe('free');
  });

  it('applies unexpired complimentary grants when higher', () => {
    expect(resolveTier([{ tier: 'PRO', status: 'ACTIVE', currentPeriodEnd: future }], { tier: 'ULTIMATE', expiresAt: null }, now).source).toBe('complimentary');
    expect(resolveTier([], { tier: 'ULTIMATE', expiresAt: past }, now).tier).toBe('free');
  });
});
