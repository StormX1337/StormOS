import { z } from 'zod';
import { AI_ALLOWED_ACTION_RULES } from '@storm/types';

const finite = z.number().finite();
const optionalNumber = finite.nullish();
const shortText = (max: number) => z.string().trim().max(max);
const isoDate = z.string().datetime({ offset: true });
const semverString = () => z.string().regex(/^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$/, 'Use a semantic version such as 1.4.0 or 1.5.0-beta.1.');

export const emailSchema = z.string().trim().toLowerCase().email().max(254);

/** 10–128 characters; length is the main strength factor, no composition rules (NIST SP 800-63B). */
export const passwordSchema = z.string().min(10, 'Use at least 10 characters.').max(128);

export const registerSchema = z.object({ email: emailSchema, password: passwordSchema });
export const loginSchema = z.object({ email: emailSchema, password: z.string().min(1).max(128) });
export const refreshSchema = z.object({ refreshToken: z.string().min(32).max(512) });

export const deviceRegisterSchema = z.object({
  name: shortText(64).min(1),
  hardware: shortText(256).default(''),
  platform: z.literal('windows'),
});

export const benchmarkTypes = ['cpu', 'gpu', 'memory', 'disk', 'network', 'gaming'] as const;

const frameStatisticsSchema = z.object({
  frameCount: z.number().int().nonnegative(),
  durationSeconds: finite.nonnegative(),
  averageFps: finite.nonnegative(),
  minFps: finite.nonnegative().optional(),
  maxFps: finite.nonnegative().optional(),
  onePercentLowFps: finite.nonnegative(),
  pointOnePercentLowFps: finite.nonnegative().optional(),
  averageFrameTimeMs: finite.nonnegative(),
  medianFrameTimeMs: finite.nonnegative().optional(),
  p99FrameTimeMs: finite.nonnegative().optional(),
  p999FrameTimeMs: finite.nonnegative().optional(),
  frameTimeStdDevMs: finite.nonnegative().optional(),
  stutterCount: z.number().int().nonnegative().optional(),
  averageCpuBusyMs: optionalNumber,
  averageGpuBusyMs: optionalNumber,
  droppedFrames: z.number().int().nonnegative().nullish(),
});

const scoreSchema = z.object({
  name: shortText(64),
  score: optionalNumber,
  coverage: finite.min(0).max(1),
});

/** Mirrors StormOS.Core.Benchmark.BenchmarkResult (unknown keys are stripped). */
export const benchmarkUploadSchema = z.object({
  id: z.string().uuid(),
  type: z.enum(benchmarkTypes),
  startedAt: isoDate,
  duration: z.string().max(32).optional(),
  hardware: shortText(256).default(''),
  gameId: shortText(128).nullish(),
  gameName: shortText(128).nullish(),
  settings: shortText(512).nullish(),
  label: shortText(40).nullish(),
  metrics: z
    .array(
      z.object({
        key: z.string().regex(/^[a-z0-9][a-z0-9._-]{0,63}$/),
        name: shortText(128),
        value: finite,
        unit: shortText(16),
        higherIsBetter: z.boolean().default(true),
      }),
    )
    .max(64)
    .default([]),
  frames: frameStatisticsSchema.nullish(),
  averageCpuUsage: optionalNumber,
  averageGpuUsage: optionalNumber,
  score: scoreSchema.nullish(),
  completed: z.boolean(),
  error: shortText(512).nullish(),
  appVersion: shortText(64).default(''),
});
export type BenchmarkUpload = z.infer<typeof benchmarkUploadSchema>;

/** Mirrors StormOS.Core.History.GameSession. */
export const sessionUploadSchema = z.object({
  id: z.string().uuid(),
  gameId: shortText(128).nullish(),
  gameName: shortText(128),
  processName: shortText(128).default(''),
  startedAt: isoDate,
  endedAt: isoDate.nullish(),
  averageFps: optionalNumber,
  onePercentLowFps: optionalNumber,
  pointOnePercentLowFps: optionalNumber,
  averageFrameTimeMs: optionalNumber,
  averageCpuUsage: optionalNumber,
  averageGpuUsage: optionalNumber,
  maxCpuTemperature: optionalNumber,
  maxGpuTemperature: optionalNumber,
  averageLatencyMs: optionalNumber,
  frameSource: shortText(64).nullish(),
  sampleCount: z.number().int().nonnegative().default(0),
});
export type SessionUpload = z.infer<typeof sessionUploadSchema>;

/** Mirrors StormOS.Core.Analysis.AnalysisWindow: measured aggregates only, no identifiers. */
export const analysisWindowSchema = z.object({
  from: isoDate.optional(),
  to: isoDate.optional(),
  sampleCount: z.number().int().nonnegative().max(1_000_000),
  averageCpuUsage: optionalNumber,
  averageMaxCoreUsage: optionalNumber,
  averageGpuUsage: optionalNumber,
  averageRamUsage: optionalNumber,
  averageVramUsage: optionalNumber,
  averageDiskActive: optionalNumber,
  averageLatencyMs: optionalNumber,
  maxCpuTemperature: optionalNumber,
  maxGpuTemperature: optionalNumber,
  processCount: z.number().int().nonnegative().nullish(),
  busyBackgroundProcesses: z.number().int().nonnegative().nullish(),
  gameName: shortText(128).nullish(),
  frames: frameStatisticsSchema.nullish(),
  powerPlan: shortText(128).nullish(),
  refreshRateHz: optionalNumber,
});
export type AnalysisWindow = z.infer<typeof analysisWindowSchema>;

export const riskLevels = ['none', 'low', 'medium', 'high'] as const;
export const confidences = ['low', 'medium', 'high'] as const;

/** Validation applied to every AI-produced recommendation before it reaches a user. */
export const aiRecommendationSchema = z.object({
  id: z.string().regex(/^[a-z0-9-]{3,48}$/),
  title: shortText(120).min(3),
  why: shortText(600).min(10),
  evidence: z.array(shortText(200).min(3)).min(1).max(6),
  risk: z.enum(riskLevels),
  advice: shortText(800).min(10),
  action: z
    .object({
      label: shortText(60).min(2),
      ruleId: z.enum(AI_ALLOWED_ACTION_RULES),
      parameters: z.record(z.string().regex(/^[a-zA-Z][a-zA-Z0-9_-]{0,31}$/), z.string().max(64).regex(/^[A-Za-z0-9 ._:,-]*$/)).optional(),
    })
    .nullish(),
  confidence: z.enum(confidences),
});
export type AiRecommendation = z.infer<typeof aiRecommendationSchema>;

export const checkoutSchema = z.object({
  tier: z.enum(['pro', 'ultimate']),
  interval: z.enum(['month', 'year']).default('month'),
});


export const releaseSchema = z.object({
  version: semverString(),
  channel: z.enum(['stable', 'beta']),
  arch: z.enum(['x64', 'arm64']),
  url: z.string().url().startsWith('https://', 'Release downloads must use HTTPS.'),
  sha256: z.string().regex(/^[a-fA-F0-9]{64}$/).transform((value) => value.toLowerCase()),
  notes: shortText(4000).optional(),
  sizeBytes: z.number().int().positive().max(4 * 1024 * 1024 * 1024),
  publishedAt: isoDate.optional(),
});

export const announcementSchema = z.object({
  title: shortText(120).min(3),
  body: shortText(2000).min(3),
  severity: z.enum(['info', 'warning', 'critical']),
  active: z.boolean().default(true),
  startsAt: isoDate.nullish(),
  endsAt: isoDate.nullish(),
});

/** A game profile document; the full document is stored, the header fields are enforced. */
export const gameProfileSchema = z
  .object({
    schemaVersion: z.literal(1),
    id: z.string().regex(/^[a-z0-9][a-z0-9-]{1,63}$/),
    version: semverString(),
    name: shortText(128).min(1),
    publisher: shortText(128).optional(),
    detection: z.object({
      executables: z.array(z.object({ name: z.string().regex(/^[A-Za-z0-9 ._()+-]{1,128}$/) }).passthrough()).max(16).default([]),
      launchers: z.array(z.object({ launcher: shortText(32), gameId: shortText(128) })).max(16).default([]),
    }),
  })
  .passthrough()
  .refine((profile) => JSON.stringify(profile).length <= 64 * 1024, 'The profile is larger than 64 KB.');

export const adminUserUpdateSchema = z
  .object({
    role: z.enum(['user', 'support', 'admin']).optional(),
    disabled: z.boolean().optional(),
    compTier: z.enum(['pro', 'ultimate']).nullable().optional(),
    compExpiresAt: isoDate.nullable().optional(),
  })
  .refine((value) => Object.keys(value).length > 0, 'Nothing to update.');

export const paginationSchema = z.object({
  page: z.coerce.number().int().min(1).max(10_000).default(1),
  pageSize: z.coerce.number().int().min(1).max(100).default(25),
  q: shortText(254).optional(),
});

export const benchmarkListSchema = paginationSchema.extend({ type: z.enum(benchmarkTypes).optional() });

export const releaseQuerySchema = z.object({
  channel: z.enum(['stable', 'beta']).default('stable'),
  arch: z.enum(['x64', 'arm64']).default('x64'),
});

/** Formats a Zod error into one user-safe sentence. */
export function describeZodError(error: z.ZodError): string {
  const issue = error.issues[0];
  if (!issue) return 'The request is invalid.';
  const path = issue.path.length > 0 ? `${issue.path.join('.')}: ` : '';
  return `${path}${issue.message}`.slice(0, 300);
}

export { z };
