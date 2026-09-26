import { Controller, Get, HttpStatus, Res } from '@nestjs/common';
import { SkipThrottle } from '@nestjs/throttler';
import type { Response } from 'express';
import type { Announcement } from '@storm/types';
import { Public } from '../auth/decorators';
import { PrismaService } from '../common/prisma.service';
import { RedisService } from '../common/redis.service';
import { EntitlementSigner } from '../entitlements/entitlement-signer';

export const ANNOUNCEMENTS_CACHE_KEY = 'storm:announcements';

@Controller()
export class SystemController {
  constructor(
    private readonly prisma: PrismaService,
    private readonly redis: RedisService,
    private readonly signer: EntitlementSigner,
  ) {}

  @Public()
  @SkipThrottle()
  @Get('health')
  async health(@Res({ passthrough: true }) response: Response) {
    let database: 'ok' | 'unavailable' = 'ok';
    try {
      await this.prisma.$queryRaw`SELECT 1`;
    } catch {
      database = 'unavailable';
    }

    const redis = await this.redis.ping();
    const healthy = database === 'ok' && redis !== 'unavailable';
    response.status(healthy ? HttpStatus.OK : HttpStatus.SERVICE_UNAVAILABLE);
    return { status: healthy ? 'ok' : 'degraded', database, redis, time: new Date().toISOString() };
  }

  @Public()
  @Get('system/announcements')
  announcements(): Promise<Announcement[]> {
    return this.redis.cached(ANNOUNCEMENTS_CACHE_KEY, 60, async () => {
      const now = new Date();
      const rows = await this.prisma.announcement.findMany({
        where: { active: true, AND: [{ OR: [{ startsAt: null }, { startsAt: { lte: now } }] }, { OR: [{ endsAt: null }, { endsAt: { gt: now } }] }] },
        orderBy: { createdAt: 'desc' },
        take: 10,
      });
      return rows.map((row) => ({ id: row.id, title: row.title, body: row.body, severity: row.severity.toLowerCase() as Announcement['severity'] }));
    });
  }

  /** Public key that verifies entitlement tokens; shipped with the installer as Licensing:PublicKeyPem. */
  @Public()
  @Get('system/entitlement-key')
  entitlementKey() {
    return { algorithm: 'ES256', keyId: this.signer.keyId, publicKeyPem: this.signer.publicKeyPem };
  }
}
