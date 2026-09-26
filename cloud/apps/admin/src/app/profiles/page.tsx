import { Alert, Badge, Button, Table, Td, Th } from '@storm/ui';
import { setProfilePublished } from '@/app/actions';
import { AdminShell } from '@/components/admin-shell';
import { load, requireStaff } from '@/lib/guard';

export const dynamic = 'force-dynamic';

interface Profile {
  id: string;
  name: string;
  version: string;
  published: boolean;
  updatedAt: string;
}

export default async function Profiles({ searchParams }: { searchParams: Promise<{ ok?: string; error?: string }> }) {
  const notice = await searchParams;
  const { token, staff } = await requireStaff('/profiles');
  const profiles = await load<Profile[]>('admin/profiles', token);
  return (
    <AdminShell staff={staff} title="Game profiles" notice={notice}>
      <p className="text-sm text-storm-muted">Profiles are validated JSON documents (see docs/PROFILES.md). Published profiles are downloaded by STORM OS clients.</p>
      {typeof profiles === 'string' ? <Alert tone="error">{profiles}</Alert> : (
        <Table>
          <thead><tr><Th>Game</Th><Th>Id</Th><Th>Version</Th><Th>Updated</Th><Th>Status</Th><Th /></tr></thead>
          <tbody>
            {profiles.map((profile) => (
              <tr key={profile.id}>
                <Td>{profile.name}</Td>
                <Td className="font-mono text-xs">{profile.id}</Td>
                <Td>{profile.version}</Td>
                <Td>{new Date(profile.updatedAt).toLocaleString('en-US')}</Td>
                <Td>{profile.published ? <Badge tone="success">Published</Badge> : <Badge>Draft</Badge>}</Td>
                <Td>
                  <form action={setProfilePublished}>
                    <input type="hidden" name="id" value={profile.id} />
                    <input type="hidden" name="published" value={String(!profile.published)} />
                    <Button variant="ghost" size="sm" disabled={staff.role !== 'admin'}>{profile.published ? 'Unpublish' : 'Publish'}</Button>
                  </form>
                </Td>
              </tr>
            ))}
          </tbody>
        </Table>
      )}
    </AdminShell>
  );
}
