import type { ThrottlerStorage } from '@nestjs/throttler';
import type Redis from 'ioredis';

/** Fixed-window counters in Redis so rate limits hold across API instances. ttl/blockDuration are milliseconds. */
export class RedisThrottlerStorage implements ThrottlerStorage {
  constructor(private readonly redis: Redis) {}

  async increment(key: string, ttl: number, limit: number, blockDuration: number, throttlerName: string) {
    const hitKey = `storm:throttle:${throttlerName}:${key}`;
    const blockKey = `${hitKey}:blocked`;
    const results = await this.redis.multi().incr(hitKey).pexpire(hitKey, ttl, 'NX').pttl(hitKey).pttl(blockKey).exec();
    const totalHits = Number(results?.[0]?.[1] ?? 1);
    const hitTtl = Number(results?.[2]?.[1] ?? ttl);
    let blockTtl = Number(results?.[3]?.[1] ?? -2);
    if (blockTtl <= 0 && totalHits > limit) {
      await this.redis.set(blockKey, '1', 'PX', blockDuration, 'NX');
      blockTtl = blockDuration;
    }

    const isBlocked = blockTtl > 0;
    return {
      totalHits,
      timeToExpire: Math.max(0, Math.ceil(hitTtl / 1000)),
      isBlocked,
      timeToBlockExpire: isBlocked ? Math.ceil(blockTtl / 1000) : 0,
    };
  }
}
