import { cva, type VariantProps } from 'class-variance-authority';
import type { HTMLAttributes } from 'react';
import { cn } from './cn';

const badgeVariants = cva('inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-semibold uppercase tracking-wider', {
  variants: {
    tone: {
      accent: 'bg-storm-accent/15 text-storm-accent',
      success: 'bg-storm-success/15 text-storm-success',
      warning: 'bg-storm-warning/15 text-storm-warning',
      danger: 'bg-storm-danger/15 text-storm-danger',
      neutral: 'bg-storm-surface-2 text-storm-muted',
    },
  },
  defaultVariants: { tone: 'neutral' },
});

export function Badge({ className, tone, ...props }: HTMLAttributes<HTMLSpanElement> & VariantProps<typeof badgeVariants>) {
  return <span className={cn(badgeVariants({ tone }), className)} {...props} />;
}
