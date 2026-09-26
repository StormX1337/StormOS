import type { AnalysisWindow } from '@storm/validation';

/** Every numeric measurement in the window (and derived ratios), used to verify AI evidence. */
export function measuredValues(window: AnalysisWindow): number[] {
  const values: number[] = [];
  const push = (value: unknown) => {
    if (typeof value === 'number' && Number.isFinite(value)) values.push(value);
  };
  for (const [key, value] of Object.entries(window)) {
    if (key !== 'frames') push(value);
  }

  if (window.frames) {
    for (const value of Object.values(window.frames)) push(value);
    if (window.frames.averageFps > 0) push((window.frames.onePercentLowFps / window.frames.averageFps) * 100);
  }

  return values;
}

/**
 * An evidence line is grounded when it quotes at least one measured number (±1 % or ±0.6 absolute, to allow
 * rounding) or names the measured game or power plan. Recommendations with any ungrounded evidence are dropped,
 * so the AI can explain measurements but never introduce values of its own.
 */
export function isGrounded(evidence: string, window: AnalysisWindow, values: readonly number[] = measuredValues(window)): boolean {
  const text = evidence.toLowerCase();
  if ((window.gameName && text.includes(window.gameName.toLowerCase())) || (window.powerPlan && text.includes(window.powerPlan.toLowerCase()))) {
    return true;
  }

  const numbers = [...evidence.matchAll(/-?\d+(?:[.,]\d+)?/g)].map((match) => Number(match[0].replace(',', '.')));
  return numbers.some((quoted) => values.some((measured) => Math.abs(measured - quoted) <= Math.max(0.6, Math.abs(measured) * 0.01)));
}
