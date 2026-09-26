import { Inject, Injectable } from '@nestjs/common';
import type { Tier } from '@storm/database';
import { TIER_FEATURES, maxTier, type EntitlementSummary, type EntitlementTokenResponse, type LicenseTier } from '@storm/types';
import { ApiError } from '../common/api-error';
import { PrismaService } from '../common/prisma.service';
import { APP_CONFIG, type AppConfig } from '../config/app-config';
import { EntitlementSigner } from './entitlement-signer';

/** Days a past-due subscription keeps its features while Stripe retries the payment. */
const PAST_DUE_GRACE_DAYS = 7;

export const toLicenseTier = (tier: Tier): LicenseTier => tier.toLowerCase() as LicenseTier;

/** Pure tier resolution: the best of an active subscription and an unexpired complimentary grant. */
export function resolveTier(
  subscriptions: ReadonlyArray<{ tier: Tier; status: string; currentPeriodEnd: Date }>,
  comp: { tier: Tier | null; expiresAt: Date | null },
  now: Date,
): { tier: LicenseTier; source: EntitlementSummary['source']; expiresAt: Date | null } {
  let best: { tier: LicenseTier; source: EntitlementSummary['source']; expiresAt: Date | null } = { tier: 'free', source: 'free', expiresAt: null };
  for (const subscription of subscriptions) {
    const graceEnd = new Date(subscription.currentPeriodEnd.getTime() + PAST_DUE_GRACE_DAYS * 86_400_000);
    const valid =
      ((subscription.status === 'ACTIVE' || subscription.status === 'TRIALING') && subscription.currentPeriodEnd > now) ||
      (subscription.status === 'PAST_DUE' && graceEnd > now);
    const tier = toLicenseTier(subscription.tier);
    if (valid && maxTier(best.tier, tier) === tier && tier !== best.tier) {
      best = { tier, source: 'subscription', expiresAt: subscription.currentPeriodEnd };
    }
  }

  if (comp.tier && (comp.expiresAt === null || comp.expiresAt > now)) {
    const tier = toLicenseTier(comp.tier);
    if (maxTier(best.tier, tier) === tier && tier !== best.tier) {
      best = { tier, source: 'complimentary', expiresAt: comp.expiresAt };
    }
  }

  return best;
}

@Injectable()
export class EntitlementsService {
  constructor(
    private readonly prisma: PrismaService,
    private readonly signer: EntitlementSigner,
    @Inject(APP_CONFIG) private readonly config: AppConfig,
  ) {}

  async resolve(userId: string): Promise<EntitlementSummary> {
    const user = await this.prisma.user.findUnique({
      where: { id: userId },
      select: { compTier: true, compExpiresAt: true, subscriptions: { select: { tier: true, status: true, currentPeriodEnd: true } } },
    });
    if (!user) {
      throw ApiError.notFound('Account not found.');
    }

    const resolved = resolveTier(user.subscriptions, { tier: user.compTier, expiresAt: user.compExpiresAt }, new Date());
    return { tier: resolved.tier, features: [...TIER_FEATURES[resolved.tier]], source: resolved.source, expiresAt: resolved.expiresAt?.toISOString() ?? null };
  }

  /** Issues a device-bound, time-limited license token the desktop app verifies offline. */
  async issueDeviceToken(userId: string, deviceId: string): Promise<EntitlementTokenResponse> {
    const device = await this.prisma.device.findFirst({ where: { id: deviceId, userId, revokedAt: null } });
    if (!device) {
      throw ApiError.notFound('This PC is not registered to your account. Register it again from STORM OS.');
    }

    await this.prisma.device.update({ where: { id: device.id }, data: { lastSeenAt: new Date() } });
    const summary = await this.resolve(userId);
    const now = Math.floor(Date.now() / 1000);
    let exp = now + this.config.ENTITLEMENT_TOKEN_TTL_HOURS * 3600;
    if (summary.expiresAt) {
      // Never outlive the paid period (plus the grace window the client allows via clock skew).
      exp = Math.min(exp, Math.floor(new Date(summary.expiresAt).getTime() / 1000) + PAST_DUE_GRACE_DAYS * 86_400);
    }

    const token = this.signer.sign({ sub: userId, did: device.id, tier: summary.tier, features: summary.features, iat: now, nbf: now - 60, exp });
    return { token, tier: summary.tier, features: summary.features, expiresAt: new Date(exp * 1000).toISOString() };
  }
}
