import Link from 'next/link';
import type { Paged } from '@storm/types';
import { Alert, Button, Input, Table, Td, Th } from '@storm/ui';
import { AdminShell } from '@/components/admin-shell';
import { load, requireStaff } from '@/lib/guard';

export const dynamic = 'force-dynamic';

interface Entry {
  id: string;
  actorId: string | null;
  action: string;
  target: string | null;
  details: unknown;
  ip: string | null;
  createdAt: string;
}

export default async function Audit({ searchParams }: { searchParams: Promise<{ q?: string; page?: string }> }) {
  const { q = '', page = '1' } = await searchParams;
  const { token, staff } = await requireStaff('/audit');
  const query = new URLSearchParams({ page: String(Math.max(1, Number(page) || 1)), pageSize: '50', ...(q ? { q } : {}) });
  const entries = await load<Paged<Entry>>(`admin/audit?${query}`, token);
  const current = Number(query.get('page'));
  return (
    <AdminShell staff={staff} title="Audit log">
      <form className="flex max-w-md gap-2">
        <Input name="q" defaultValue={q} placeholder="Filter by action prefix, e.g. admin. or auth." />
        <Button variant="outline">Filter</Button>
      </form>
      {typeof entries === 'string' ? <Alert tone="error">{entries}</Alert> : (
        <>
          <Table>
            <thead><tr><Th>Time</Th><Th>Action</Th><Th>Actor</Th><Th>Target</Th><Th>Details</Th><Th>IP</Th></tr></thead>
            <tbody>
              {entries.items.map((entry) => (
                <tr key={entry.id}>
                  <Td className="whitespace-nowrap">{new Date(entry.createdAt).toLocaleString('en-US')}</Td>
                  <Td className="font-mono text-xs">{entry.action}</Td>
                  <Td className="font-mono text-xs">{entry.actorId ? <Link className="text-storm-accent" href={`/users/${entry.actorId}`}>{entry.actorId.slice(0, 8)}</Link> : '—'}</Td>
                  <Td className="font-mono text-xs">{entry.target ?? '—'}</Td>
                  <Td className="font-mono text-xs text-storm-muted">{entry.details ? JSON.stringify(entry.details) : '—'}</Td>
                  <Td className="font-mono text-xs">{entry.ip ?? '—'}</Td>
                </tr>
              ))}
            </tbody>
          </Table>
          <div className="flex gap-4 text-sm text-storm-muted">
            {current > 1 ? <Link href={`/audit?q=${encodeURIComponent(q)}&page=${current - 1}`}>← Newer</Link> : null}
            {current * entries.pageSize < entries.total ? <Link href={`/audit?q=${encodeURIComponent(q)}&page=${current + 1}`}>Older →</Link> : null}
          </div>
        </>
      )}
    </AdminShell>
  );
}
