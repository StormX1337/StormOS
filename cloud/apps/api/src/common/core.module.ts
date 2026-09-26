import { Global, Module } from '@nestjs/common';
import { APP_CONFIG, loadConfig, type AppConfig } from '../config/app-config';
import { AuditService } from './audit.service';
import { PrismaService } from './prisma.service';
import { RedisService } from './redis.service';

/** Configuration and infrastructure shared by every feature (and by the throttler's async factory). */
@Global()
@Module({
  providers: [{ provide: APP_CONFIG, useFactory: (): AppConfig => loadConfig() }, PrismaService, RedisService, AuditService],
  exports: [APP_CONFIG, PrismaService, RedisService, AuditService],
})
export class CoreModule {}
