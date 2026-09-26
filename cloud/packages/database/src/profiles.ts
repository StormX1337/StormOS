import { readdirSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import type { Prisma, PrismaClient } from '@prisma/client';
import { gameProfileSchema, describeZodError } from '@storm/validation';

/** Imports (and publishes) every valid profile JSON in a directory. Invalid files are reported, not imported. */
export async function importProfiles(prisma: PrismaClient, directory: string): Promise<{ imported: number; errors: string[] }> {
  const errors: string[] = [];
  let imported = 0;
  for (const file of readdirSync(directory).filter((name) => name.endsWith('.json'))) {
    const parsed = gameProfileSchema.safeParse(JSON.parse(readFileSync(join(directory, file), 'utf8')));
    if (!parsed.success) {
      errors.push(`${file}: ${describeZodError(parsed.error)}`);
      continue;
    }

    const profile = parsed.data;
    const data = profile as unknown as Prisma.InputJsonObject;
    await prisma.gameProfile.upsert({
      where: { id: profile.id },
      create: { id: profile.id, version: profile.version, name: profile.name, data, published: true },
      update: { version: profile.version, name: profile.name, data },
    });
    imported++;
  }

  return { imported, errors };
}
