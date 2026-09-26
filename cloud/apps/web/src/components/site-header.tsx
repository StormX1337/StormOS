import Link from 'next/link';

const links = [
  { href: '/', label: 'Product' },
  { href: '/pricing', label: 'Pricing' },
  { href: '/download', label: 'Download' },
  { href: '/account', label: 'Account' },
];

export function SiteHeader() {
  return (
    <header className="border-b border-storm-border/70 bg-storm-bg/80 backdrop-blur">
      <div className="mx-auto flex h-16 max-w-6xl items-center justify-between px-6">
        <Link href="/" className="flex items-center gap-2 text-sm font-bold tracking-[0.3em] text-storm-text">
          <span aria-hidden className="text-storm-accent">⚡</span> STORM OS
        </Link>
        <nav className="flex gap-6 text-sm text-storm-muted">
          {links.map((link) => (
            <Link key={link.href} href={link.href} className="hover:text-storm-text">
              {link.label}
            </Link>
          ))}
        </nav>
      </div>
    </header>
  );
}
