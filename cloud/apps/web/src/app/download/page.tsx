import type { ReleaseInfo } from '@storm/types';
import { Alert, Card, CardTitle } from '@storm/ui';
import { api, ApiRequestError } from '@/lib/api';

export const dynamic = 'force-dynamic';
export const metadata = { title: 'Download' };

async function latest(arch: 'x64' | 'arm64'): Promise<ReleaseInfo | string> {
  try {
    return await api<ReleaseInfo>(`releases/latest?channel=stable&arch=${arch}`);
  } catch (error) {
    return error instanceof ApiRequestError ? error.message : 'Unavailable';
  }
}

export default async function DownloadPage() {
  const releases = await Promise.all([latest('x64'), latest('arm64')]);
  return (
    <div className="space-y-8">
      <div className="space-y-2">
        <h1 className="text-4xl font-semibold">Download STORM OS</h1>
        <p className="text-storm-muted">Windows 10 22H2 or Windows 11. The installer is signed; verify the SHA-256 below if you download it from a mirror.</p>
      </div>
      <div className="grid gap-4 md:grid-cols-2">
        {(['x64', 'arm64'] as const).map((arch, index) => {
          const release = releases[index]!;
          return (
            <Card key={arch}>
              <CardTitle>Windows {arch === 'x64' ? 'x64' : 'ARM64'}</CardTitle>
              {typeof release === 'string' ? (
                <Alert className="mt-4">{release}</Alert>
              ) : (
                <div className="mt-4 space-y-3 text-sm">
                  <p className="text-2xl font-semibold">Version {release.version}</p>
                  <p className="text-storm-muted">Published {new Date(release.publishedAt).toLocaleDateString('en-US', { dateStyle: 'medium' })} · {(release.sizeBytes / 1024 / 1024).toFixed(1)} MB</p>
                  <p className="break-all font-mono text-xs text-storm-muted">SHA-256 {release.sha256}</p>
                  <a href={release.url} className="inline-flex h-10 items-center rounded-md bg-storm-accent px-4 font-semibold text-storm-bg">Download installer</a>
                  {release.notes ? <p className="whitespace-pre-line text-storm-muted">{release.notes}</p> : null}
                </div>
              )}
            </Card>
          );
        })}
      </div>
    </div>
  );
}
