import { CanActivate, ExecutionContext, Injectable } from '@nestjs/common';
import { Reflector } from '@nestjs/core';
import type { UserRole } from '@storm/types';
import { ApiError } from '../common/api-error';
import { PrismaService } from '../common/prisma.service';
import { EntitlementsService } from '../entitlements/entitlements.service';
import { FEATURE, IS_PUBLIC, ROLES, type AuthenticatedRequest } from './decorators';
import { TokenService } from './token.service';

/**
 * Global guard: every endpoint requires a valid access token unless marked @Public().
 * The user is re-read on each request so disabled accounts and role changes apply immediately.
 */
@Injectable()
export class AuthGuard implements CanActivate {
  constructor(
    private readonly reflector: Reflector,
    private readonly tokens: TokenService,
    private readonly prisma: PrismaService,
    private readonly entitlements: EntitlementsService,
  ) {}

  async canActivate(context: ExecutionContext): Promise<boolean> {
    if (context.getType() !== 'http') {
      return true;
    }

    const targets = [context.getHandler(), context.getClass()];
    if (this.reflector.getAllAndOverride<boolean>(IS_PUBLIC, targets)) {
      return true;
    }

    const request = context.switchToHttp().getRequest<AuthenticatedRequest>();
    const header = request.headers.authorization;
    const token = header?.startsWith('Bearer ') ? header.slice(7).trim() : null;
    const claims = token ? await this.tokens.verifyAccess(token) : null;
    if (!claims) {
      throw ApiError.unauthorized();
    }

    const user = await this.prisma.user.findUnique({ where: { id: claims.sub }, select: { id: true, email: true, role: true, disabled: true } });
    if (!user || user.disabled) {
      throw ApiError.unauthorized('Your session is no longer valid. Please sign in again.');
    }

    request.user = { id: user.id, email: user.email, role: user.role.toLowerCase() as UserRole };

    const roles = this.reflector.getAllAndOverride<UserRole[] | undefined>(ROLES, targets);
    if (roles && !roles.includes(request.user.role)) {
      throw ApiError.forbidden();
    }

    const feature = this.reflector.getAllAndOverride<string | undefined>(FEATURE, targets);
    if (feature) {
      const summary = await this.entitlements.resolve(user.id);
      if (!(summary.features as string[]).includes(feature)) {
        throw ApiError.entitlement('Your STORM OS plan does not include this feature. Upgrade on the pricing page.');
      }
    }

    return true;
  }
}
