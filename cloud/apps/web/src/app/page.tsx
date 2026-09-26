import Link from 'next/link';
import { buttonVariants, Card, CardTitle } from '@storm/ui';

const pillars = [
  { title: 'Real telemetry', body: 'CPU, GPU, memory, storage, network and frame times from Windows performance counters, D3DKMT, NVML and PresentMon/ETW. Values a sensor does not report are shown as unavailable — never estimated.' },
  { title: 'Explainable optimizations', body: 'Every change states why, what it changes, the risk and whether a restart is needed. It is backed up, applied, verified and logged — and can be restored individually or all at once.' },
  { title: 'Honest benchmarks', body: 'CPU, GPU compute, memory, storage, network and in-game FPS with 1% and 0.1% lows. Before/after differences only appear when both runs were actually measured.' },
  { title: 'Anti-cheat safe', body: 'No injection, no hooks, no driver tricks. The overlay is a normal click-through window, and STORM OS never touches Windows security, Defender, Secure Boot or anti-cheat software.' },
  { title: 'Network insight', body: 'Latency, jitter, packet loss, DNS response times and route — combined into a transparent STORM Network Score with every input and weight visible.' },
  { title: 'Private by default', body: 'Everything runs locally. Cloud sync, crash reports and AI analysis are opt-in, and the AI only ever sees aggregated measurements.' },
];

export default function Home() {
  return (
    <div className="space-y-16">
      <section className="space-y-6 pt-8 text-center">
        <p className="text-xs font-semibold uppercase tracking-[0.4em] text-storm-accent">Windows 11 · Gaming · Performance</p>
        <h1 className="text-4xl font-semibold tracking-tight md:text-6xl">The Windows gaming performance command center.</h1>
        <p className="mx-auto max-w-2xl text-lg text-storm-muted">
          STORM OS measures your PC while you play, explains what limits performance and applies only the changes you approve.
        </p>
        <div className="flex justify-center gap-3">
          <Link href="/download" className={buttonVariants({ size: 'lg' })}>Download STORM OS</Link>
          <Link href="/pricing" className={buttonVariants({ size: 'lg', variant: 'outline' })}>Compare plans</Link>
        </div>
      </section>
      <section className="grid gap-4 md:grid-cols-3">
        {pillars.map((pillar) => (
          <Card key={pillar.title}>
            <CardTitle>{pillar.title}</CardTitle>
            <p className="mt-3 text-sm leading-relaxed text-storm-muted">{pillar.body}</p>
          </Card>
        ))}
      </section>
    </div>
  );
}
