import { Card } from '@storm/ui';
import { LoginForm } from '@/components/login-form';

export const metadata = { title: 'Sign in' };

export default async function LoginPage({ searchParams }: { searchParams: Promise<{ error?: string }> }) {
  const { error } = await searchParams;
  return (
    <div className="mx-auto mt-24 max-w-sm space-y-6">
      <p className="text-center text-xs font-bold tracking-[0.3em]"><span className="text-storm-accent">⚡</span> STORM ADMIN</p>
      <Card>
        <LoginForm initialError={error === 'no-access' ? 'This account does not have Storm Admin access.' : undefined} />
      </Card>
    </div>
  );
}
