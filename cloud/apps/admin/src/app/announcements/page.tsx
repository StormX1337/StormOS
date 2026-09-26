import { Alert, Badge, Button, Card, CardTitle, Input, Label, Select, Table, Td, Textarea, Th } from '@storm/ui';
import { createAnnouncement, deleteAnnouncement } from '@/app/actions';
import { AdminShell } from '@/components/admin-shell';
import { load, requireStaff } from '@/lib/guard';

export const dynamic = 'force-dynamic';

interface Announcement {
  id: string;
  title: string;
  body: string;
  severity: string;
  active: boolean;
  endsAt: string | null;
  createdAt: string;
}

export default async function Announcements({ searchParams }: { searchParams: Promise<{ ok?: string; error?: string }> }) {
  const notice = await searchParams;
  const { token, staff } = await requireStaff('/announcements');
  const items = await load<Announcement[]>('admin/announcements', token);
  return (
    <AdminShell staff={staff} title="Announcements" notice={notice}>
      <Card>
        <CardTitle>New announcement</CardTitle>
        <form action={createAnnouncement} className="mt-4 grid gap-3 md:grid-cols-3">
          <div className="md:col-span-2"><Label htmlFor="title">Title</Label><Input id="title" name="title" maxLength={120} required /></div>
          <div><Label htmlFor="severity">Severity</Label><Select id="severity" name="severity"><option value="info">Info</option><option value="warning">Warning</option><option value="critical">Critical</option></Select></div>
          <div className="md:col-span-3"><Label htmlFor="body">Message</Label><Textarea id="body" name="body" maxLength={2000} required /></div>
          <div><Label htmlFor="endsAt">Show until (optional)</Label><Input id="endsAt" name="endsAt" type="date" /></div>
          <div className="flex items-end"><Button disabled={staff.role !== 'admin'}>Publish</Button></div>
        </form>
      </Card>
      {typeof items === 'string' ? <Alert tone="error">{items}</Alert> : (
        <Table>
          <thead><tr><Th>Title</Th><Th>Severity</Th><Th>Created</Th><Th>Ends</Th><Th /></tr></thead>
          <tbody>
            {items.map((item) => (
              <tr key={item.id}>
                <Td><p className="font-semibold">{item.title}</p><p className="text-storm-muted">{item.body}</p></Td>
                <Td><Badge tone={item.severity === 'CRITICAL' ? 'danger' : item.severity === 'WARNING' ? 'warning' : 'accent'}>{item.severity}</Badge></Td>
                <Td>{new Date(item.createdAt).toLocaleString('en-US')}</Td>
                <Td>{item.endsAt ? new Date(item.endsAt).toLocaleDateString('en-US') : '—'}</Td>
                <Td><form action={deleteAnnouncement}><input type="hidden" name="id" value={item.id} /><Button variant="ghost" size="sm" disabled={staff.role !== 'admin'}>Delete</Button></form></Td>
              </tr>
            ))}
          </tbody>
        </Table>
      )}
    </AdminShell>
  );
}
