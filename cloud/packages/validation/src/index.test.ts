import { describe, expect, it } from 'vitest';
import { aiRecommendationSchema, analysisWindowSchema, benchmarkUploadSchema, gameProfileSchema, registerSchema, releaseSchema } from './index';

describe('validation', () => {
  it('normalizes e-mail and enforces password length', () => {
    expect(registerSchema.parse({ email: ' Player@Example.COM ', password: 'correct horse battery' }).email).toBe('player@example.com');
    expect(registerSchema.safeParse({ email: 'a@b.co', password: 'short' }).success).toBe(false);
  });

  it('accepts the desktop benchmark payload and strips unknown keys', () => {
    const parsed = benchmarkUploadSchema.parse({
      id: '1f0b0d44-4a86-4b8f-9d9e-3c3f7d1b2a10',
      type: 'cpu',
      startedAt: '2026-09-26T10:00:00+00:00',
      duration: '00:00:10.1234',
      hardware: 'CPU · GPU · 32 GB RAM',
      metrics: [{ key: 'cpu.multi.mops', name: 'Multi-thread', value: 9000, unit: 'MOPS', higherIsBetter: true }],
      completed: true,
      appVersion: '1.0.0',
      unexpected: 'dropped',
    });
    expect(parsed).not.toHaveProperty('unexpected');
    expect(parsed.metrics[0]?.value).toBe(9000);
  });

  it('rejects non-finite metric values', () => {
    const result = benchmarkUploadSchema.safeParse({ id: '1f0b0d44-4a86-4b8f-9d9e-3c3f7d1b2a10', type: 'cpu', startedAt: '2026-09-26T10:00:00Z', metrics: [{ key: 'x', name: 'x', value: Number.NaN, unit: 'x' }], completed: true });
    expect(result.success).toBe(false);
  });

  it('only lets AI recommendations reference allow-listed reversible rules', () => {
    const base = { id: 'power-saver', title: 'Power saver plan', why: 'The power saver plan limits clocks.', evidence: ['Power plan: Power saver'], risk: 'low', advice: 'Switch to Balanced while gaming.', confidence: 'high' };
    expect(aiRecommendationSchema.safeParse({ ...base, action: { label: 'Switch', ruleId: 'power.plan', parameters: { plan: 'balanced' } } }).success).toBe(true);
    expect(aiRecommendationSchema.safeParse({ ...base, action: { label: 'Disable', ruleId: 'service.start-type' } }).success).toBe(false);
    expect(aiRecommendationSchema.safeParse({ ...base, action: { label: 'x', ruleId: 'power.plan', parameters: { plan: 'a; rm -rf' } } }).success).toBe(false);
  });

  it('requires https release URLs and a SHA-256', () => {
    const release = { version: '1.2.0', channel: 'stable', arch: 'x64', url: 'https://downloads.example.com/StormOS-1.2.0.msi', sha256: 'A'.repeat(64), sizeBytes: 1000 };
    expect(releaseSchema.parse(release).sha256).toBe('a'.repeat(64));
    expect(releaseSchema.safeParse({ ...release, url: 'http://downloads.example.com/x.msi' }).success).toBe(false);
  });

  it('validates analysis windows and game profiles', () => {
    expect(analysisWindowSchema.safeParse({ sampleCount: 120, averageCpuUsage: 50, gameName: 'CS2' }).success).toBe(true);
    expect(gameProfileSchema.safeParse({ schemaVersion: 1, id: 'cs2', version: '1.0.0', name: 'Counter-Strike 2', detection: { executables: [{ name: 'cs2.exe' }] } }).success).toBe(true);
    expect(gameProfileSchema.safeParse({ schemaVersion: 1, id: '../etc', version: '1.0.0', name: 'x', detection: {} }).success).toBe(false);
  });
});
