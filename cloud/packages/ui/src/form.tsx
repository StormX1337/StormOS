import type { InputHTMLAttributes, LabelHTMLAttributes, SelectHTMLAttributes, TextareaHTMLAttributes } from 'react';
import { cn } from './cn';

const field = 'w-full rounded-md border border-storm-border bg-storm-bg px-3 py-2 text-sm text-storm-text placeholder:text-storm-muted/70 focus:outline-none focus:ring-2 focus:ring-storm-accent/50';

export function Input({ className, ...props }: InputHTMLAttributes<HTMLInputElement>) {
  return <input className={cn(field, 'h-10', className)} {...props} />;
}

export function Textarea({ className, ...props }: TextareaHTMLAttributes<HTMLTextAreaElement>) {
  return <textarea className={cn(field, 'min-h-24', className)} {...props} />;
}

export function Select({ className, ...props }: SelectHTMLAttributes<HTMLSelectElement>) {
  return <select className={cn(field, 'h-10', className)} {...props} />;
}

export function Label({ className, ...props }: LabelHTMLAttributes<HTMLLabelElement>) {
  return <label className={cn('mb-1 block text-xs font-semibold uppercase tracking-wider text-storm-muted', className)} {...props} />;
}
