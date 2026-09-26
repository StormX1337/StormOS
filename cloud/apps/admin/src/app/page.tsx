import { Alert, Card, CardTitle, CardValue } from '@storm/ui';
import { AdminShell } from '@/components/admin-shell';
import { load, requireStaff } from '@/lib/guard';

export const dynamic = 'force-dynamic';

interface Stats {
  users: number;
  newUsersLast24h: number;
  devices: number;
  benchmarks: number;
  sessions: number;
  aiAnalysesLast24h: number;
  activeSubscriptions: Record<string, number>;
}

export default async function Overview() {
  const { token, staff } = await requireStaff('/');
  const stats = await load<Stats>('admin/stats', token);
  const cards: Array<[string, number | string]> =
    typeof stats === 'string'
      ? []
      : [
          ['Users', stats.users],
          ['New users (24 h)', stats.newUsersLast24h],
          ['Registered PCs', stats.devices],
          ['Pro subscriptions', stats.activeSubscriptions.pro ?? 0],
          ['Ultimate subscriptions', stats.activeSubscriptions.ultimate ?? 0],
          ['Synced benchmarks', stats.benchmarks],
          ['Synced sessions', stats.sessions],
          ['AI analyses (24 h)', stats.aiAnalysesLast24h],
        ];
  return (
    <AdminShell staff={staff} title="Overview">
      {typeof stats === 'string' ? <Alert tone="error">{stats}</Alert> : null}
      <div className="grid gap-4 md:grid-cols-4">
        {cards.map(([label, value]) => (
          <Card key={label}>
            <CardTitle>{label}</CardTitle>
            <CardValue>{value}</CardValue>
          </Card>
        ))}
      </div>
    </AdminShell>
  );
}
