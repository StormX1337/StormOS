import { cva, type VariantProps } from 'class-variance-authority';
import type { ButtonHTMLAttributes } from 'react';
import { cn } from './cn';

export const buttonVariants = cva(
  'inline-flex items-center justify-center gap-2 rounded-md text-sm font-semibold tracking-wide transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-storm-accent/60 disabled:pointer-events-none disabled:opacity-50',
  {
    variants: {
      variant: {
        default: 'bg-storm-accent text-storm-bg hover:bg-storm-accent/90',
        outline: 'border border-storm-border bg-transparent text-storm-text hover:bg-storm-surface-2',
        ghost: 'text-storm-muted hover:bg-storm-surface-2 hover:text-storm-text',
        destructive: 'bg-storm-danger text-white hover:bg-storm-danger/90',
      },
      size: { sm: 'h-8 px-3', md: 'h-10 px-4', lg: 'h-12 px-6 text-base' },
    },
    defaultVariants: { variant: 'default', size: 'md' },
  },
);

export type ButtonProps = ButtonHTMLAttributes<HTMLButtonElement> & VariantProps<typeof buttonVariants>;

export function Button({ className, variant, size, ...props }: ButtonProps) {
  return <button className={cn(buttonVariants({ variant, size }), className)} {...props} />;
}
