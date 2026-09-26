import type { EntitlementSummary } from '@storm/types';
import { Alert, Badge, Button, Card, CardTitle, Input, Label, Select, Table, Td, Th } from '@storm/ui';
import { updateUser } from '@/app/actions';
import { AdminShell } from '@/components/admin-shell';
import { load, requireStaff } from '@/lib/guard';

export const dynamic = 'force-dynamic';

interface Detail {
  id: string;
  email: string;
  role: string;
  disabled: boolean;
  compTier: string | null;
  compExpiresAt: string | null;
  createdAt: string;
  lastLoginAt: string | null;
  devices: Array<{ id: string; name: string; hardware: string; lastSeenAt: string; revokedAt: string | null }>;
  subscriptions: Array<{ id: string; tier: string; status: string; currentPeriodEnd: string; cancelAtPeriodEnd: boolean }>;
  entitlements: EntitlementSummary;
}

export default async function UserDetail({ params, searchParams }: { params: Promise<{ id: string }>; searchParams: Promise<{ ok?: string; error?: string }> }) {
  const { id } = await params;
  const notice = await searchParams;
  const { token, staff } = await requireStaff(`/users/${id}`);
  const user = await load<Detail>(`admin/users/${encodeURIComponent(id)}`, token);
  if (typeof user === 'string') {
    return <AdminShell staff={staff} title="User"><Alert tone="error">{user}</Alert></AdminShell>;
  }

  const readOnly = staff.role !== 'admin';
  return (
    <AdminShell staff={staff} title={user.email} notice={notice}>
      <div className="grid gap-4 md:grid-cols-2">
        <Card>
          <CardTitle>Entitlements</CardTitle>
          <p className="mt-3 text-2xl font-semibold capitalize">{user.entitlements.tier}</p>
          <p className="text-sm text-storm-muted">Source: {user.entitlements.source}{user.entitlements.expiresAt ? ` · until ${new Date(user.entitlements.expiresAt).toLocaleDateString('en-US')}` : ''}</p>
          <div className="mt-3 flex flex-wrap gap-1">{user.entitlements.features.map((feature) => <Badge key={feature}>{feature}</Badge>)}</div>
        </Card>
        <Card>
          <CardTitle>Account settings</CardTitle>
          {readOnly ? <p className="mt-3 text-sm text-storm-muted">Support staff have read-only access.</p> : null}
          <form action={updateUser} className="mt-4 grid gap-3">
            <input type="hidden" name="id" value={user.id} />
            <div>
              <Label htmlFor="role">Role</Label>
              <Select id="role" name="role" defaultValue={user.role.toLowerCase()} disabled={readOnly}>
                <option value="user">User</option>
                <option value="support">Support</option>
                <option value="admin">Admin</option>
              </Select>
            </div>
            <div className="grid grid-cols-2 gap-3">
              <div>
                <Label htmlFor="compTier">Complimentary plan</Label>
                <Select id="compTier" name="compTier" defaultValue={user.compTier?.toLowerCase() ?? 'none'} disabled={readOnly}>
                  <option value="none">None</option>
                  <option value="pro">Pro</option>
                  <option value="ultimate">Ultimate</option>
                </Select>
              </div>
              <div>
                <Label htmlFor="compExpiresAt">Grant expires</Label>
                <Input id="compExpiresAt" name="compExpiresAt" type="date" defaultValue={user.compExpiresAt?.slice(0, 10)} disabled={readOnly} />
              </div>
            </div>
            <label className="flex items-center gap-2 text-sm">
              <input type="checkbox" name="disabled" defaultChecked={user.disabled} disabled={readOnly} /> Account disabled (signs out every session)
            </label>
            <Button disabled={readOnly}>Save changes</Button>
          </form>
        </Card>
      </div>
      <Card>
        <CardTitle>Subscriptions</CardTitle>
        <Table className="mt-3">
          <thead><tr><Th>Plan</Th><Th>Status</Th><Th>Period end</Th><Th>Cancels</Th></tr></thead>
          <tbody>
            {user.subscriptions.map((subscription) => (
              <tr key={subscription.id}>
                <Td>{subscription.tier}</Td>
                <Td>{subscription.status}</Td>
                <Td>{new Date(subscription.currentPeriodEnd).toLocaleDateString('en-US')}</Td>
                <Td>{subscription.cancelAtPeriodEnd ? 'At period end' : '—'}</Td>
              </tr>
            ))}
          </tbody>
        </Table>
      </Card>
      <Card>
        <CardTitle>PCs</CardTitle>
        <Table className="mt-3">
          <thead><tr><Th>Name</Th><Th>Hardware</Th><Th>Last seen</Th><Th>Status</Th></tr></thead>
          <tbody>
            {user.devices.map((device) => (
              <tr key={device.id}>
                <Td>{device.name}</Td>
                <Td className="text-storm-muted">{device.hardware || '—'}</Td>
                <Td>{new Date(device.lastSeenAt).toLocaleString('en-US')}</Td>
                <Td>{device.revokedAt ? <Badge>Removed</Badge> : <Badge tone="success">Active</Badge>}</Td>
              </tr>
            ))}
          </tbody>
        </Table>
      </Card>
    </AdminShell>
  );
}
