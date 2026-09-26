import { Injectable } from '@nestjs/common';
import { randomBytes, scrypt, timingSafeEqual, type ScryptOptions } from 'node:crypto';

const N = 1 << 15;
const R = 8;
const P = 1;
const KEY_LENGTH = 64;

function derive(password: string, salt: Buffer, options: ScryptOptions): Promise<Buffer> {
  return new Promise((resolve, reject) => {
    scrypt(password.normalize('NFKC'), salt, KEY_LENGTH, { ...options, maxmem: 128 * N * R * 2 }, (error, key) => (error ? reject(error) : resolve(key)));
  });
}

/**
 * scrypt password hashing (OWASP-recommended parameters N=2^15, r=8, p=1), stored as
 * scrypt$N$r$p$salt$hash so parameters can be raised later without breaking existing hashes.
 */
@Injectable()
export class PasswordService {
  private readonly dummy = `scrypt$${N}$${R}$${P}$${randomBytes(16).toString('base64')}$${randomBytes(KEY_LENGTH).toString('base64')}`;

  async hash(password: string): Promise<string> {
    const salt = randomBytes(16);
    const key = await derive(password, salt, { N, r: R, p: P });
    return `scrypt$${N}$${R}$${P}$${salt.toString('base64')}$${key.toString('base64')}`;
  }

  /** Verifies in constant time. Pass null for unknown users so timing does not reveal account existence. */
  async verify(password: string, stored: string | null): Promise<boolean> {
    const [scheme, n, r, p, salt, hash] = (stored ?? this.dummy).split('$');
    if (scheme !== 'scrypt' || !n || !r || !p || !salt || !hash) {
      return false;
    }

    const expected = Buffer.from(hash, 'base64');
    const actual = await derive(password, Buffer.from(salt, 'base64'), { N: Number(n), r: Number(r), p: Number(p) });
    return stored !== null && expected.length === actual.length && timingSafeEqual(expected, actual);
  }
}
