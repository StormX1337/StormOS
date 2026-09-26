import { Inject, Injectable, Logger, OnModuleDestroy } from '@nestjs/common';
import Redis from 'ioredis';
import { APP_CONFIG, type AppConfig } from '../config/app-config';

/** Optional Redis: caching and distributed rate limits. Everything keeps working without it. */
@Injectable()
export class RedisService implements OnModuleDestroy {
  private readonly logger = new Logger(RedisService.name);
  readonly client: Redis | null;

  constructor(@Inject(APP_CONFIG) config: AppConfig) {
    this.client = config.REDIS_URL
      ? new Redis(config.REDIS_URL, { maxRetriesPerRequest: 2, enableOfflineQueue: false, lazyConnect: false })
      : null;
    this.client?.on('error', (error) => this.logger.warn(`Redis unavailable: ${error.message}`));
  }

  get available(): boolean {
    return this.client?.status === 'ready';
  }

  /** Returns a cached JSON value or computes, stores and returns it. Cache failures fall back to computing. */
  async cached<T>(key: string, ttlSeconds: number, compute: () => Promise<T>): Promise<T> {
    if (this.available) {
      try {
        const hit = await this.client!.get(key);
        if (hit !== null) return JSON.parse(hit) as T;
      } catch (error) {
        this.logger.debug(`Cache read failed for ${key}: ${String(error)}`);
      }
    }

    const value = await compute();
    if (this.available) {
      this.client!.set(key, JSON.stringify(value), 'EX', ttlSeconds).catch(() => undefined);
    }

    return value;
  }

  async invalidate(...keys: string[]): Promise<void> {
    if (this.available && keys.length > 0) {
      await this.client!.del(...keys).catch(() => undefined);
    }
  }

  async ping(): Promise<'ok' | 'disabled' | 'unavailable'> {
    if (!this.client) return 'disabled';
    try {
      return (await this.client.ping()) === 'PONG' ? 'ok' : 'unavailable';
    } catch {
      return 'unavailable';
    }
  }

  async onModuleDestroy(): Promise<void> {
    await this.client?.quit().catch(() => undefined);
  }
}
