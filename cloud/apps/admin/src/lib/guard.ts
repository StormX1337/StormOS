import 'server-only';
import { redirect } from 'next/navigation';
import { api, ApiRequestError } from './api';
import { readSession } from './session';

export interface Staff {
  id: string;
  email: string;
  role: 'admin' | 'support' | 'user';
}

/** Loads the signed-in staff member or redirects to sign-in. Authorization is enforced again by the API. */
export async function requireStaff(returnTo: string): Promise<{ token: string; staff: Staff }> {
  const { accessToken, refreshToken } = await readSession();
  if (!accessToken) {
    redirect(refreshToken ? `/api/session/refresh?next=${encodeURIComponent(returnTo)}` : '/login');
  }

  try {
    const staff = await api<Staff>('auth/me', { token: accessToken });
    if (staff.role === 'user') redirect('/login?error=no-access');
    return { token: accessToken, staff };
  } catch (error) {
    if (error instanceof ApiRequestError && error.status === 401) {
      redirect(`/api/session/refresh?next=${encodeURIComponent(returnTo)}`);
    }

    throw error;
  }
}

/** Calls the API and maps failures to a message for the page. */
export async function load<T>(path: string, token: string): Promise<T | string> {
  try {
    return await api<T>(path, { token });
  } catch (error) {
    return error instanceof Error ? error.message : 'Request failed.';
  }
}
