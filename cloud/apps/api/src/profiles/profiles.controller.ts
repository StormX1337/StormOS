import { Controller, Get } from '@nestjs/common';
import { Public } from '../auth/decorators';
import { PrismaService } from '../common/prisma.service';
import { RedisService } from '../common/redis.service';

export const PROFILES_CACHE_KEY = 'storm:profiles:published';

@Controller('profiles')
export class ProfilesController {
  constructor(
    private readonly prisma: PrismaService,
    private readonly redis: RedisService,
  ) {}

  /** Published game profiles, in the same JSON format as the bundled profiles/*.json files. */
  @Public()
  @Get()
  list(): Promise<unknown[]> {
    return this.redis.cached(PROFILES_CACHE_KEY, 300, async () =>
      (await this.prisma.gameProfile.findMany({ where: { published: true }, orderBy: { name: 'asc' }, select: { data: true } })).map((profile) => profile.data),
    );
  }
}
