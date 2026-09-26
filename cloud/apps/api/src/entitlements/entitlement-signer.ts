import { Inject, Injectable } from '@nestjs/common';
import { createHash, createPrivateKey, createPublicKey, sign, type KeyObject } from 'node:crypto';
import type { Feature, LicenseTier } from '@storm/types';
import { APP_CONFIG, type AppConfig } from '../config/app-config';

export interface EntitlementClaims {
  sub: string;
  did: string;
  tier: LicenseTier;
  features: readonly Feature[];
  iat: number;
  nbf: number;
  exp: number;
}

/** Accepts a literal PEM, a PEM with escaped newlines, or a base64-encoded PEM (convenient for container secrets). */
export function readPem(value: string): string {
  const trimmed = value.trim();
  if (trimmed.startsWith('-----BEGIN')) {
    return trimmed.replace(/\\n/g, '\n');
  }

  return Buffer.from(trimmed, 'base64').toString('utf8');
}

/**
 * Signs offline license tokens as compact JWS with ES256 (P-256, IEEE P1363 signature encoding),
 * the exact format verified by the desktop client's EntitlementTokenVerifier.
 */
@Injectable()
export class EntitlementSigner {
  private readonly privateKey: KeyObject;
  readonly publicKeyPem: string;
  readonly keyId: string;

  constructor(@Inject(APP_CONFIG) config: AppConfig) {
    this.privateKey = createPrivateKey(readPem(config.ENTITLEMENT_PRIVATE_KEY));
    if (this.privateKey.asymmetricKeyType !== 'ec' || this.privateKey.asymmetricKeyDetails?.namedCurve !== 'prime256v1') {
      throw new Error('ENTITLEMENT_PRIVATE_KEY must be a P-256 (prime256v1) EC private key.');
    }

    const publicKey = createPublicKey(this.privateKey);
    this.publicKeyPem = publicKey.export({ type: 'spki', format: 'pem' }).toString();
    this.keyId = createHash('sha256').update(publicKey.export({ type: 'spki', format: 'der' })).digest('base64url').slice(0, 16);
  }

  sign(claims: EntitlementClaims): string {
    const header = Buffer.from(JSON.stringify({ alg: 'ES256', typ: 'JWT', kid: this.keyId })).toString('base64url');
    const payload = Buffer.from(JSON.stringify({ iss: 'storm-cloud', ...claims })).toString('base64url');
    const signature = sign('sha256', Buffer.from(`${header}.${payload}`, 'ascii'), { key: this.privateKey, dsaEncoding: 'ieee-p1363' });
    return `${header}.${payload}.${signature.toString('base64url')}`;
  }
}
