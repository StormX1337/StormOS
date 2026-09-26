'use server';

import { redirect } from 'next/navigation';
import type { AuthResponse } from '@storm/types';
import { api, ApiRequestError } from '@/lib/api';
import { clearSession, readSession, storeSession } from '@/lib/session';

export interface FormState {
  error?: string;
}

export async function login(_state: FormState, form: FormData): Promise<FormState> {
  try {
    const response = await api<AuthResponse>('auth/login', { method: 'POST', body: { email: String(form.get('email') ?? ''), password: String(form.get('password') ?? '') } });
    const me = await api<{ role: string }>('auth/me', { token: response.tokens.accessToken });
    if (me.role !== 'admin' && me.role !== 'support') {
      await api('auth/logout', { method: 'POST', body: { refreshToken: response.tokens.refreshToken } }).catch(() => undefined);
      return { error: 'This account does not have Storm Admin access.' };
    }

    await storeSession(response.tokens);
  } catch (error) {
    return { error: error instanceof ApiRequestError ? error.message : 'Sign-in failed.' };
  }

  redirect('/');
}

export async function logout(): Promise<void> {
  const { refreshToken } = await readSession();
  if (refreshToken) await api('auth/logout', { method: 'POST', body: { refreshToken } }).catch(() => undefined);
  await clearSession();
  redirect('/login');
}

/** Runs an authenticated mutation and returns to the page with a result message. */
async function mutate(back: string, path: string, method: string, body?: unknown): Promise<never> {
  const { accessToken } = await readSession();
  if (!accessToken) redirect('/login');
  let message: string;
  try {
    await api(path, { method, token: accessToken, body });
    message = 'ok=Saved.';
  } catch (error) {
    message = `error=${encodeURIComponent(error instanceof ApiRequestError ? error.message : 'The change failed.')}`;
  }

  redirect(`${back}${back.includes('?') ? '&' : '?'}${message}`);
}

const text = (form: FormData, name: string) => String(form.get(name) ?? '').trim();
const optionalDate = (value: string) => (value ? new Date(value).toISOString() : null);

export async function updateUser(form: FormData): Promise<void> {
  const id = text(form, 'id');
  const compTier = text(form, 'compTier');
  await mutate(`/users/${id}`, `admin/users/${id}`, 'PATCH', {
    role: text(form, 'role') || undefined,
    disabled: form.get('disabled') === 'on',
    compTier: compTier === 'none' ? null : compTier,
    compExpiresAt: optionalDate(text(form, 'compExpiresAt')),
  });
}

export async function createRelease(form: FormData): Promise<void> {
  await mutate('/releases', 'admin/releases', 'POST', {
    version: text(form, 'version'),
    channel: text(form, 'channel'),
    arch: text(form, 'arch'),
    url: text(form, 'url'),
    sha256: text(form, 'sha256'),
    sizeBytes: Number(text(form, 'sizeBytes')),
    notes: text(form, 'notes') || undefined,
  });
}

export async function deleteRelease(form: FormData): Promise<void> {
  await mutate('/releases', `admin/releases/${text(form, 'id')}`, 'DELETE');
}

export async function createAnnouncement(form: FormData): Promise<void> {
  await mutate('/announcements', 'admin/announcements', 'POST', {
    title: text(form, 'title'),
    body: text(form, 'body'),
    severity: text(form, 'severity'),
    active: true,
    endsAt: optionalDate(text(form, 'endsAt')),
  });
}

export async function deleteAnnouncement(form: FormData): Promise<void> {
  await mutate('/announcements', `admin/announcements/${text(form, 'id')}`, 'DELETE');
}

export async function setProfilePublished(form: FormData): Promise<void> {
  await mutate('/profiles', `admin/profiles/${encodeURIComponent(text(form, 'id'))}/publish`, 'PATCH', { published: form.get('published') === 'true' });
}
