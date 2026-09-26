import { Body, Controller, Delete, Get, HttpCode, HttpStatus, Param, ParseUUIDPipe, Post, Query } from '@nestjs/common';
import { FEATURES, type Paged } from '@storm/types';
import type { Prisma } from '@storm/database';
import { benchmarkListSchema, benchmarkUploadSchema } from '@storm/validation';
import { CurrentUser, RequireFeature, type AuthUser } from '../auth/decorators';
import { ApiError, parseInput } from '../common/api-error';
import { PrismaService } from '../common/prisma.service';

const MAX_RUNS_PER_USER = 10_000;

@Controller('benchmarks')
export class BenchmarksController {
  constructor(private readonly prisma: PrismaService) {}

  /** Stores a benchmark measured by the desktop app. Re-uploading the same run id updates it (idempotent). */
  @Post()
  @RequireFeature(FEATURES.cloudSync)
  async upload(@CurrentUser() user: AuthUser, @Body() body: unknown): Promise<{ id: string }> {
    const run = parseInput(benchmarkUploadSchema, body);
    const exists = await this.prisma.benchmarkRun.findUnique({ where: { userId_clientId: { userId: user.id, clientId: run.id } }, select: { id: true } });
    if (!exists && (await this.prisma.benchmarkRun.count({ where: { userId: user.id } })) >= MAX_RUNS_PER_USER) {
      throw ApiError.conflict('Your benchmark history is full. Delete older runs first.');
    }

    const data = {
      type: run.type,
      startedAt: new Date(run.startedAt),
      hardware: run.hardware,
      gameId: run.gameId ?? null,
      gameName: run.gameName ?? null,
      label: run.label ?? null,
      completed: run.completed,
      score: run.score?.score ?? null,
      data: run as unknown as Prisma.InputJsonObject,
    };
    const saved = await this.prisma.benchmarkRun.upsert({
      where: { userId_clientId: { userId: user.id, clientId: run.id } },
      create: { userId: user.id, clientId: run.id, ...data },
      update: data,
      select: { id: true },
    });
    return saved;
  }

  @Get()
  async list(@CurrentUser() user: AuthUser, @Query() query: unknown): Promise<Paged<unknown>> {
    const { page, pageSize, type } = parseInput(benchmarkListSchema, query);
    const where = { userId: user.id, ...(type ? { type } : {}) };
    const [items, total] = await Promise.all([
      this.prisma.benchmarkRun.findMany({
        where,
        orderBy: { startedAt: 'desc' },
        skip: (page - 1) * pageSize,
        take: pageSize,
        select: { id: true, clientId: true, type: true, startedAt: true, hardware: true, gameName: true, label: true, completed: true, score: true, data: true },
      }),
      this.prisma.benchmarkRun.count({ where }),
    ]);
    return { items, total, page, pageSize };
  }

  @Get(':id')
  async get(@CurrentUser() user: AuthUser, @Param('id', ParseUUIDPipe) id: string) {
    const run = await this.prisma.benchmarkRun.findFirst({ where: { id, userId: user.id } });
    if (!run) throw ApiError.notFound('Benchmark not found.');
    return run;
  }

  @Delete(':id')
  @HttpCode(HttpStatus.NO_CONTENT)
  async remove(@CurrentUser() user: AuthUser, @Param('id', ParseUUIDPipe) id: string): Promise<void> {
    const result = await this.prisma.benchmarkRun.deleteMany({ where: { id, userId: user.id } });
    if (result.count === 0) throw ApiError.notFound('Benchmark not found.');
  }
}
