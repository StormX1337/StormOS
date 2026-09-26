import { describe, expect, it } from 'vitest';
import type { AppConfig } from '../config/app-config';
import { ClaudeAnalysisProvider } from './claude-analysis.provider';
import { isGrounded } from './grounding';
import { analyzeWithRules } from './rules-engine';

const window = { sampleCount: 120, gameName: 'Test Game', averageGpuUsage: 55, averageMaxCoreUsage: 97, averageRamUsage: 60, powerPlan: 'Power saver', refreshRateHz: 144 };

describe('rules engine', () => {
  it('matches the desktop rules', () => {
    const ids = analyzeWithRules(window).map((r) => r.id);
    expect(ids).toEqual(['cpu-limited', 'power-saver']);
    const power = analyzeWithRules(window).find((r) => r.id === 'power-saver');
    expect(power?.action).toEqual({ label: 'Switch to Balanced', ruleId: 'power.plan', parameters: { plan: 'balanced' } });
  });

  it('needs enough samples', () => {
    expect(analyzeWithRules({ ...window, sampleCount: 3 })).toEqual([]);
  });
});

describe('AI output acceptance', () => {
  const provider = new ClaudeAnalysisProvider({ AI_PROVIDER: 'rules' } as AppConfig);

  it('accepts grounded, schema-valid recommendations', () => {
    const accepted = provider.accept(
      { recommendations: [{ id: 'gpu-idle', title: 'GPU waits for the CPU', why: 'The GPU is only 55 % busy while one CPU thread is saturated.', evidence: ['GPU usage 55 %', 'Busiest thread 97 %'], risk: 'none', advice: 'Raise the resolution or lower simulation settings.', confidence: 'high', action: null }] },
      window,
    );
    expect(accepted).toHaveLength(1);
    expect(accepted[0]).toMatchObject({ id: 'ai-gpu-idle', source: 'ai' });
  });

  it('drops invented numbers and disallowed actions', () => {
    const invented = { id: 'fake', title: 'Invented value', why: 'Temperature is dangerously high.', evidence: ['CPU at 104 °C'], risk: 'none', advice: 'Replace the cooler immediately.', confidence: 'high', action: null };
    const unsafe = { id: 'unsafe', title: 'Disable a service', why: 'Some service uses resources.', evidence: ['GPU usage 55 %'], risk: 'high', advice: 'Disable it.', confidence: 'low', action: { label: 'Disable', ruleId: 'service.start-type' } };
    expect(provider.accept({ recommendations: [invented, unsafe] }, window)).toEqual([]);
    expect(provider.accept('not json', window)).toEqual([]);
  });

  it('grounds evidence on measured numbers or names', () => {
    expect(isGrounded('Average GPU usage: 55.4 %', window)).toBe(true);
    expect(isGrounded('Active plan: Power saver', window)).toBe(true);
    expect(isGrounded('VRAM at 99 %', window)).toBe(false);
  });
});
