import { Body, Controller, HttpCode, HttpStatus, Post } from '@nestjs/common';
import { Throttle } from '@nestjs/throttler';
import { FEATURES } from '@storm/types';
import { analysisWindowSchema } from '@storm/validation';
import { CurrentUser, RequireFeature, type AuthUser } from '../auth/decorators';
import { parseInput } from '../common/api-error';
import { AiService } from './ai.service';

@Controller('ai')
export class AiController {
  constructor(private readonly ai: AiService) {}

  /** Explainable recommendations from aggregated measurements. Never changes anything on the PC. */
  @Post('analyze')
  @HttpCode(HttpStatus.OK)
  @RequireFeature(FEATURES.aiAnalysis)
  @Throttle({ default: { limit: 10, ttl: 60_000 } })
  analyze(@CurrentUser() user: AuthUser, @Body() body: unknown) {
    return this.ai.analyze(user.id, parseInput(analysisWindowSchema, body));
  }
}
