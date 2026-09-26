import { Inject, Injectable, Logger } from '@nestjs/common';
import Anthropic from '@anthropic-ai/sdk';
import type { AnalysisResponse } from '@storm/types';
import type { AnalysisWindow } from '@storm/validation';
import { ApiError } from '../common/api-error';
import { PrismaService } from '../common/prisma.service';
import { APP_CONFIG, type AppConfig } from '../config/app-config';
import { ClaudeAnalysisProvider } from './claude-analysis.provider';
import { analyzeWithRules } from './rules-engine';

@Injectable()
export class AiService {
  private readonly logger = new Logger(AiService.name);

  constructor(
    private readonly prisma: PrismaService,
    private readonly claude: ClaudeAnalysisProvider,
    @Inject(APP_CONFIG) private readonly config: AppConfig,
  ) {}

  async analyze(userId: string, window: AnalysisWindow): Promise<AnalysisResponse> {
    const since = new Date(Date.now() - 24 * 3600 * 1000);
    if ((await this.prisma.aiUsage.count({ where: { userId, createdAt: { gte: since } } })) >= this.config.AI_DAILY_LIMIT) {
      throw ApiError.tooMany(`You reached today's limit of ${this.config.AI_DAILY_LIMIT} analyses. Local analysis keeps working.`);
    }

    const rules = analyzeWithRules(window);
    let ai: AnalysisResponse['recommendations'] = [];
    if (this.claude.enabled) {
      try {
        ai = await this.claude.analyze(window);
      } catch (error) {
        const detail = error instanceof Anthropic.APIError ? `${error.status ?? ''} ${error.name}` : String(error);
        this.logger.warn(`AI provider failed, returning rule-based results only: ${detail}`);
      }
    }

    await this.prisma.aiUsage.create({ data: { userId, provider: this.claude.enabled ? 'anthropic' : 'rules' } });
    const ids = new Set(rules.map((recommendation) => recommendation.id));
    return { recommendations: [...rules, ...ai.filter((recommendation) => !ids.has(recommendation.id)).slice(0, 8)] };
  }
}
