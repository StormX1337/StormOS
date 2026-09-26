import Link from 'next/link';
import type { ReactNode } from 'react';
import { Alert, Button } from '@storm/ui';
import { logout } from '@/app/actions';
import type { Staff } from '@/lib/guard';

const nav = [
  { href: '/', label: 'Overview' },
  { href: '/users', label: 'Users' },
  { href: '/releases', label: 'Releases' },
  { href: '/announcements', label: 'Announcements' },
  { href: '/profiles', label: 'Game profiles' },
  { href: '/audit', label: 'Audit log' },
];

export function AdminShell({ staff, title, children, notice }: { staff: Staff; title: string; children: ReactNode; notice?: { ok?: string; error?: string } }) {
  return (
    <div className="flex min-h-screen">
      <aside className="w-60 shrink-0 border-r border-storm-border bg-storm-surface p-6">
        <p className="text-xs font-bold tracking-[0.3em]"><span className="text-storm-accent">⚡</span> STORM ADMIN</p>
        <nav className="mt-8 flex flex-col gap-1 text-sm">
          {nav.map((item) => (
            <Link key={item.href} href={item.href} className="rounded-md px-3 py-2 text-storm-muted hover:bg-storm-surface-2 hover:text-storm-text">{item.label}</Link>
          ))}
        </nav>
        <div className="mt-10 space-y-2 text-xs text-storm-muted">
          <p className="break-all">{staff.email}</p>
          <p className="uppercase tracking-wider">{staff.role}</p>
          <form action={logout}><Button variant="outline" size="sm">Sign out</Button></form>
        </div>
      </aside>
      <main className="flex-1 space-y-6 p-10">
        <h1 className="text-2xl font-semibold">{title}</h1>
        {notice?.error ? <Alert tone="error">{notice.error}</Alert> : null}
        {notice?.ok ? <Alert tone="success">{notice.ok}</Alert> : null}
        {children}
      </main>
    </div>
  );
}
