import { redirect } from 'next/navigation';
import type { EntitlementSummary, Paged } from '@storm/types';
import { Alert, Badge, Button, Card, CardTitle, CardValue, Table, Td, Th } from '@storm/ui';
import { api, ApiRequestError } from '@/lib/api';
import { readSession } from '@/lib/session';
import { logout, openBillingPortal, removeDevice } from '../actions';

export const dynamic = 'force-dynamic';
export const metadata = { title: 'Account' };

interface Me {
  id: string;
  email: string;
  role: string;
  entitlements: EntitlementSummary;
}

interface Device {
  id: string;
  name: string;
  hardware: string;
  lastSeenAt: string;
}

interface Benchmark {
  id: string;
  type: string;
  startedAt: string;
  label: string | null;
  gameName: string | null;
  completed: boolean;
  score: number | null;
}

export default async function AccountPage({ searchParams }: { searchParams: Promise<{ checkout?: string; error?: string }> }) {
  const { accessToken, refreshToken } = await readSession();
  if (!accessToken) {
    redirect(refreshToken ? '/api/session/refresh?next=/account' : '/login');
  }

  const params = await searchParams;
  let me: Me;
  let devices: Device[];
  let benchmarks: Paged<Benchmark> | null = null;
  try {
    [me, devices] = await Promise.all([api<Me>('auth/me', { token: accessToken }), api<Device[]>('devices', { token: accessToken })]);
    benchmarks = await api<Paged<Benchmark>>('benchmarks?pageSize=10', { token: accessToken }).catch(() => null);
  } catch (error) {
    if (error instanceof ApiRequestError && error.status === 401) redirect('/api/session/refresh?next=/account');
    return <Alert tone="error">{error instanceof Error ? error.message : 'The account could not be loaded.'}</Alert>;
  }

  const plan = me.entitlements;
  return (
    <div className="space-y-8">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-3xl font-semibold">Account</h1>
          <p className="text-storm-muted">{me.email}</p>
        </div>
        <form action={logout}>
          <Button variant="outline">Sign out</Button>
        </form>
      </div>
      {params.checkout === 'success' ? <Alert tone="success">Thank you! Your plan is being activated; STORM OS picks it up within a minute.</Alert> : null}
      {params.error ? <Alert tone="error">{params.error}</Alert> : null}
      <div className="grid gap-4 md:grid-cols-3">
        <Card>
          <CardTitle>Plan</CardTitle>
          <CardValue className="capitalize">{plan.tier}</CardValue>
          <p className="mt-2 text-sm text-storm-muted">
            {plan.source === 'subscription' ? 'Subscription' : plan.source === 'complimentary' ? 'Complimentary' : 'Free'}
            {plan.expiresAt ? ` · renews or ends ${new Date(plan.expiresAt).toLocaleDateString('en-US', { dateStyle: 'medium' })}` : ''}
          </p>
          <form action={openBillingPortal} className="mt-4">
            <Button variant="outline" size="sm">Manage billing</Button>
          </form>
        </Card>
        <Card>
          <CardTitle>Registered PCs</CardTitle>
          <CardValue>{devices.length}</CardValue>
          <p className="mt-2 text-sm text-storm-muted">Register a PC from STORM OS › Settings › STORM Cloud.</p>
        </Card>
        <Card>
          <CardTitle>Synced benchmarks</CardTitle>
          <CardValue>{benchmarks?.total ?? '—'}</CardValue>
          <p className="mt-2 text-sm text-storm-muted">{plan.features.includes('cloud.sync') ? 'Cloud sync is included in your plan.' : 'Cloud sync is part of Pro and Ultimate.'}</p>
        </Card>
      </div>
      <Card>
        <CardTitle>PCs</CardTitle>
        {devices.length === 0 ? (
          <p className="mt-4 text-sm text-storm-muted">No PCs registered yet.</p>
        ) : (
          <Table className="mt-4">
            <thead><tr><Th>Name</Th><Th>Hardware</Th><Th>Last seen</Th><Th /></tr></thead>
            <tbody>
              {devices.map((device) => (
                <tr key={device.id}>
                  <Td>{device.name}</Td>
                  <Td className="text-storm-muted">{device.hardware || '—'}</Td>
                  <Td>{new Date(device.lastSeenAt).toLocaleString('en-US')}</Td>
                  <Td className="text-right">
                    <form action={removeDevice}>
                      <input type="hidden" name="id" value={device.id} />
                      <Button variant="ghost" size="sm">Remove</Button>
                    </form>
                  </Td>
                </tr>
              ))}
            </tbody>
          </Table>
        )}
      </Card>
      {benchmarks && benchmarks.items.length > 0 ? (
        <Card>
          <CardTitle>Recent benchmarks</CardTitle>
          <Table className="mt-4">
            <thead><tr><Th>When</Th><Th>Type</Th><Th>Label</Th><Th>Score</Th><Th>Status</Th></tr></thead>
            <tbody>
              {benchmarks.items.map((run) => (
                <tr key={run.id}>
                  <Td>{new Date(run.startedAt).toLocaleString('en-US')}</Td>
                  <Td className="uppercase">{run.type}{run.gameName ? ` · ${run.gameName}` : ''}</Td>
                  <Td>{run.label ?? '—'}</Td>
                  <Td>{run.score != null ? Math.round(run.score) : '—'}</Td>
                  <Td>{run.completed ? <Badge tone="success">Completed</Badge> : <Badge tone="warning">Failed</Badge>}</Td>
                </tr>
              ))}
            </tbody>
          </Table>
        </Card>
      ) : null}
    </div>
  );
}
