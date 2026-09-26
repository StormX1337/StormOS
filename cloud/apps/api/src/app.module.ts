import { Module } from '@nestjs/common';
import { APP_FILTER, APP_GUARD } from '@nestjs/core';
import { JwtModule } from '@nestjs/jwt';
import { ThrottlerGuard, ThrottlerModule } from '@nestjs/throttler';
import { AdminController } from './admin/admin.controller';
import { AiController } from './ai/ai.controller';
import { AiService } from './ai/ai.service';
import { ClaudeAnalysisProvider } from './ai/claude-analysis.provider';
import { AuthController } from './auth/auth.controller';
import { AuthGuard } from './auth/auth.guard';
import { AuthService } from './auth/auth.service';
import { PasswordService } from './auth/password.service';
import { TokenService } from './auth/token.service';
import { BenchmarksController } from './benchmarks/benchmarks.controller';
import { BillingController } from './billing/billing.controller';
import { BillingService } from './billing/billing.service';
import { CoreModule } from './common/core.module';
import { HttpExceptionFilter } from './common/http-exception.filter';
import { RedisThrottlerStorage } from './common/redis-throttler.storage';
import { RedisService } from './common/redis.service';
import { DevicesController } from './devices/devices.controller';
import { EntitlementSigner } from './entitlements/entitlement-signer';
import { EntitlementsController } from './entitlements/entitlements.controller';
import { EntitlementsService } from './entitlements/entitlements.service';
import { ProfilesController } from './profiles/profiles.controller';
import { RealtimeGateway } from './realtime/realtime.gateway';
import { ReleasesController } from './releases/releases.controller';
import { SessionsController } from './sessions/sessions.controller';
import { SystemController } from './system/system.controller';

@Module({
  imports: [
    CoreModule,
    JwtModule.register({}),
    ThrottlerModule.forRootAsync({
      imports: [CoreModule],
      inject: [RedisService],
      useFactory: (redis: RedisService) => ({
        throttlers: [{ name: 'default', ttl: 60_000, limit: 120 }],
        storage: redis.client ? new RedisThrottlerStorage(redis.client) : undefined,
      }),
    }),
  ],
  controllers: [
    AuthController,
    DevicesController,
    EntitlementsController,
    BillingController,
    BenchmarksController,
    SessionsController,
    ProfilesController,
    ReleasesController,
    SystemController,
    AiController,
    AdminController,
  ],
  providers: [
    PasswordService,
    TokenService,
    AuthService,
    EntitlementSigner,
    EntitlementsService,
    BillingService,
    RealtimeGateway,
    ClaudeAnalysisProvider,
    AiService,
    { provide: APP_GUARD, useClass: ThrottlerGuard },
    { provide: APP_GUARD, useClass: AuthGuard },
    { provide: APP_FILTER, useClass: HttpExceptionFilter },
  ],
})
export class AppModule {}
