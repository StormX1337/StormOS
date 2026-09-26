import { Alert, Button, Card, CardTitle, Input, Label, Select, Table, Td, Textarea, Th } from '@storm/ui';
import { createRelease, deleteRelease } from '@/app/actions';
import { AdminShell } from '@/components/admin-shell';
import { load, requireStaff } from '@/lib/guard';

export const dynamic = 'force-dynamic';

interface Release {
  id: string;
  version: string;
  channel: string;
  arch: string;
  url: string;
  sha256: string;
  sizeBytes: number;
  publishedAt: string;
}

export default async function Releases({ searchParams }: { searchParams: Promise<{ ok?: string; error?: string }> }) {
  const notice = await searchParams;
  const { token, staff } = await requireStaff('/releases');
  const releases = await load<Release[]>('admin/releases', token);
  return (
    <AdminShell staff={staff} title="Releases" notice={notice}>
      <Card>
        <CardTitle>Publish a release</CardTitle>
        <p className="mt-2 text-sm text-storm-muted">The desktop app downloads over HTTPS and refuses installers whose SHA-256 or Authenticode signer does not match.</p>
        <form action={createRelease} className="mt-4 grid gap-3 md:grid-cols-3">
          <div><Label htmlFor="version">Version</Label><Input id="version" name="version" placeholder="1.2.0" required /></div>
          <div><Label htmlFor="channel">Channel</Label><Select id="channel" name="channel"><option value="stable">Stable</option><option value="beta">Beta</option></Select></div>
          <div><Label htmlFor="arch">Architecture</Label><Select id="arch" name="arch"><option value="x64">x64</option><option value="arm64">ARM64</option></Select></div>
          <div className="md:col-span-2"><Label htmlFor="url">Installer URL (https)</Label><Input id="url" name="url" type="url" required /></div>
          <div><Label htmlFor="sizeBytes">Size (bytes)</Label><Input id="sizeBytes" name="sizeBytes" type="number" min={1} required /></div>
          <div className="md:col-span-3"><Label htmlFor="sha256">SHA-256</Label><Input id="sha256" name="sha256" pattern="[A-Fa-f0-9]{64}" required className="font-mono" /></div>
          <div className="md:col-span-3"><Label htmlFor="notes">Release notes</Label><Textarea id="notes" name="notes" maxLength={4000} /></div>
          <div><Button disabled={staff.role !== 'admin'}>Publish</Button></div>
        </form>
      </Card>
      {typeof releases === 'string' ? <Alert tone="error">{releases}</Alert> : (
        <Table>
          <thead><tr><Th>Version</Th><Th>Channel</Th><Th>Arch</Th><Th>Published</Th><Th>SHA-256</Th><Th /></tr></thead>
          <tbody>
            {releases.map((release) => (
              <tr key={release.id}>
                <Td>{release.version}</Td>
                <Td>{release.channel}</Td>
                <Td>{release.arch}</Td>
                <Td>{new Date(release.publishedAt).toLocaleString('en-US')}</Td>
                <Td className="font-mono text-xs">{release.sha256.slice(0, 16)}…</Td>
                <Td>
                  <form action={deleteRelease}><input type="hidden" name="id" value={release.id} /><Button variant="ghost" size="sm" disabled={staff.role !== 'admin'}>Delete</Button></form>
                </Td>
              </tr>
            ))}
          </tbody>
        </Table>
      )}
    </AdminShell>
  );
}
