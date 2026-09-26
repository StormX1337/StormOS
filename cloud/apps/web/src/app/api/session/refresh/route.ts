import { NextResponse, type NextRequest } from 'next/server';
import type { AuthTokens } from '@storm/types';
import { api } from '@/lib/api';
import { clearSession, readSession, storeSession } from '@/lib/session';

/** Exchanges the refresh cookie for new tokens, then returns to a same-site page. */
export async function GET(request: NextRequest) {
  const next = request.nextUrl.searchParams.get('next');
  const target = next && next.startsWith('/') && !next.startsWith('//') ? next : '/account';
  const { refreshToken } = await readSession();
  if (refreshToken) {
    try {
      await storeSession(await api<AuthTokens>('auth/refresh', { method: 'POST', body: { refreshToken } }));
      return NextResponse.redirect(new URL(target, request.url));
    } catch {
      await clearSession();
    }
  }

  return NextResponse.redirect(new URL('/login', request.url));
}
