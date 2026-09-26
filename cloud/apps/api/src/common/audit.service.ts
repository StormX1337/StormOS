import { Injectable, Logger } from '@nestjs/common';
import type { Prisma } from '@storm/database';
import { PrismaService } from './prisma.service';

/** Append-only audit trail for security-relevant and administrative actions. Never stores secrets. */
@Injectable()
export class AuditService {
  private readonly logger = new Logger('Audit');

  constructor(private readonly prisma: PrismaService) {}

  async record(action: string, options: { actorId?: string | null; target?: string | null; details?: Prisma.InputJsonObject; ip?: string | null } = {}): Promise<void> {
    try {
      await this.prisma.auditLog.create({
        data: { action, actorId: options.actorId ?? null, target: options.target ?? null, details: options.details, ip: options.ip ?? null },
      });
    } catch (error) {
      this.logger.error(`Audit write failed for ${action}: ${String(error)}`);
    }
  }
}
