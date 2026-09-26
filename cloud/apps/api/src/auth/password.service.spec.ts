import { describe, expect, it } from 'vitest';
import { PasswordService } from './password.service';

describe('PasswordService', () => {
  const passwords = new PasswordService();

  it('hashes with a random salt and verifies', async () => {
    const a = await passwords.hash('correct horse battery staple');
    const b = await passwords.hash('correct horse battery staple');
    expect(a).not.toBe(b);
    expect(a.startsWith('scrypt$32768$8$1$')).toBe(true);
    expect(await passwords.verify('correct horse battery staple', a)).toBe(true);
    expect(await passwords.verify('wrong password here', a)).toBe(false);
  });

  it('never accepts unknown users or malformed hashes', async () => {
    expect(await passwords.verify('anything at all', null)).toBe(false);
    expect(await passwords.verify('anything at all', 'md5$abc')).toBe(false);
  });
});
