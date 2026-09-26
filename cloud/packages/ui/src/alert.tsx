import type { HTMLAttributes } from 'react';
import { cn } from './cn';

export function Alert({ className, tone = 'info', ...props }: HTMLAttributes<HTMLDivElement> & { tone?: 'info' | 'error' | 'success' }) {
  const tones = {
    info: 'border-storm-accent/40 bg-storm-accent/10 text-storm-text',
    error: 'border-storm-danger/40 bg-storm-danger/10 text-storm-text',
    success: 'border-storm-success/40 bg-storm-success/10 text-storm-text',
  };
  return <div role={tone === 'error' ? 'alert' : 'status'} className={cn('rounded-md border px-4 py-3 text-sm', tones[tone], className)} {...props} />;
}
