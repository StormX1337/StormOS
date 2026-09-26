import Link from 'next/link';
import type { Paged } from '@storm/types';
import { Alert, Badge, Button, Input, Table, Td, Th } from '@storm/ui';
import { AdminShell } from '@/components/admin-shell';
import { load, requireStaff } from '@/lib/guard';

export const dynamic = 'force-dynamic';

interface Row {
  id: string;
  email: string;
  role: string;
  disabled: boolean;
  compTier: string | null;
  createdAt: string;
  lastLoginAt: string | null;
  _count: { devices: number };
}

export default async function Users({ searchParams }: { searchParams: Promise<{ q?: string; page?: string }> }) {
  const { q = '', page = '1' } = await searchParams;
  const { token, staff } = await requireStaff('/users');
  const query = new URLSearchParams({ page: String(Math.max(1, Number(page) || 1)), pageSize: '25', ...(q ? { q } : {}) });
  const users = await load<Paged<Row>>(`admin/users?${query}`, token);
  const current = Number(query.get('page'));
  return (
    <AdminShell staff={staff} title="Users">
      <form className="flex max-w-md gap-2">
        <Input name="q" defaultValue={q} placeholder="Search by e-mail" />
        <Button variant="outline">Search</Button>
      </form>
      {typeof users === 'string' ? (
        <Alert tone="error">{users}</Alert>
      ) : (
        <>
          <Table>
            <thead><tr><Th>E-mail</Th><Th>Role</Th><Th>Status</Th><Th>Grant</Th><Th>PCs</Th><Th>Created</Th><Th>Last sign-in</Th></tr></thead>
            <tbody>
              {users.items.map((user) => (
                <tr key={user.id}>
                  <Td><Link className="text-storm-accent hover:underline" href={`/users/${user.id}`}>{user.email}</Link></Td>
                  <Td className="uppercase">{user.role}</Td>
                  <Td>{user.disabled ? <Badge tone="danger">Disabled</Badge> : <Badge tone="success">Active</Badge>}</Td>
                  <Td>{user.compTier ?? '—'}</Td>
                  <Td>{user._count.devices}</Td>
                  <Td>{new Date(user.createdAt).toLocaleDateString('en-US')}</Td>
                  <Td>{user.lastLoginAt ? new Date(user.lastLoginAt).toLocaleString('en-US') : '—'}</Td>
                </tr>
              ))}
            </tbody>
          </Table>
          <div className="flex items-center gap-4 text-sm text-storm-muted">
            <span>{users.total} users</span>
            {current > 1 ? <Link href={`/users?q=${encodeURIComponent(q)}&page=${current - 1}`}>← Previous</Link> : null}
            {current * users.pageSize < users.total ? <Link href={`/users?q=${encodeURIComponent(q)}&page=${current + 1}`}>Next →</Link> : null}
          </div>
        </>
      )}
    </AdminShell>
  );
}
