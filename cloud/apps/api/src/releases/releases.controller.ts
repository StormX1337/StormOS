import { Controller, Get, Query } from '@nestjs/common';
import type { ReleaseInfo } from '@storm/types';
import { releaseQuerySchema } from '@storm/validation';
import { Public } from '../auth/decorators';
import { ApiError, parseInput } from '../common/api-error';
import { PrismaService } from '../common/prisma.service';
import { RedisService } from '../common/redis.service';

export const releaseCacheKey = (channel: string, arch: string) => `storm:release:${channel}:${arch}`;

@Controller('releases')
export class ReleasesController {
  constructor(
    private readonly prisma: PrismaService,
    private readonly redis: RedisService,
  ) {}

  /** Latest published installer; the desktop app verifies its SHA-256 and Authenticode signature before running it. */
  @Public()
  @Get('latest')
  async latest(@Query() query: unknown): Promise<ReleaseInfo> {
    const { channel, arch } = parseInput(releaseQuerySchema, query);
    const release = await this.redis.cached(releaseCacheKey(channel, arch), 60, async () => {
      const channels = channel === 'beta' ? (['BETA', 'STABLE'] as const) : (['STABLE'] as const);
      const found = await this.prisma.release.findFirst({
        where: { channel: { in: [...channels] }, arch, publishedAt: { lte: new Date() } },
        orderBy: { publishedAt: 'desc' },
      });
      return found
        ? { version: found.version, channel: found.channel.toLowerCase() as ReleaseInfo['channel'], url: found.url, sha256: found.sha256, notes: found.notes ?? undefined, publishedAt: found.publishedAt.toISOString(), sizeBytes: Number(found.sizeBytes) }
        : null;
    });
    if (!release) {
      throw ApiError.notFound('No release is available on this channel yet.');
    }

    return release;
  }
}
