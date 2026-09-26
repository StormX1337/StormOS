'use client';

import { useActionState } from 'react';
import { Alert, Button, Input, Label } from '@storm/ui';
import type { FormState } from '@/app/actions';

export function AuthForm({ action, mode }: { action: (state: FormState, form: FormData) => Promise<FormState>; mode: 'login' | 'register' }) {
  const [state, formAction, pending] = useActionState(action, {});
  return (
    <form action={formAction} className="space-y-4">
      {state.error ? <Alert tone="error">{state.error}</Alert> : null}
      <div>
        <Label htmlFor="email">E-mail</Label>
        <Input id="email" name="email" type="email" autoComplete="email" required maxLength={254} />
      </div>
      <div>
        <Label htmlFor="password">Password</Label>
        <Input id="password" name="password" type="password" autoComplete={mode === 'login' ? 'current-password' : 'new-password'} required minLength={mode === 'register' ? 10 : 1} maxLength={128} />
      </div>
      {mode === 'register' ? (
        <div>
          <Label htmlFor="confirm">Confirm password</Label>
          <Input id="confirm" name="confirm" type="password" autoComplete="new-password" required minLength={10} maxLength={128} />
          <p className="mt-2 text-xs text-storm-muted">At least 10 characters. A long passphrase is best.</p>
        </div>
      ) : null}
      <Button type="submit" className="w-full" disabled={pending}>
        {pending ? 'Please wait…' : mode === 'login' ? 'Sign in' : 'Create account'}
      </Button>
    </form>
  );
}
