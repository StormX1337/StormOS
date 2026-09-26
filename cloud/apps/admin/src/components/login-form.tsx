'use client';

import { useActionState } from 'react';
import { Alert, Button, Input, Label } from '@storm/ui';
import { login } from '@/app/actions';

export function LoginForm({ initialError }: { initialError?: string }) {
  const [state, action, pending] = useActionState(login, { error: initialError });
  return (
    <form action={action} className="space-y-4">
      {state.error ? <Alert tone="error">{state.error}</Alert> : null}
      <div>
        <Label htmlFor="email">E-mail</Label>
        <Input id="email" name="email" type="email" autoComplete="username" required />
      </div>
      <div>
        <Label htmlFor="password">Password</Label>
        <Input id="password" name="password" type="password" autoComplete="current-password" required />
      </div>
      <Button type="submit" className="w-full" disabled={pending}>{pending ? 'Signing in…' : 'Sign in'}</Button>
    </form>
  );
}
