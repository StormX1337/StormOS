import type { HTMLAttributes } from 'react';
import { cn } from './cn';

export function Card({ className, ...props }: HTMLAttributes<HTMLDivElement>) {
  return <div className={cn('rounded-xl border border-storm-border bg-storm-surface p-6 shadow-sm', className)} {...props} />;
}

export function CardTitle({ className, ...props }: HTMLAttributes<HTMLHeadingElement>) {
  return <h3 className={cn('text-xs font-semibold uppercase tracking-[0.18em] text-storm-muted', className)} {...props} />;
}

export function CardValue({ className, ...props }: HTMLAttributes<HTMLParagraphElement>) {
  return <p className={cn('mt-2 text-3xl font-semibold text-storm-text', className)} {...props} />;
}
