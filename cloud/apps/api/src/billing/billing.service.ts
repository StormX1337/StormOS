import { Inject, Injectable, Logger } from '@nestjs/common';
import { Prisma, type SubscriptionStatus, type Tier } from '@storm/database';
import Stripe from 'stripe';
import { ApiError } from '../common/api-error';
import { AuditService } from '../common/audit.service';
import { PrismaService } from '../common/prisma.service';
import { APP_CONFIG, type AppConfig } from '../config/app-config';
import { RealtimeGateway } from '../realtime/realtime.gateway';

type Tx = Prisma.TransactionClient;

const STATUS: Record<string, SubscriptionStatus> = {
  active: 'ACTIVE',
  trialing: 'TRIALING',
  past_due: 'PAST_DUE',
  canceled: 'CANCELED',
  incomplete: 'INCOMPLETE',
  incomplete_expired: 'CANCELED',
  unpaid: 'UNPAID',
  paused: 'CANCELED',
};

/** Reads the billing period end across Stripe API versions (moved from the subscription to its items in 2025). */
export function periodEnd(subscription: Stripe.Subscription): Date {
  const item = subscription.items.data[0] as (Stripe.SubscriptionItem & { current_period_end?: number }) | undefined;
  const legacy = (subscription as Stripe.Subscription & { current_period_end?: number }).current_period_end;
  const seconds = item?.current_period_end ?? legacy;
  return seconds ? new Date(seconds * 1000) : new Date();
}

@Injectable()
export class BillingService {
  private readonly logger = new Logger(BillingService.name);
  private readonly stripe: Stripe | null;
  private readonly prices: ReadonlyMap<string, { tier: Tier; interval: 'month' | 'year' }>;

  constructor(
    private readonly prisma: PrismaService,
    private readonly audit: AuditService,
    private readonly realtime: RealtimeGateway,
    @Inject(APP_CONFIG) private readonly config: AppConfig,
  ) {
    this.stripe = config.STRIPE_SECRET_KEY ? new Stripe(config.STRIPE_SECRET_KEY, { maxNetworkRetries: 2, timeout: 20_000 }) : null;
    const entries: Array<[string | undefined, Tier, 'month' | 'year']> = [
      [config.STRIPE_PRICE_PRO_MONTH, 'PRO', 'month'],
      [config.STRIPE_PRICE_PRO_YEAR, 'PRO', 'year'],
      [config.STRIPE_PRICE_ULTIMATE_MONTH, 'ULTIMATE', 'month'],
      [config.STRIPE_PRICE_ULTIMATE_YEAR, 'ULTIMATE', 'year'],
    ];
    this.prices = new Map(entries.filter((entry): entry is [string, Tier, 'month' | 'year'] => !!entry[0]).map(([price, tier, interval]) => [price, { tier, interval }]));
  }

  async createCheckout(user: { id: string; email: string }, tier: 'pro' | 'ultimate', interval: 'month' | 'year', idempotencyKey?: string): Promise<{ url: string }> {
    const stripe = this.requireStripe();
    const price = [...this.prices.entries()].find(([, value]) => value.tier === tier.toUpperCase() && value.interval === interval)?.[0];
    if (!price) {
      throw ApiError.unavailable('This plan is not available for purchase right now.');
    }

    const customer = await this.ensureCustomer(stripe, user);
    const session = await stripe.checkout.sessions.create(
      {
        mode: 'subscription',
        customer,
        client_reference_id: user.id,
        line_items: [{ price, quantity: 1 }],
        subscription_data: { metadata: { userId: user.id } },
        allow_promotion_codes: true,
        success_url: `${this.config.WEB_ORIGIN}/account?checkout=success`,
        cancel_url: `${this.config.WEB_ORIGIN}/pricing?checkout=cancelled`,
      },
      idempotencyKey ? { idempotencyKey: `checkout-${user.id}-${idempotencyKey}` } : undefined,
    );
    if (!session.url) {
      throw ApiError.unavailable('Checkout could not be started. Please try again.');
    }

    await this.audit.record('billing.checkout_started', { actorId: user.id, details: { tier, interval } });
    return { url: session.url };
  }

  async createPortal(user: { id: string; email: string }): Promise<{ url: string }> {
    const stripe = this.requireStripe();
    const customer = await this.ensureCustomer(stripe, user);
    const portal = await stripe.billingPortal.sessions.create({ customer, return_url: `${this.config.WEB_ORIGIN}/account` });
    return { url: portal.url };
  }

  /** Verifies the Stripe signature over the raw body, then processes the event exactly once. */
  async handleWebhook(rawBody: Buffer | undefined, signature: string | undefined): Promise<{ received: true; duplicate?: boolean }> {
    const stripe = this.requireStripe();
    if (!this.config.STRIPE_WEBHOOK_SECRET) {
      throw ApiError.unavailable('Billing webhooks are not configured.');
    }

    if (!rawBody || !signature) {
      throw ApiError.badRequest('Missing Stripe signature.');
    }

    let event: Stripe.Event;
    try {
      event = stripe.webhooks.constructEvent(rawBody, signature, this.config.STRIPE_WEBHOOK_SECRET);
    } catch {
      throw ApiError.badRequest('Invalid Stripe signature.');
    }

    return this.processEvent(event);
  }

  /**
   * Idempotent event processing. A transaction-scoped advisory lock serializes concurrent deliveries of the
   * same event; an event marked processed is acknowledged without side effects. On failure nothing is
   * committed and Stripe's retry processes it again.
   */
  async processEvent(event: Stripe.Event): Promise<{ received: true; duplicate?: boolean }> {
    let affectedUser = null as string | null;
    try {
      const duplicate = await this.prisma.$transaction(
        async (tx) => {
          await tx.$executeRaw`SELECT pg_advisory_xact_lock(hashtext(${event.id}))`;
          const existing = await tx.stripeEvent.findUnique({ where: { id: event.id } });
          if (existing?.processedAt) {
            return true;
          }

          affectedUser = await this.apply(tx, event);
          await tx.stripeEvent.upsert({
            where: { id: event.id },
            create: { id: event.id, type: event.type, attempts: 1, processedAt: new Date() },
            update: { attempts: { increment: 1 }, processedAt: new Date(), error: null },
          });
          return false;
        },
        { timeout: 15_000 },
      );
      if (duplicate) {
        return { received: true, duplicate: true };
      }
    } catch (error) {
      this.logger.error(`Stripe event ${event.id} (${event.type}) failed: ${String(error)}`);
      await this.prisma.stripeEvent
        .upsert({
          where: { id: event.id },
          create: { id: event.id, type: event.type, attempts: 1, error: String(error).slice(0, 500) },
          update: { attempts: { increment: 1 }, error: String(error).slice(0, 500) },
        })
        .catch(() => undefined);
      throw error;
    }

    if (affectedUser) {
      this.realtime.notifyEntitlementsChanged(affectedUser);
      await this.audit.record('billing.event', { actorId: affectedUser, target: event.id, details: { type: event.type } });
    }

    return { received: true };
  }

  private async apply(tx: Tx, event: Stripe.Event): Promise<string | null> {
    switch (event.type) {
      case 'checkout.session.completed': {
        const session = event.data.object;
        const customer = typeof session.customer === 'string' ? session.customer : session.customer?.id;
        if (session.client_reference_id && customer) {
          await tx.user.updateMany({ where: { id: session.client_reference_id, stripeCustomerId: null }, data: { stripeCustomerId: customer } });
          return session.client_reference_id;
        }

        return null;
      }

      case 'customer.subscription.created':
      case 'customer.subscription.updated':
      case 'customer.subscription.deleted':
        return this.upsertSubscription(tx, event.data.object, event.type === 'customer.subscription.deleted');

      default:
        return null;
    }
  }

  private async upsertSubscription(tx: Tx, subscription: Stripe.Subscription, deleted: boolean): Promise<string | null> {
    const customer = typeof subscription.customer === 'string' ? subscription.customer : subscription.customer.id;
    const userId =
      subscription.metadata?.userId ??
      (await tx.user.findUnique({ where: { stripeCustomerId: customer }, select: { id: true } }))?.id ??
      null;
    if (!userId || !(await tx.user.findUnique({ where: { id: userId }, select: { id: true } }))) {
      this.logger.warn(`Subscription ${subscription.id} has no matching user; ignored.`);
      return null;
    }

    const priceId = subscription.items.data[0]?.price.id ?? '';
    const plan = this.prices.get(priceId);
    if (!plan) {
      this.logger.warn(`Subscription ${subscription.id} uses unknown price ${priceId}; ignored.`);
      return null;
    }

    const status: SubscriptionStatus = deleted ? 'CANCELED' : (STATUS[subscription.status] ?? 'INCOMPLETE');
    const data = { userId, stripePriceId: priceId, tier: plan.tier, status, currentPeriodEnd: periodEnd(subscription), cancelAtPeriodEnd: subscription.cancel_at_period_end };
    await tx.subscription.upsert({ where: { stripeSubscriptionId: subscription.id }, create: { stripeSubscriptionId: subscription.id, ...data }, update: data });
    await tx.user.updateMany({ where: { id: userId, stripeCustomerId: null }, data: { stripeCustomerId: customer } });
    return userId;
  }

  private async ensureCustomer(stripe: Stripe, user: { id: string; email: string }): Promise<string> {
    const existing = await this.prisma.user.findUnique({ where: { id: user.id }, select: { stripeCustomerId: true } });
    if (existing?.stripeCustomerId) {
      return existing.stripeCustomerId;
    }

    const customer = await stripe.customers.create({ email: user.email, metadata: { userId: user.id } }, { idempotencyKey: `customer-${user.id}` });
    await this.prisma.user.updateMany({ where: { id: user.id, stripeCustomerId: null }, data: { stripeCustomerId: customer.id } });
    return (await this.prisma.user.findUnique({ where: { id: user.id }, select: { stripeCustomerId: true } }))?.stripeCustomerId ?? customer.id;
  }

  private requireStripe(): Stripe {
    if (!this.stripe) {
      throw ApiError.unavailable('Billing is not configured on this server.');
    }

    return this.stripe;
  }
}
