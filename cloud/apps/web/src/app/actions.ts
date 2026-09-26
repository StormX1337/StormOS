'use server';

import { redirect } from 'next/navigation';
import type { AuthResponse } from '@storm/types';
import { api, ApiRequestError } from '@/lib/api';
import { clearSession, readSession, storeSession } from '@/lib/session';

export interface FormState {
  error?: string;
}

async function authenticate(path: 'auth/login' | 'auth/register', form: FormData): Promise<FormState> {
  const email = String(form.get('email') ?? '');
  const password = String(form.get('password') ?? '');
  try {
    const response = await api<AuthResponse>(path, { method: 'POST', body: { email, password } });
    await storeSession(response.tokens);
  } catch (error) {
    return { error: error instanceof ApiRequestError ? error.message : 'Sign-in failed. Please try again.' };
  }

  redirect('/account');
}

export async function login(_state: FormState, form: FormData): Promise<FormState> {
  return authenticate('auth/login', form);
}

export async function register(_state: FormState, form: FormData): Promise<FormState> {
  if (form.get('password') !== form.get('confirm')) {
    return { error: 'The passwords do not match.' };
  }

  return authenticate('auth/register', form);
}

export async function logout(): Promise<void> {
  const { refreshToken } = await readSession();
  if (refreshToken) {
    await api('auth/logout', { method: 'POST', body: { refreshToken } }).catch(() => undefined);
  }

  await clearSession();
  redirect('/');
}

async function requireToken(): Promise<string> {
  const { accessToken } = await readSession();
  if (!accessToken) redirect('/api/session/refresh?next=/account');
  return accessToken;
}

export async function startCheckout(form: FormData): Promise<void> {
  const token = await requireToken();
  const tier = form.get('tier') === 'ultimate' ? 'ultimate' : 'pro';
  const interval = form.get('interval') === 'year' ? 'year' : 'month';
  let url: string;
  try {
    ({ url } = await api<{ url: string }>('billing/checkout', { method: 'POST', token, body: { tier, interval } }));
  } catch (error) {
    redirect(`/pricing?error=${encodeURIComponent(error instanceof ApiRequestError ? error.message : 'Checkout is unavailable.')}`);
  }

  redirect(url);
}

export async function openBillingPortal(): Promise<void> {
  const token = await requireToken();
  let url: string;
  try {
    ({ url } = await api<{ url: string }>('billing/portal', { method: 'POST', token }));
  } catch (error) {
    redirect(`/account?error=${encodeURIComponent(error instanceof ApiRequestError ? error.message : 'Billing is unavailable.')}`);
  }

  redirect(url);
}

export async function removeDevice(form: FormData): Promise<void> {
  const token = await requireToken();
  const id = String(form.get('id') ?? '');
  if (/^[0-9a-f-]{36}$/i.test(id)) {
    await api(`devices/${id}`, { method: 'DELETE', token }).catch(() => undefined);
  }

  redirect('/account');
}
