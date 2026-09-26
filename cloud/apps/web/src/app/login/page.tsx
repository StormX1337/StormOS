import Link from 'next/link';
import { Card } from '@storm/ui';
import { AuthForm } from '@/components/auth-form';
import { login } from '../actions';

export const metadata = { title: 'Sign in' };

export default function LoginPage() {
  return (
    <div className="mx-auto max-w-md space-y-6">
      <h1 className="text-3xl font-semibold">Sign in</h1>
      <Card>
        <AuthForm action={login} mode="login" />
      </Card>
      <p className="text-sm text-storm-muted">
        No account yet? <Link href="/register" className="text-storm-accent hover:underline">Create one</Link>
      </p>
    </div>
  );
}
