import type { Metadata } from 'next';
import type { ReactNode } from 'react';
import { SiteHeader } from '@/components/site-header';
import './globals.css';

export const metadata: Metadata = {
  title: { default: 'STORM OS — Windows gaming performance command center', template: '%s · STORM OS' },
  description: 'Real telemetry, explainable and reversible optimizations, benchmarks and an anti-cheat-safe overlay for Windows 11 gaming PCs.',
};

export default function RootLayout({ children }: { children: ReactNode }) {
  return (
    <html lang="en">
      <body className="min-h-screen font-sans">
        <SiteHeader />
        <main className="mx-auto max-w-6xl px-6 py-12">{children}</main>
        <footer className="border-t border-storm-border/70 py-8 text-center text-xs text-storm-muted">
          STORM OS · Measured, not guessed. Every change reversible.
        </footer>
      </body>
    </html>
  );
}
