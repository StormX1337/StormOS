import type { Recommendation } from '@storm/types';
import type { AnalysisWindow } from '@storm/validation';

const f = (value: number, digits = 0): string => value.toFixed(digits);

/**
 * Deterministic, explainable analysis. A server-side port of StormOS.Services.Analysis.RuleBasedAnalysisEngine
 * with identical thresholds and ids, so desktop and cloud results agree.
 */
export function analyzeWithRules(window: AnalysisWindow): Recommendation[] {
  const list: Recommendation[] = [];
  if (window.sampleCount < 5) return list;
  const gaming = !!window.gameName;
  const add = (recommendation: Omit<Recommendation, 'source'>) => list.push({ ...recommendation, source: 'rules' });

  const gpu = window.averageGpuUsage;
  const core = window.averageMaxCoreUsage;
  if (gaming && gpu != null && core != null && gpu < 75 && core > 90) {
    add({
      id: 'cpu-limited',
      title: 'The game is CPU-limited',
      why: 'Your GPU utilization is consistently below CPU utilization while the game is running. This may indicate a CPU-limited workload.',
      evidence: [`Average GPU usage: ${f(gpu)} %`, `Busiest CPU thread: ${f(core)} % on average`, `Game: ${window.gameName}`],
      risk: 'none',
      advice: 'Raise the resolution or graphics quality (it will cost little), lower CPU-heavy settings (view distance, crowd/physics detail), and close busy background programs.',
      confidence: gpu < 60 ? 'high' : 'medium',
    });
  }

  if (gaming && gpu != null && gpu > 97) {
    add({
      id: 'gpu-limited',
      title: 'The game is GPU-limited',
      why: 'The GPU is fully busy, so it sets the frame rate.',
      evidence: [`Average GPU usage: ${f(gpu)} %`],
      risk: 'none',
      advice: 'Lower the resolution scale or GPU-heavy settings, or enable an upscaler (DLSS/FSR/XeSS). Enable NVIDIA Reflex / AMD Anti-Lag to keep latency low while GPU-bound.',
      confidence: 'high',
    });
  }

  if (window.averageVramUsage != null && window.averageVramUsage > 92) {
    add({
      id: 'vram-full',
      title: 'Video memory is nearly full',
      why: 'When VRAM runs out, textures are streamed over PCIe, which causes stutter.',
      evidence: [`Average VRAM usage: ${f(window.averageVramUsage)} %`],
      risk: 'none',
      advice: 'Lower texture quality or the texture streaming budget by one step.',
      confidence: 'high',
    });
  }

  if (window.averageRamUsage != null && window.averageRamUsage > 88) {
    add({
      id: 'ram-pressure',
      title: 'System memory is under pressure',
      why: 'Above ~90 % memory use Windows starts paging, which causes hitches.',
      evidence: [`Average RAM usage: ${f(window.averageRamUsage)} %`, `Running processes: ${window.processCount ?? 'unknown'}`],
      risk: 'low',
      advice: 'Close browsers and launchers you do not need while playing, and review startup programs.',
      confidence: 'high',
    });
  }

  if (window.maxCpuTemperature != null && window.maxCpuTemperature > 90) {
    add({
      id: 'cpu-hot',
      title: 'CPU temperature is very high',
      why: 'Near its limit the CPU lowers its clock speed (thermal throttling).',
      evidence: [`Peak thermal zone temperature: ${f(window.maxCpuTemperature)} °C`],
      risk: 'none',
      advice: 'Check cooler mounting and case airflow, and clean dust filters.',
      confidence: 'medium',
    });
  }

  if (window.maxGpuTemperature != null && window.maxGpuTemperature > 85) {
    add({
      id: 'gpu-hot',
      title: 'GPU temperature is very high',
      why: 'Most GPUs reduce boost clocks above roughly 83–87 °C.',
      evidence: [`Peak GPU temperature: ${f(window.maxGpuTemperature)} °C`],
      risk: 'none',
      advice: 'Improve case airflow, clean the GPU fans, or cap the frame rate slightly below your refresh rate.',
      confidence: 'high',
    });
  }

  const frames = window.frames;
  if (frames && frames.frameCount > 500 && frames.averageFps > 0) {
    const ratio = frames.onePercentLowFps / frames.averageFps;
    if (ratio < 0.5) {
      add({
        id: 'stutter',
        title: 'Frame pacing is uneven',
        why: 'The slowest 1 % of frames are much slower than average, which is felt as stutter.',
        evidence: [`Average: ${f(frames.averageFps)} FPS`, `1% low: ${f(frames.onePercentLowFps)} FPS (${f(ratio * 100)} % of average)`, `Stutter frames: ${frames.stutterCount ?? 0}`],
        risk: 'none',
        advice: "A frame rate cap a little below the average (for example with the game's own limiter) often smooths frame times. Also check VRAM and disk activity.",
        confidence: 'high',
      });
    }

    const hz = window.refreshRateHz;
    if (hz != null && frames.averageFps < hz * 0.6) {
      add({
        id: 'below-refresh',
        title: "Frame rate is well below your display's refresh rate",
        why: 'Your monitor can show more frames than the game currently produces.',
        evidence: [`Average: ${f(frames.averageFps)} FPS`, `Refresh rate: ${f(hz)} Hz`],
        risk: 'none',
        advice: 'See whether the game is CPU- or GPU-limited (above) and adjust the matching settings.',
        confidence: 'medium',
      });
    }
  }

  if (gaming && window.averageDiskActive != null && window.averageDiskActive > 80) {
    add({
      id: 'disk-busy',
      title: 'The disk is saturated during gameplay',
      why: 'When the disk is constantly busy, asset streaming stalls and causes hitches.',
      evidence: [`Average active time of the busiest disk: ${f(window.averageDiskActive)} %`],
      risk: 'none',
      advice: 'Pause downloads and updates while playing, and install the game on an SSD if it is on a hard disk.',
      confidence: 'medium',
    });
  }

  if (window.averageLatencyMs != null && window.averageLatencyMs > 80) {
    add({
      id: 'latency-high',
      title: 'Internet latency is high',
      why: 'Latency above ~80 ms is noticeable in fast online games.',
      evidence: [`Average latency: ${f(window.averageLatencyMs)} ms`],
      risk: 'none',
      advice: 'Use a wired connection, stop large downloads, and run the Network test to check jitter and packet loss.',
      confidence: 'medium',
    });
  }

  if (gaming && window.powerPlan && window.powerPlan.toLowerCase().includes('saver')) {
    add({
      id: 'power-saver',
      title: 'Power saver plan is active while gaming',
      why: 'Power saver limits CPU clocks.',
      evidence: [`Active plan: ${window.powerPlan}`],
      risk: 'low',
      advice: 'Switch to Balanced or High performance while playing.',
      action: { label: 'Switch to Balanced', ruleId: 'power.plan', parameters: { plan: 'balanced' } },
      confidence: 'high',
    });
  }

  if (gaming && window.busyBackgroundProcesses != null && window.busyBackgroundProcesses > 3) {
    add({
      id: 'background-busy',
      title: 'Several background programs are using the CPU',
      why: 'They compete with the game for CPU time.',
      evidence: [`Busy background processes: ${window.busyBackgroundProcesses}`],
      risk: 'low',
      advice: 'Close them or lower their priority from the Processes page.',
      confidence: 'medium',
    });
  }

  return list;
}
