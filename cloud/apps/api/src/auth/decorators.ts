import { createParamDecorator, ExecutionContext, SetMetadata } from '@nestjs/common';
import type { Request } from 'express';
import type { Feature, UserRole } from '@storm/types';

export interface AuthUser {
  id: string;
  email: string;
  role: UserRole;
}

export type AuthenticatedRequest = Request & { user?: AuthUser };

export const IS_PUBLIC = 'storm:public';
export const ROLES = 'storm:roles';
export const FEATURE = 'storm:feature';

/** Marks an endpoint as reachable without a signed-in user. */
export const Public = (): MethodDecorator & ClassDecorator => SetMetadata(IS_PUBLIC, true);

/** Restricts an endpoint to the given roles. */
export const Roles = (...roles: UserRole[]): MethodDecorator & ClassDecorator => SetMetadata(ROLES, roles);

/** Requires a license feature (checked server-side against the user's entitlements). */
export const RequireFeature = (feature: Feature): MethodDecorator & ClassDecorator => SetMetadata(FEATURE, feature);

export const CurrentUser = createParamDecorator((_data: unknown, context: ExecutionContext): AuthUser => {
  const request = context.switchToHttp().getRequest<AuthenticatedRequest>();
  if (!request.user) {
    throw new Error('CurrentUser used on a public endpoint.');
  }

  return request.user;
});

export const ClientIp = createParamDecorator((_data: unknown, context: ExecutionContext): string | null =>
  context.switchToHttp().getRequest<Request>().ip ?? null);
