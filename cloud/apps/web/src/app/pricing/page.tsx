import { TIER_FEATURES, type Feature, type LicenseTier } from '@storm/types';
import { Alert, Badge, Button, Card } from '@storm/ui';
import { startCheckout } from '../actions';

export const metadata = { title: 'Pricing' };

const featureNames: Record<Feature, string> = {
  monitoring: 'Live hardware monitoring',
  'game-detection': 'Game detection (Steam, Epic, Xbox, Battle.net, EA, Ubisoft, GOG, Riot)',
  'profiles.basic': 'Game profiles',
  benchmark: 'CPU, GPU, memory, disk, network and FPS benchmarks',
  overlay: 'In-game overlay',
  'network.diagnostics': 'Network diagnostics and STORM Network Score',
  'optimization.advanced': 'Advanced, reversible optimizations',
  'cloud.sync': 'Cloud sync of benchmarks and sessions',
  'history.advanced': 'Extended history',
  'ai.analysis': 'AI analysis with evidence-backed recommendations',
  'profiles.advanced': 'Advanced game profiles',
};

const plans: Array<{ tier: LicenseTier; name: string; tagline: string }> = [
  { tier: 'free', name: 'Free', tagline: 'Monitoring and game detection' },
  { tier: 'pro', name: 'Pro', tagline: 'Benchmarks, overlay, optimizations and sync' },
  { tier: 'ultimate', name: 'Ultimate', tagline: 'Everything, plus AI analysis' },
];

export default async function PricingPage({ searchParams }: { searchParams: Promise<{ error?: string; checkout?: string }> }) {
  const params = await searchParams;
  return (
    <div className="space-y-8">
      <div className="space-y-2 text-center">
        <h1 className="text-4xl font-semibold">Plans</h1>
        <p className="text-storm-muted">Payments are handled by Stripe. Cancel any time; features stay until the end of the paid period.</p>
      </div>
      {params.error ? <Alert tone="error">{params.error}</Alert> : null}
      {params.checkout === 'cancelled' ? <Alert>Checkout was cancelled. Nothing was charged.</Alert> : null}
      <div className="grid gap-4 md:grid-cols-3">
        {plans.map((plan) => (
          <Card key={plan.tier} className={plan.tier === 'pro' ? 'border-storm-accent/60' : undefined}>
            <div className="flex items-center justify-between">
              <h2 className="text-xl font-semibold">{plan.name}</h2>
              {plan.tier === 'pro' ? <Badge tone="accent">Popular</Badge> : null}
            </div>
            <p className="mt-1 text-sm text-storm-muted">{plan.tagline}</p>
            <ul className="mt-6 space-y-2 text-sm">
              {TIER_FEATURES[plan.tier].map((feature) => (
                <li key={feature} className="flex gap-2">
                  <span className="text-storm-success">✓</span>
                  {featureNames[feature]}
                </li>
              ))}
            </ul>
            {plan.tier !== 'free' ? (
              <form action={startCheckout} className="mt-6 space-y-2">
                <input type="hidden" name="tier" value={plan.tier} />
                <div className="grid grid-cols-2 gap-2">
                  <Button name="interval" value="month" variant={plan.tier === 'pro' ? 'default' : 'outline'}>Monthly</Button>
                  <Button name="interval" value="year" variant="outline">Yearly</Button>
                </div>
              </form>
            ) : (
              <p className="mt-6 text-sm text-storm-muted">Included with every download.</p>
            )}
          </Card>
        ))}
      </div>
    </div>
  );
}
