import 'server-only';
import type { ApiErrorBody } from '@storm/types';

const API_URL = (process.env.STORM_API_URL ?? 'http://localhost:4000').replace(/\/$/, '');

export class ApiRequestError extends Error {
  constructor(readonly status: number, message: string, readonly code: string) {
    super(message);
  }
}

/** Server-side call to the STORM Cloud API. Errors carry the API's user-safe message. */
export async function api<T>(path: string, options: { method?: string; token?: string | null; body?: unknown } = {}): Promise<T> {
  let response: Response;
  try {
    response = await fetch(`${API_URL}/api/v1/${path}`, {
      method: options.method ?? 'GET',
      headers: {
        ...(options.body !== undefined ? { 'Content-Type': 'application/json' } : {}),
        ...(options.token ? { Authorization: `Bearer ${options.token}` } : {}),
      },
      body: options.body !== undefined ? JSON.stringify(options.body) : undefined,
      cache: 'no-store',
      signal: AbortSignal.timeout(15_000),
    });
  } catch {
    throw new ApiRequestError(503, 'STORM Cloud is not reachable right now. Please try again shortly.', 'service_unavailable');
  }

  if (response.status === 204) return undefined as T;
  const payload = (await response.json().catch(() => null)) as unknown;
  if (!response.ok) {
    const error = payload as Partial<ApiErrorBody> | null;
    throw new ApiRequestError(response.status, error?.message ?? 'The request failed.', error?.code ?? 'internal');
  }

  return payload as T;
}
