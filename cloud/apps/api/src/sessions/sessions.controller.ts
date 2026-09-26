import { Body, Controller, Delete, Get, HttpCode, HttpStatus, Param, ParseUUIDPipe, Post, Query } from '@nestjs/common';
import { FEATURES, type Paged } from '@storm/types';
import type { Prisma } from '@storm/database';
import { paginationSchema, sessionUploadSchema } from '@storm/validation';
import { CurrentUser, RequireFeature, type AuthUser } from '../auth/decorators';
import { ApiError, parseInput } from '../common/api-error';
import { PrismaService } from '../common/prisma.service';

@Controller('sessions')
export class SessionsController {
  constructor(private readonly prisma: PrismaService) {}

  @Post()
  @RequireFeature(FEATURES.cloudSync)
  async upload(@CurrentUser() user: AuthUser, @Body() body: unknown): Promise<{ id: string }> {
    const session = parseInput(sessionUploadSchema, body);
    const data = {
      gameId: session.gameId ?? null,
      gameName: session.gameName,
      startedAt: new Date(session.startedAt),
      endedAt: session.endedAt ? new Date(session.endedAt) : null,
      averageFps: session.averageFps ?? null,
      onePercentLowFps: session.onePercentLowFps ?? null,
      data: session as unknown as Prisma.InputJsonObject,
    };
    return this.prisma.gameSession.upsert({
      where: { userId_clientId: { userId: user.id, clientId: session.id } },
      create: { userId: user.id, clientId: session.id, ...data },
      update: data,
      select: { id: true },
    });
  }

  @Get()
  async list(@CurrentUser() user: AuthUser, @Query() query: unknown): Promise<Paged<unknown>> {
    const { page, pageSize } = parseInput(paginationSchema, query);
    const [items, total] = await Promise.all([
      this.prisma.gameSession.findMany({ where: { userId: user.id }, orderBy: { startedAt: 'desc' }, skip: (page - 1) * pageSize, take: pageSize }),
      this.prisma.gameSession.count({ where: { userId: user.id } }),
    ]);
    return { items, total, page, pageSize };
  }

  @Delete(':id')
  @HttpCode(HttpStatus.NO_CONTENT)
  async remove(@CurrentUser() user: AuthUser, @Param('id', ParseUUIDPipe) id: string): Promise<void> {
    const result = await this.prisma.gameSession.deleteMany({ where: { id, userId: user.id } });
    if (result.count === 0) throw ApiError.notFound('Session not found.');
  }
}
