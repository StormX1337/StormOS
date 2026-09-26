/**
 * Shared STORM Cloud contracts. Field names and enum values match the desktop client
 * (System.Text.Json web defaults: camelCase properties, camelCase enum strings).
 */

export const LICENSE_TIERS = ['free', 'pro', 'ultimate'] as const;
export type LicenseTier = (typeof LICENSE_TIERS)[number];

/** Feature keys; identical to StormOS.Core.Licensing.Features. */
export const FEATURES = {
  monitoring: 'monitoring',
  gameDetection: 'game-detection',
  basicProfiles: 'profiles.basic',
  advancedOptimization: 'optimization.advanced',
  benchmark: 'benchmark',
  overlay: 'overlay',
  networkDiagnostics: 'network.diagnostics',
  aiAnalysis: 'ai.analysis',
  cloudSync: 'cloud.sync',
  advancedHistory: 'history.advanced',
  advancedProfiles: 'profiles.advanced',
} as const;
export type Feature = (typeof FEATURES)[keyof typeof FEATURES];

const FREE: readonly Feature[] = [FEATURES.monitoring, FEATURES.gameDetection, FEATURES.basicProfiles];
const PRO: readonly Feature[] = [
  ...FREE,
  FEATURES.benchmark,
  FEATURES.overlay,
  FEATURES.networkDiagnostics,
  FEATURES.advancedOptimization,
  FEATURES.cloudSync,
  FEATURES.advancedHistory,
];
const ULTIMATE: readonly Feature[] = [...PRO, FEATURES.aiAnalysis, FEATURES.advancedProfiles];

/** Server-side source of truth for what each tier unlocks. */
export const TIER_FEATURES: Readonly<Record<LicenseTier, readonly Feature[]>> = {
  free: FREE,
  pro: PRO,
  ultimate: ULTIMATE,
};

export const TIER_RANK: Readonly<Record<LicenseTier, number>> = { free: 0, pro: 1, ultimate: 2 };

/** Returns the higher of two tiers. */
export function maxTier(a: LicenseTier, b: LicenseTier): LicenseTier {
  return TIER_RANK[a] >= TIER_RANK[b] ? a : b;
}

export const USER_ROLES = ['user', 'support', 'admin'] as const;
export type UserRole = (typeof USER_ROLES)[number];

export interface AuthTokens {
  accessToken: string;
  refreshToken: string;
  /** Access token lifetime in seconds. */
  expiresIn: number;
}

export interface AuthResponse {
  tokens: AuthTokens;
  email: string;
}

export interface CloudDevice {
  id: string;
  name: string;
}

export interface EntitlementTokenResponse {
  token: string;
  tier: LicenseTier;
  features: Feature[];
  expiresAt: string;
}

export interface EntitlementSummary {
  tier: LicenseTier;
  features: Feature[];
  source: 'free' | 'subscription' | 'complimentary';
  expiresAt: string | null;
}

export type ReleaseChannel = 'stable' | 'beta';

export interface ReleaseInfo {
  version: string;
  channel: ReleaseChannel;
  url: string;
  sha256: string;
  notes?: string;
  publishedAt: string;
  sizeBytes: number;
}

export type AnnouncementSeverity = 'info' | 'warning' | 'critical';

export interface Announcement {
  id: string;
  title: string;
  body: string;
  severity: AnnouncementSeverity;
}

export type RiskLevel = 'none' | 'low' | 'medium' | 'high';
export type Confidence = 'low' | 'medium' | 'high';

export interface RecommendedAction {
  label: string;
  ruleId: string;
  parameters?: Record<string, string>;
}

/** Explainable recommendation: Why / Evidence / Risk / Recommendation (advice) / Action. */
export interface Recommendation {
  id: string;
  title: string;
  why: string;
  evidence: string[];
  risk: RiskLevel;
  advice: string;
  action?: RecommendedAction;
  confidence: Confidence;
  source: 'rules' | 'ai';
}

export interface AnalysisResponse {
  recommendations: Recommendation[];
}

/** Error body returned by every API error. The desktop client displays `message`. */
export interface ApiErrorBody {
  statusCode: number;
  code: string;
  message: string;
}

export interface Paged<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}

/** Optimization rule ids the AI analysis may reference as actions (all reversible, all confirmed by the user). */
export const AI_ALLOWED_ACTION_RULES = [
  'power.plan',
  'windows.game-mode',
  'windows.game-dvr',
  'windows.hags',
  'windows.windowed-optimizations',
  'background.lower-priority',
] as const;
