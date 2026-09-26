import { Body, Controller, Delete, Get, HttpCode, HttpStatus, Inject, Param, ParseUUIDPipe, Post } from '@nestjs/common';
import type { CloudDevice } from '@storm/types';
import { deviceRegisterSchema } from '@storm/validation';
import { CurrentUser, type AuthUser } from '../auth/decorators';
import { ApiError, parseInput } from '../common/api-error';
import { AuditService } from '../common/audit.service';
import { PrismaService } from '../common/prisma.service';
import { APP_CONFIG, type AppConfig } from '../config/app-config';
import { EntitlementsService } from '../entitlements/entitlements.service';

@Controller('devices')
export class DevicesController {
  constructor(
    private readonly prisma: PrismaService,
    private readonly entitlements: EntitlementsService,
    private readonly audit: AuditService,
    @Inject(APP_CONFIG) private readonly config: AppConfig,
  ) {}

  @Post()
  async register(@CurrentUser() user: AuthUser, @Body() body: unknown): Promise<CloudDevice> {
    const input = parseInput(deviceRegisterSchema, body);
    const count = await this.prisma.device.count({ where: { userId: user.id, revokedAt: null } });
    if (count >= this.config.MAX_DEVICES_PER_USER) {
      throw ApiError.conflict(`You can register up to ${this.config.MAX_DEVICES_PER_USER} PCs. Remove one on the account page first.`);
    }

    const device = await this.prisma.device.create({ data: { userId: user.id, name: input.name, hardware: input.hardware, platform: input.platform } });
    await this.audit.record('device.register', { actorId: user.id, target: device.id });
    return { id: device.id, name: device.name };
  }

  @Get()
  async list(@CurrentUser() user: AuthUser) {
    return this.prisma.device.findMany({
      where: { userId: user.id, revokedAt: null },
      select: { id: true, name: true, hardware: true, platform: true, createdAt: true, lastSeenAt: true },
      orderBy: { lastSeenAt: 'desc' },
    });
  }

  @Delete(':id')
  @HttpCode(HttpStatus.NO_CONTENT)
  async remove(@CurrentUser() user: AuthUser, @Param('id', ParseUUIDPipe) id: string): Promise<void> {
    const result = await this.prisma.device.updateMany({ where: { id, userId: user.id, revokedAt: null }, data: { revokedAt: new Date() } });
    if (result.count === 0) {
      throw ApiError.notFound('Device not found.');
    }

    await this.audit.record('device.remove', { actorId: user.id, target: id });
  }

  @Post(':id/entitlements')
  @HttpCode(HttpStatus.OK)
  entitlementToken(@CurrentUser() user: AuthUser, @Param('id', ParseUUIDPipe) id: string) {
    return this.entitlements.issueDeviceToken(user.id, id);
  }
}
