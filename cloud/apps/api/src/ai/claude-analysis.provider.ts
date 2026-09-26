import { Inject, Injectable, Logger } from '@nestjs/common';
import Anthropic from '@anthropic-ai/sdk';
import { AI_ALLOWED_ACTION_RULES, type Recommendation } from '@storm/types';
import { aiRecommendationSchema, z, type AnalysisWindow } from '@storm/validation';
import { APP_CONFIG, type AppConfig } from '../config/app-config';
import { isGrounded, measuredValues } from './grounding';

const SYSTEM_PROMPT = `You are the performance analyst of STORM OS, a Windows gaming performance app.
You receive one JSON object with aggregated measurements from the user's PC (averages over a time window, peak temperatures, optional frame statistics).

Explain what limits performance or smoothness, based only on those measurements.
- Every evidence item must quote a number (or the game / power plan name) exactly as it appears in the input. Do not estimate, extrapolate or invent values; if a value is missing, do not mention it.
- Prefer a few high-value findings over many weak ones. Return an empty list when nothing stands out.
- "why" explains the mechanism in one or two sentences; "advice" gives concrete in-game or Windows settings to try.
- Only set "action" when one of the listed rule ids directly addresses the finding; otherwise use null. The user always confirms actions, and every action is reversible.
- Never recommend disabling Windows security, Windows Defender, the firewall, Secure Boot, Windows Update or anti-cheat software, and never suggest third-party "debloat" or registry tweak tools.
- Use short kebab-case ids prefixed with "ai-".`;

const RESPONSE_SCHEMA = {
  type: 'object',
  additionalProperties: false,
  required: ['recommendations'],
  properties: {
    recommendations: {
      type: 'array',
      items: {
        type: 'object',
        additionalProperties: false,
        required: ['id', 'title', 'why', 'evidence', 'risk', 'advice', 'confidence', 'action'],
        properties: {
          id: { type: 'string' },
          title: { type: 'string' },
          why: { type: 'string' },
          evidence: { type: 'array', items: { type: 'string' } },
          risk: { type: 'string', enum: ['none', 'low', 'medium', 'high'] },
          advice: { type: 'string' },
          confidence: { type: 'string', enum: ['low', 'medium', 'high'] },
          action: {
            anyOf: [
              { type: 'null' },
              {
                type: 'object',
                additionalProperties: false,
                required: ['label', 'ruleId'],
                properties: {
                  label: { type: 'string' },
                  ruleId: { type: 'string', enum: [...AI_ALLOWED_ACTION_RULES] },
                  parameters: {
                    type: 'object',
                    additionalProperties: false,
                    properties: { plan: { type: 'string', enum: ['balanced', 'high-performance'] }, processName: { type: 'string' } },
                  },
                },
              },
            ],
          },
        },
      },
    },
  },
} as const;

const responseSchema = z.object({ recommendations: z.array(z.unknown()).max(12) });

/**
 * Optional Claude-backed analysis. Output is schema-constrained, re-validated, and every evidence line must be
 * grounded in the submitted measurements; anything else is discarded. It never changes a system: actions are
 * suggestions the desktop app shows for explicit user confirmation.
 */
@Injectable()
export class ClaudeAnalysisProvider {
  private readonly logger = new Logger(ClaudeAnalysisProvider.name);
  private readonly client: Anthropic | null;

  constructor(@Inject(APP_CONFIG) private readonly config: AppConfig) {
    this.client = config.AI_PROVIDER === 'anthropic' && config.ANTHROPIC_API_KEY ? new Anthropic({ apiKey: config.ANTHROPIC_API_KEY, maxRetries: 2, timeout: 120_000 }) : null;
  }

  get enabled(): boolean {
    return this.client !== null;
  }

  async analyze(window: AnalysisWindow): Promise<Recommendation[]> {
    if (!this.client) return [];
    const response = await this.client.beta.messages.create({
      model: this.config.ANTHROPIC_MODEL,
      max_tokens: 16000,
      betas: ['server-side-fallback-2026-07-01'],
      fallbacks: 'default',
      thinking: { type: 'adaptive' },
      output_config: { format: { type: 'json_schema', schema: RESPONSE_SCHEMA } },
      system: SYSTEM_PROMPT,
      messages: [{ role: 'user', content: JSON.stringify(window) }],
    });

    if (response.stop_reason === 'refusal' || response.stop_reason === 'max_tokens') {
      this.logger.warn(`AI analysis returned no usable answer (stop reason ${response.stop_reason}).`);
      return [];
    }

    const text = response.content.find((block): block is Anthropic.Beta.BetaTextBlock => block.type === 'text')?.text;
    if (!text) return [];
    return this.accept(JSON.parse(text) as unknown, window);
  }

  /** Validates and grounds raw model output; exposed for tests. */
  accept(raw: unknown, window: AnalysisWindow): Recommendation[] {
    const parsed = responseSchema.safeParse(raw);
    if (!parsed.success) return [];
    const values = measuredValues(window);
    const accepted: Recommendation[] = [];
    for (const candidate of parsed.data.recommendations) {
      const result = aiRecommendationSchema.safeParse(candidate);
      if (!result.success) continue;
      const recommendation = result.data;
      if (!recommendation.evidence.every((line) => isGrounded(line, window, values))) {
        this.logger.debug(`Dropped ungrounded AI recommendation ${recommendation.id}.`);
        continue;
      }

      accepted.push({
        id: recommendation.id.startsWith('ai-') ? recommendation.id : `ai-${recommendation.id}`,
        title: recommendation.title,
        why: recommendation.why,
        evidence: recommendation.evidence,
        risk: recommendation.risk,
        advice: recommendation.advice,
        action: recommendation.action ?? undefined,
        confidence: recommendation.confidence,
        source: 'ai',
      });
    }

    return accepted;
  }
}
