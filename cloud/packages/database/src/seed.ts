import { existsSync } from 'node:fs';
import { resolve } from 'node:path';
import { scryptSync, randomBytes } from 'node:crypto';
import { PrismaClient } from '../generated/client';
import { emailSchema, passwordSchema } from '@storm/validation';
import { importProfiles } from './profiles';

/**
 * Seeds reference data. No credentials are hard-coded: an administrator is only created when
 * SEED_ADMIN_EMAIL and SEED_ADMIN_PASSWORD are provided in the environment.
 */
async function main(): Promise<void> {
  const prisma = new PrismaClient();
  try {
    const profilesDir = resolve(process.env.SEED_PROFILES_DIR ?? resolve(__dirname, '../../../../profiles'));
    if (existsSync(profilesDir)) {
      const result = await importProfiles(prisma, profilesDir);
      console.log(`Imported ${result.imported} game profiles from ${profilesDir}.`);
      for (const error of result.errors) console.warn(`Skipped ${error}`);
    }

    const email = process.env.SEED_ADMIN_EMAIL;
    const password = process.env.SEED_ADMIN_PASSWORD;
    if (email && password) {
      const normalized = emailSchema.parse(email);
      passwordSchema.parse(password);
      const salt = randomBytes(16);
      const hash = scryptSync(password, salt, 64, { N: 1 << 15, r: 8, p: 1, maxmem: 64 * 1024 * 1024 });
      const passwordHash = `scrypt$32768$8$1$${salt.toString('base64')}$${hash.toString('base64')}`;
      await prisma.user.upsert({
        where: { email: normalized },
        create: { email: normalized, passwordHash, role: 'ADMIN' },
        update: { role: 'ADMIN' },
      });
      console.log(`Administrator ${normalized} is ready.`);
    }
  } finally {
    await prisma.$disconnect();
  }
}

main().catch((error: unknown) => {
  console.error(error);
  process.exitCode = 1;
});
