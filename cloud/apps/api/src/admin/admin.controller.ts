import { Body, Controller, Delete, Get, HttpCode, HttpStatus, Param, ParseUUIDPipe, Patch, Post, Put, Query } from '@nestjs/common';
import type { Prisma, Role, Tier } from '@storm/database';
import { TIER_FEATURES, type Paged } from '@storm/types';
import { adminUserUpdateSchema, announcementSchema, gameProfileSchema, paginationSchema, releaseSchema, z } from '@storm/validation';
import { AuthService } from '../auth/auth.service';
import { ClientIp, CurrentUser, Roles, type AuthUser } from '../auth/decorators';
import { ApiError, parseInput } from '../common/api-error';
import { AuditService } from '../common/audit.service';
import { PrismaService } from '../common/prisma.service';
import { RedisService } from '../common/redis.service';
import { EntitlementsService } from '../entitlements/entitlements.service';
import { PROFILES_CACHE_KEY } from '../profiles/profiles.controller';
import { RealtimeGateway } from '../realtime/realtime.gateway';
import { releaseCacheKey } from '../releases/releases.controller';
import { ANNOUNCEMENTS_CACHE_KEY } from '../system/system.controller';

const publishSchema = z.object({ published: z.boolean() });

/** Storm Admin. Support staff can read; only administrators can change anything. Every change is audited. */
@Controller('admin')
@Roles('admin', 'support')
export class AdminController {
  constructor(
    private readonly prisma: PrismaService,
    private readonly audit: AuditService,
    private readonly entitlements: EntitlementsService,
    private readonly auth: AuthService,
    private readonly redis: RedisService,
    private readonly realtime: RealtimeGateway,
  ) {}

  @Get('stats')
  async stats() {
    const since = new Date(Date.now() - 24 * 3600 * 1000);
    const [users, newUsers, devices, benchmarks, sessions, subscriptions, aiLastDay] = await Promise.all([
      this.prisma.user.count(),
      this.prisma.user.count({ where: { createdAt: { gte: since } } }),
      this.prisma.device.count({ where: { revokedAt: null } }),
      this.prisma.benchmarkRun.count(),
      this.prisma.gameSession.count(),
      this.prisma.subscription.groupBy({ by: ['tier'], where: { status: { in: ['ACTIVE', 'TRIALING', 'PAST_DUE'] } }, _count: { _all: true } }),
      this.prisma.aiUsage.count({ where: { createdAt: { gte: since } } }),
    ]);
    return {
      users,
      newUsersLast24h: newUsers,
      devices,
      benchmarks,
      sessions,
      aiAnalysesLast24h: aiLastDay,
      activeSubscriptions: Object.fromEntries(subscriptions.map((row) => [row.tier.toLowerCase(), row._count._all])),
    };
  }

  @Get('users')
  async users(@Query() query: unknown): Promise<Paged<unknown>> {
    const { page, pageSize, q } = parseInput(paginationSchema, query);
    const where: Prisma.UserWhereInput = q ? { email: { contains: q.toLowerCase() } } : {};
    const [items, total] = await Promise.all([
      this.prisma.user.findMany({
        where,
        orderBy: { createdAt: 'desc' },
        skip: (page - 1) * pageSize,
        take: pageSize,
        select: { id: true, email: true, role: true, disabled: true, compTier: true, createdAt: true, lastLoginAt: true, _count: { select: { devices: true } } },
      }),
      this.prisma.user.count({ where }),
    ]);
    return { items, total, page, pageSize };
  }

  @Get('users/:id')
  async user(@Param('id', ParseUUIDPipe) id: string) {
    const user = await this.prisma.user.findUnique({
      where: { id },
      select: {
        id: true, email: true, role: true, disabled: true, compTier: true, compExpiresAt: true, createdAt: true, lastLoginAt: true, stripeCustomerId: true,
        devices: { select: { id: true, name: true, hardware: true, createdAt: true, lastSeenAt: true, revokedAt: true } },
        subscriptions: { select: { id: true, tier: true, status: true, currentPeriodEnd: true, cancelAtPeriodEnd: true, stripeSubscriptionId: true } },
      },
    });
    if (!user) throw ApiError.notFound('User not found.');
    return { ...user, entitlements: await this.entitlements.resolve(id) };
  }

  @Patch('users/:id')
  @Roles('admin')
  async updateUser(@CurrentUser() actor: AuthUser, @Param('id', ParseUUIDPipe) id: string, @Body() body: unknown, @ClientIp() ip: string | null) {
    const input = parseInput(adminUserUpdateSchema, body);
    if (id === actor.id && (input.role !== undefined || input.disabled !== undefined)) {
      throw ApiError.forbidden('You cannot change your own role or disable your own account.');
    }

    const data: Prisma.UserUpdateInput = {};
    if (input.role !== undefined) data.role = input.role.toUpperCase() as Role;
    if (input.disabled !== undefined) data.disabled = input.disabled;
    if (input.compTier !== undefined) data.compTier = input.compTier ? (input.compTier.toUpperCase() as Tier) : null;
    if (input.compExpiresAt !== undefined) data.compExpiresAt = input.compExpiresAt ? new Date(input.compExpiresAt) : null;
    const updated = await this.prisma.user.update({ where: { id }, data, select: { id: true, email: true, role: true, disabled: true, compTier: true, compExpiresAt: true } }).catch(() => {
      throw ApiError.notFound('User not found.');
    });
    if (input.disabled) {
      await this.auth.revokeAll(id);
    }

    await this.audit.record('admin.user_update', { actorId: actor.id, target: id, ip, details: input as Prisma.InputJsonObject });
    this.realtime.notifyEntitlementsChanged(id);
    return updated;
  }

  @Get('audit')
  async auditLog(@Query() query: unknown): Promise<Paged<unknown>> {
    const { page, pageSize, q } = parseInput(paginationSchema, query);
    const where: Prisma.AuditLogWhereInput = q ? { action: { startsWith: q } } : {};
    const [items, total] = await Promise.all([
      this.prisma.auditLog.findMany({ where, orderBy: { createdAt: 'desc' }, skip: (page - 1) * pageSize, take: pageSize }),
      this.prisma.auditLog.count({ where }),
    ]);
    return { items, total, page, pageSize };
  }

  @Get('tiers')
  tiers() {
    return TIER_FEATURES;
  }

  // Releases

  @Get('releases')
  async releases() {
    const rows = await this.prisma.release.findMany({ orderBy: { publishedAt: 'desc' }, take: 100 });
    return rows.map((row) => ({ ...row, sizeBytes: Number(row.sizeBytes) }));
  }

  @Post('releases')
  @Roles('admin')
  async createRelease(@CurrentUser() actor: AuthUser, @Body() body: unknown) {
    const input = parseInput(releaseSchema, body);
    const channel = input.channel === 'beta' ? 'BETA' : 'STABLE';
    const exists = await this.prisma.release.findUnique({ where: { version_channel_arch: { version: input.version, channel, arch: input.arch } } });
    if (exists) throw ApiError.conflict('This version is already published for that channel and architecture.');
    const release = await this.prisma.release.create({
      data: { version: input.version, channel, arch: input.arch, url: input.url, sha256: input.sha256, notes: input.notes, sizeBytes: BigInt(input.sizeBytes), publishedAt: input.publishedAt ? new Date(input.publishedAt) : new Date() },
    });
    await this.redis.invalidate(releaseCacheKey('stable', input.arch), releaseCacheKey('beta', input.arch));
    await this.audit.record('admin.release_create', { actorId: actor.id, target: release.id, details: { version: input.version, channel: input.channel, arch: input.arch } });
    return { ...release, sizeBytes: Number(release.sizeBytes) };
  }

  @Delete('releases/:id')
  @Roles('admin')
  @HttpCode(HttpStatus.NO_CONTENT)
  async deleteRelease(@CurrentUser() actor: AuthUser, @Param('id', ParseUUIDPipe) id: string): Promise<void> {
    const release = await this.prisma.release.delete({ where: { id } }).catch(() => {
      throw ApiError.notFound('Release not found.');
    });
    await this.redis.invalidate(releaseCacheKey('stable', release.arch), releaseCacheKey('beta', release.arch));
    await this.audit.record('admin.release_delete', { actorId: actor.id, target: id, details: { version: release.version } });
  }

  // Announcements

  @Get('announcements')
  announcements() {
    return this.prisma.announcement.findMany({ orderBy: { createdAt: 'desc' }, take: 100 });
  }

  @Post('announcements')
  @Roles('admin')
  async createAnnouncement(@CurrentUser() actor: AuthUser, @Body() body: unknown) {
    const input = parseInput(announcementSchema, body);
    const announcement = await this.prisma.announcement.create({
      data: { title: input.title, body: input.body, severity: input.severity.toUpperCase() as 'INFO' | 'WARNING' | 'CRITICAL', active: input.active, startsAt: input.startsAt ? new Date(input.startsAt) : null, endsAt: input.endsAt ? new Date(input.endsAt) : null },
    });
    await this.redis.invalidate(ANNOUNCEMENTS_CACHE_KEY);
    await this.audit.record('admin.announcement_create', { actorId: actor.id, target: announcement.id });
    if (announcement.active && !announcement.startsAt) {
      this.realtime.broadcastAnnouncement({ id: announcement.id, title: announcement.title, body: announcement.body, severity: input.severity });
    }

    return announcement;
  }

  @Delete('announcements/:id')
  @Roles('admin')
  @HttpCode(HttpStatus.NO_CONTENT)
  async deleteAnnouncement(@CurrentUser() actor: AuthUser, @Param('id', ParseUUIDPipe) id: string): Promise<void> {
    await this.prisma.announcement.delete({ where: { id } }).catch(() => {
      throw ApiError.notFound('Announcement not found.');
    });
    await this.redis.invalidate(ANNOUNCEMENTS_CACHE_KEY);
    await this.audit.record('admin.announcement_delete', { actorId: actor.id, target: id });
  }

  // Game profiles

  @Get('profiles')
  profiles() {
    return this.prisma.gameProfile.findMany({ orderBy: { name: 'asc' }, select: { id: true, name: true, version: true, published: true, updatedAt: true } });
  }

  @Get('profiles/:id')
  async profile(@Param('id') id: string) {
    const profile = await this.prisma.gameProfile.findUnique({ where: { id } });
    if (!profile) throw ApiError.notFound('Profile not found.');
    return profile;
  }

  @Put('profiles/:id')
  @Roles('admin')
  async saveProfile(@CurrentUser() actor: AuthUser, @Param('id') id: string, @Body() body: unknown) {
    const profile = parseInput(gameProfileSchema, body);
    if (profile.id !== id) throw ApiError.badRequest('The profile id does not match the URL.');
    const data = profile as unknown as Prisma.InputJsonObject;
    const saved = await this.prisma.gameProfile.upsert({
      where: { id },
      create: { id, version: profile.version, name: profile.name, data, updatedById: actor.id },
      update: { version: profile.version, name: profile.name, data, updatedById: actor.id },
      select: { id: true, name: true, version: true, published: true },
    });
    await this.redis.invalidate(PROFILES_CACHE_KEY);
    await this.audit.record('admin.profile_save', { actorId: actor.id, target: id, details: { version: profile.version } });
    return saved;
  }

  @Patch('profiles/:id/publish')
  @Roles('admin')
  async publishProfile(@CurrentUser() actor: AuthUser, @Param('id') id: string, @Body() body: unknown) {
    const { published } = parseInput(publishSchema, body);
    const saved = await this.prisma.gameProfile.update({ where: { id }, data: { published, updatedById: actor.id }, select: { id: true, published: true } }).catch(() => {
      throw ApiError.notFound('Profile not found.');
    });
    await this.redis.invalidate(PROFILES_CACHE_KEY);
    await this.audit.record(published ? 'admin.profile_publish' : 'admin.profile_unpublish', { actorId: actor.id, target: id });
    return saved;
  }
}
