import { Body, Controller, Get, Headers, HttpCode, HttpStatus, Post } from '@nestjs/common';
import { Throttle } from '@nestjs/throttler';
import { loginSchema, refreshSchema, registerSchema } from '@storm/validation';
import { parseInput } from '../common/api-error';
import { EntitlementsService } from '../entitlements/entitlements.service';
import { AuthService } from './auth.service';
import { ClientIp, CurrentUser, Public, type AuthUser } from './decorators';

const strict = { default: { limit: 10, ttl: 60_000 } };

@Controller('auth')
export class AuthController {
  constructor(
    private readonly auth: AuthService,
    private readonly entitlements: EntitlementsService,
  ) {}

  @Public()
  @Throttle(strict)
  @Post('register')
  register(@Body() body: unknown, @ClientIp() ip: string | null, @Headers('user-agent') userAgent?: string) {
    const input = parseInput(registerSchema, body);
    return this.auth.register(input.email, input.password, { ip, userAgent: userAgent ?? null });
  }

  @Public()
  @Throttle(strict)
  @HttpCode(HttpStatus.OK)
  @Post('login')
  login(@Body() body: unknown, @ClientIp() ip: string | null, @Headers('user-agent') userAgent?: string) {
    const input = parseInput(loginSchema, body);
    return this.auth.login(input.email, input.password, { ip, userAgent: userAgent ?? null });
  }

  @Public()
  @Throttle({ default: { limit: 30, ttl: 60_000 } })
  @HttpCode(HttpStatus.OK)
  @Post('refresh')
  refresh(@Body() body: unknown, @ClientIp() ip: string | null, @Headers('user-agent') userAgent?: string) {
    return this.auth.refresh(parseInput(refreshSchema, body).refreshToken, { ip, userAgent: userAgent ?? null });
  }

  @Public()
  @HttpCode(HttpStatus.NO_CONTENT)
  @Post('logout')
  async logout(@Body() body: unknown): Promise<void> {
    await this.auth.logout(parseInput(refreshSchema, body).refreshToken);
  }

  @Get('me')
  async me(@CurrentUser() user: AuthUser) {
    return { ...user, entitlements: await this.entitlements.resolve(user.id) };
  }
}
