import type { HTMLAttributes, TdHTMLAttributes, ThHTMLAttributes } from 'react';
import { cn } from './cn';

export function Table({ className, ...props }: HTMLAttributes<HTMLTableElement>) {
  return (
    <div className="overflow-x-auto">
      <table className={cn('w-full border-collapse text-left text-sm', className)} {...props} />
    </div>
  );
}

export function Th({ className, ...props }: ThHTMLAttributes<HTMLTableCellElement>) {
  return <th className={cn('border-b border-storm-border px-3 py-2 text-xs font-semibold uppercase tracking-wider text-storm-muted', className)} {...props} />;
}

export function Td({ className, ...props }: TdHTMLAttributes<HTMLTableCellElement>) {
  return <td className={cn('border-b border-storm-border/60 px-3 py-2 align-top text-storm-text', className)} {...props} />;
}
