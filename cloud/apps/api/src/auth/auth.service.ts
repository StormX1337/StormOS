import { Inject, Injectable } from '@nestjs/common';
import { Prisma } from '@storm/database';
import type { AuthResponse, AuthTokens, UserRole } from '@storm/types';
import { randomUUID } from 'node:crypto';
import { ApiError } from '../common/api-error';
import { AuditService } from '../common/audit.service';
import { PrismaService } from '../common/prisma.service';
import { APP_CONFIG, type AppConfig } from '../config/app-config';
import { PasswordService } from './password.service';
import { TokenService } from './token.service';

interface ClientInfo {
  ip: string | null;
  userAgent: string | null;
}

@Injectable()
export class AuthService {
  constructor(
    private readonly prisma: PrismaService,
    private readonly passwords: PasswordService,
    private readonly tokens: TokenService,
    private readonly audit: AuditService,
    @Inject(APP_CONFIG) private readonly config: AppConfig,
  ) {}

  async register(email: string, password: string, client: ClientInfo): Promise<AuthResponse> {
    const passwordHash = await this.passwords.hash(password);
    try {
      const user = await this.prisma.user.create({ data: { email, passwordHash, lastLoginAt: new Date() } });
      await this.audit.record('auth.register', { actorId: user.id, ip: client.ip });
      return { tokens: await this.issue(user.id, user.email, 'user', client, randomUUID()), email: user.email };
    } catch (error) {
      if (error instanceof Prisma.PrismaClientKnownRequestError && error.code === 'P2002') {
        throw ApiError.conflict('An account with this e-mail address already exists. Sign in instead.');
      }

      throw error;
    }
  }

  async login(email: string, password: string, client: ClientInfo): Promise<AuthResponse> {
    const user = await this.prisma.user.findUnique({ where: { email } });
    const valid = await this.passwords.verify(password, user?.passwordHash ?? null);
    if (!user || !valid) {
      await this.audit.record('auth.login_failed', { target: email, ip: client.ip });
      throw ApiError.unauthorized('E-mail or password is incorrect.');
    }

    if (user.disabled) {
      throw ApiError.forbidden('This account is disabled. Contact support.');
    }

    await this.prisma.user.update({ where: { id: user.id }, data: { lastLoginAt: new Date() } });
    await this.audit.record('auth.login', { actorId: user.id, ip: client.ip });
    return { tokens: await this.issue(user.id, user.email, user.role.toLowerCase() as UserRole, client, randomUUID()), email: user.email };
  }

  /**
   * Rotates a refresh token. Presenting an already-rotated token is treated as theft:
   * the whole token family is revoked and the user must sign in again.
   */
  async refresh(refreshToken: string, client: ClientInfo): Promise<AuthTokens> {
    const stored = await this.prisma.refreshToken.findUnique({ where: { tokenHash: TokenService.hashRefresh(refreshToken) }, include: { user: true } });
    if (!stored) {
      throw ApiError.unauthorized('Your session expired. Please sign in again.');
    }

    if (stored.revokedAt) {
      await this.prisma.refreshToken.updateMany({ where: { familyId: stored.familyId, revokedAt: null }, data: { revokedAt: new Date() } });
      await this.audit.record('auth.refresh_reuse_detected', { actorId: stored.userId, ip: client.ip });
      throw ApiError.unauthorized('Your session was ended for security reasons. Please sign in again.');
    }

    if (stored.expiresAt < new Date() || stored.user.disabled) {
      throw ApiError.unauthorized('Your session expired. Please sign in again.');
    }

    const role = stored.user.role.toLowerCase() as UserRole;
    const next = this.tokens.newRefreshToken();
    const claimed = await this.prisma.refreshToken.updateMany({ where: { id: stored.id, revokedAt: null }, data: { revokedAt: new Date() } });
    if (claimed.count !== 1) {
      throw ApiError.unauthorized('Your session expired. Please sign in again.');
    }

    const created = await this.prisma.refreshToken.create({
      data: { userId: stored.userId, tokenHash: next.hash, familyId: stored.familyId, expiresAt: this.refreshExpiry(), userAgent: client.userAgent },
    });
    await this.prisma.refreshToken.update({ where: { id: stored.id }, data: { replacedBy: created.id } });
    return {
      accessToken: await this.tokens.signAccess({ sub: stored.userId, email: stored.user.email, role }),
      refreshToken: next.token,
      expiresIn: this.tokens.accessTtlSeconds,
    };
  }

  async logout(refreshToken: string): Promise<void> {
    await this.prisma.refreshToken.updateMany({
      where: { tokenHash: TokenService.hashRefresh(refreshToken), revokedAt: null },
      data: { revokedAt: new Date() },
    });
  }

  /** Ends every session of a user (password change, account disable). */
  async revokeAll(userId: string): Promise<void> {
    await this.prisma.refreshToken.updateMany({ where: { userId, revokedAt: null }, data: { revokedAt: new Date() } });
  }

  private async issue(userId: string, email: string, role: UserRole, client: ClientInfo, familyId: string): Promise<AuthTokens> {
    const refresh = this.tokens.newRefreshToken();
    await this.prisma.refreshToken.create({
      data: { userId, tokenHash: refresh.hash, familyId, expiresAt: this.refreshExpiry(), userAgent: client.userAgent?.slice(0, 256) ?? null },
    });
    return { accessToken: await this.tokens.signAccess({ sub: userId, email, role }), refreshToken: refresh.token, expiresIn: this.tokens.accessTtlSeconds };
  }

  private refreshExpiry(): Date {
    return new Date(Date.now() + this.config.REFRESH_TOKEN_TTL_DAYS * 24 * 3600 * 1000);
  }
}
