import Link from 'next/link';
import { Card } from '@storm/ui';
import { AuthForm } from '@/components/auth-form';
import { register } from '../actions';

export const metadata = { title: 'Create account' };

export default function RegisterPage() {
  return (
    <div className="mx-auto max-w-md space-y-6">
      <h1 className="text-3xl font-semibold">Create your STORM account</h1>
      <Card>
        <AuthForm action={register} mode="register" />
      </Card>
      <p className="text-sm text-storm-muted">
        STORM OS works fully without an account. An account adds license management, cloud sync and AI analysis. Already registered?{' '}
        <Link href="/login" className="text-storm-accent hover:underline">Sign in</Link>
      </p>
    </div>
  );
}
