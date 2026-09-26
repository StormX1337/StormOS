import { describe, expect, it } from 'vitest';
import { FEATURES, TIER_FEATURES, maxTier } from './index';

describe('tiers', () => {
  it('higher tiers include every lower-tier feature', () => {
    for (const feature of TIER_FEATURES.free) expect(TIER_FEATURES.pro).toContain(feature);
    for (const feature of TIER_FEATURES.pro) expect(TIER_FEATURES.ultimate).toContain(feature);
  });

  it('keeps AI analysis and cloud sync out of the free tier', () => {
    expect(TIER_FEATURES.free).not.toContain(FEATURES.aiAnalysis);
    expect(TIER_FEATURES.free).not.toContain(FEATURES.cloudSync);
    expect(TIER_FEATURES.ultimate).toContain(FEATURES.aiAnalysis);
  });

  it('maxTier picks the higher tier', () => {
    expect(maxTier('free', 'pro')).toBe('pro');
    expect(maxTier('ultimate', 'pro')).toBe('ultimate');
  });
});
