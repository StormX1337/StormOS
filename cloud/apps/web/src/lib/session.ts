import 'server-only';
import { cookies } from 'next/headers';
import type { AuthTokens } from '@storm/types';

export const ACCESS_COOKIE = 'storm_at';
export const REFRESH_COOKIE = 'storm_rt';

const base = { httpOnly: true, secure: process.env.NODE_ENV === 'production', sameSite: 'lax' as const, path: '/' };

/** Stores tokens in httpOnly cookies; they never reach browser JavaScript. */
export async function storeSession(tokens: AuthTokens): Promise<void> {
  const jar = await cookies();
  jar.set(ACCESS_COOKIE, tokens.accessToken, { ...base, maxAge: Math.max(60, tokens.expiresIn - 30) });
  jar.set(REFRESH_COOKIE, tokens.refreshToken, { ...base, maxAge: 30 * 24 * 3600 });
}

export async function clearSession(): Promise<void> {
  const jar = await cookies();
  jar.delete(ACCESS_COOKIE);
  jar.delete(REFRESH_COOKIE);
}

export async function readSession(): Promise<{ accessToken: string | null; refreshToken: string | null }> {
  const jar = await cookies();
  return { accessToken: jar.get(ACCESS_COOKIE)?.value ?? null, refreshToken: jar.get(REFRESH_COOKIE)?.value ?? null };
}
