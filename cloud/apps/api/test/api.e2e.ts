import { generateKeyPairSync, verify } from 'node:crypto';
import { randomUUID } from 'node:crypto';
import type { NestExpressApplication } from '@nestjs/platform-express';
import { Test } from '@nestjs/testing';
import Stripe from 'stripe';
import request from 'supertest';
import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import { AppModule } from '../src/app.module';
import { configureApp } from '../src/bootstrap';
import { PrismaService } from '../src/common/prisma.service';

/**
 * Cloud → API → DB integration test against a real PostgreSQL database.
 * Runs when TEST_DATABASE_URL is set (CI provides a disposable database); skipped otherwise.
 */
const databaseUrl = process.env.TEST_DATABASE_URL;
const suite = databaseUrl ? describe : describe.skip;

suite('STORM Cloud API (e2e)', () => {
  let app: NestExpressApplication;
  let prisma: PrismaService;
  let http: ReturnType<typeof request>;
  const webhookSecret = 'whsec_e2e_secret';

  beforeAll(async () => {
    Object.assign(process.env, {
      NODE_ENV: 'test',
      DATABASE_URL: databaseUrl,
      JWT_ACCESS_SECRET: 'e2e-access-secret-with-at-least-32-chars',
      ENTITLEMENT_PRIVATE_KEY: generateKeyPairSync('ec', { namedCurve: 'prime256v1' }).privateKey.export({ type: 'pkcs8', format: 'pem' }).toString(),
      STRIPE_SECRET_KEY: 'sk_test_e2e_not_a_real_key',
      STRIPE_WEBHOOK_SECRET: webhookSecret,
      STRIPE_PRICE_ULTIMATE_MONTH: 'price_ultimate_month',
      AI_PROVIDER: 'rules',
    });
    delete process.env.REDIS_URL;

    const moduleRef = await Test.createTestingModule({ imports: [AppModule] }).compile();
    app = moduleRef.createNestApplication<NestExpressApplication>({ rawBody: true, logger: false });
    configureApp(app);
    await app.init();
    prisma = app.get(PrismaService);
    await prisma.$executeRawUnsafe(
      'TRUNCATE "User", "RefreshToken", "Device", "Subscription", "StripeEvent", "BenchmarkRun", "GameSession", "GameProfile", "Release", "Announcement", "AuditLog", "AiUsage" CASCADE',
    );
    http = request(app.getHttpServer());
  });

  afterAll(async () => {
    await app?.close();
  });

  const register = async (email: string) => {
    const response = await http.post('/api/v1/auth/register').send({ email, password: 'correct horse battery staple' }).expect(201);
    return response.body as { tokens: { accessToken: string; refreshToken: string; expiresIn: number }; email: string };
  };

  let user: Awaited<ReturnType<typeof register>>;
  let userId: string;
  let adminToken: string;
  let deviceId: string;

  it('reports health', async () => {
    const response = await http.get('/api/v1/health').expect(200);
    expect(response.body).toMatchObject({ status: 'ok', database: 'ok', redis: 'disabled' });
  });

  it('registers, rejects duplicates and wrong passwords, and signs in', async () => {
    user = await register('Player@Example.com');
    expect(user.email).toBe('player@example.com');
    expect(user.tokens.expiresIn).toBe(900);

    const duplicate = await http.post('/api/v1/auth/register').send({ email: 'player@example.com', password: 'another long password' }).expect(409);
    expect(duplicate.body).toMatchObject({ code: 'conflict' });

    const wrong = await http.post('/api/v1/auth/login').send({ email: 'player@example.com', password: 'not the password' }).expect(401);
    expect(wrong.body.message).toBe('E-mail or password is incorrect.');

    await http.post('/api/v1/auth/login').send({ email: 'player@example.com', password: 'correct horse battery staple' }).expect(200);
    const me = await http.get('/api/v1/auth/me').set('Authorization', `Bearer ${user.tokens.accessToken}`).expect(200);
    userId = me.body.id;
    expect(me.body.entitlements).toMatchObject({ tier: 'free', source: 'free' });
  });

  it('requires authentication and validates input', async () => {
    await http.get('/api/v1/devices').expect(401);
    const invalid = await http.post('/api/v1/devices').set('Authorization', `Bearer ${user.tokens.accessToken}`).send({ name: 'PC', platform: 'linux' }).expect(400);
    expect(invalid.body.code).toBe('validation_failed');
  });

  it('registers a device and issues a verifiable, device-bound license token', async () => {
    const device = await http.post('/api/v1/devices').set('Authorization', `Bearer ${user.tokens.accessToken}`).send({ name: 'Gaming PC', hardware: 'Test CPU', platform: 'windows' }).expect(201);
    deviceId = device.body.id;
    const issued = await http.post(`/api/v1/devices/${deviceId}/entitlements`).set('Authorization', `Bearer ${user.tokens.accessToken}`).expect(200);
    const key = await http.get('/api/v1/system/entitlement-key').expect(200);
    const [header, payload, signature] = (issued.body.token as string).split('.');
    expect(verify('sha256', Buffer.from(`${header}.${payload}`), { key: key.body.publicKeyPem, dsaEncoding: 'ieee-p1363' }, Buffer.from(signature!, 'base64url'))).toBe(true);
    expect(JSON.parse(Buffer.from(payload!, 'base64url').toString())).toMatchObject({ iss: 'storm-cloud', did: deviceId, tier: 'free' });
  });

  it('gates cloud sync by entitlement, and lets an administrator grant a plan', async () => {
    const benchmark = { id: randomUUID(), type: 'cpu', startedAt: new Date().toISOString(), metrics: [{ key: 'cpu.multi.mops', name: 'Multi-thread', value: 9000, unit: 'MOPS' }], completed: true };
    const blocked = await http.post('/api/v1/benchmarks').set('Authorization', `Bearer ${user.tokens.accessToken}`).send(benchmark).expect(402);
    expect(blocked.body.code).toBe('entitlement_required');

    const admin = await register('admin@example.com');
    await prisma.user.update({ where: { email: 'admin@example.com' }, data: { role: 'ADMIN' } });
    adminToken = admin.tokens.accessToken;
    await http.patch(`/api/v1/admin/users/${userId}`).set('Authorization', `Bearer ${user.tokens.accessToken}`).send({ compTier: 'pro' }).expect(403);
    await http.patch(`/api/v1/admin/users/${userId}`).set('Authorization', `Bearer ${adminToken}`).send({ compTier: 'pro' }).expect(200);

    const first = await http.post('/api/v1/benchmarks').set('Authorization', `Bearer ${user.tokens.accessToken}`).send(benchmark).expect(201);
    const again = await http.post('/api/v1/benchmarks').set('Authorization', `Bearer ${user.tokens.accessToken}`).send(benchmark).expect(201);
    expect(again.body.id).toBe(first.body.id);
    const list = await http.get('/api/v1/benchmarks').set('Authorization', `Bearer ${user.tokens.accessToken}`).expect(200);
    expect(list.body.total).toBe(1);

    const audit = await prisma.auditLog.findFirst({ where: { action: 'admin.user_update', target: userId } });
    expect(audit).not.toBeNull();
  });

  it('processes Stripe webhooks idempotently and upgrades the user', async () => {
    const stripe = new Stripe('sk_test_e2e_not_a_real_key');
    const event = {
      id: `evt_${randomUUID().replace(/-/g, '')}`,
      object: 'event',
      type: 'customer.subscription.updated',
      data: {
        object: {
          id: 'sub_e2e',
          object: 'subscription',
          customer: 'cus_e2e',
          status: 'active',
          cancel_at_period_end: false,
          metadata: { userId },
          items: { object: 'list', data: [{ id: 'si_e2e', price: { id: 'price_ultimate_month' }, current_period_end: Math.floor(Date.now() / 1000) + 30 * 86400 }] },
        },
      },
    };
    const payload = JSON.stringify(event);
    const signature = stripe.webhooks.generateTestHeaderString({ payload, secret: webhookSecret });

    await http.post('/api/v1/billing/webhook').set('Content-Type', 'application/json').set('stripe-signature', 'bogus').send(payload).expect(400);
    const first = await http.post('/api/v1/billing/webhook').set('Content-Type', 'application/json').set('stripe-signature', signature).send(payload).expect(200);
    expect(first.body).toEqual({ received: true });
    const second = await http.post('/api/v1/billing/webhook').set('Content-Type', 'application/json').set('stripe-signature', signature).send(payload).expect(200);
    expect(second.body).toEqual({ received: true, duplicate: true });

    expect(await prisma.subscription.count({ where: { userId } })).toBe(1);
    const entitlements = await http.get('/api/v1/entitlements/me').set('Authorization', `Bearer ${user.tokens.accessToken}`).expect(200);
    expect(entitlements.body).toMatchObject({ tier: 'ultimate', source: 'subscription' });
  });

  it('returns explainable AI analysis for entitled users', async () => {
    const response = await http
      .post('/api/v1/ai/analyze')
      .set('Authorization', `Bearer ${user.tokens.accessToken}`)
      .send({ sampleCount: 120, gameName: 'Test Game', averageGpuUsage: 50, averageMaxCoreUsage: 98 })
      .expect(200);
    const cpu = response.body.recommendations.find((r: { id: string }) => r.id === 'cpu-limited');
    expect(cpu).toMatchObject({ risk: 'none', confidence: 'high', source: 'rules' });
    expect(cpu.evidence).toContain('Average GPU usage: 50 %');
  });

  it('rotates refresh tokens and revokes the family on reuse', async () => {
    const rotated = await http.post('/api/v1/auth/refresh').send({ refreshToken: user.tokens.refreshToken }).expect(200);
    expect(rotated.body.refreshToken).not.toBe(user.tokens.refreshToken);
    await http.post('/api/v1/auth/refresh').send({ refreshToken: user.tokens.refreshToken }).expect(401);
    await http.post('/api/v1/auth/refresh').send({ refreshToken: rotated.body.refreshToken }).expect(401);
    expect(await prisma.auditLog.count({ where: { action: 'auth.refresh_reuse_detected' } })).toBe(2);
  });

  it('publishes releases and serves the latest per channel', async () => {
    await http.get('/api/v1/releases/latest?channel=stable&arch=x64').expect(404);
    await http
      .post('/api/v1/admin/releases')
      .set('Authorization', `Bearer ${adminToken}`)
      .send({ version: '1.0.0', channel: 'stable', arch: 'x64', url: 'https://downloads.example.com/StormOS-1.0.0-x64.msi', sha256: 'a'.repeat(64), sizeBytes: 123456 })
      .expect(201);
    const latest = await http.get('/api/v1/releases/latest?channel=beta&arch=x64').expect(200);
    expect(latest.body).toMatchObject({ version: '1.0.0', channel: 'stable', sha256: 'a'.repeat(64), sizeBytes: 123456 });
  });

  it('serves published profiles only', async () => {
    const profile = { schemaVersion: 1, id: 'test-game', version: '1.0.0', name: 'Test Game', detection: { executables: [{ name: 'test.exe' }], launchers: [] } };
    await http.put('/api/v1/admin/profiles/test-game').set('Authorization', `Bearer ${adminToken}`).send(profile).expect(200);
    expect((await http.get('/api/v1/profiles').expect(200)).body).toEqual([]);
    await http.patch('/api/v1/admin/profiles/test-game/publish').set('Authorization', `Bearer ${adminToken}`).send({ published: true }).expect(200);
    expect((await http.get('/api/v1/profiles').expect(200)).body).toEqual([profile]);
  });
});
