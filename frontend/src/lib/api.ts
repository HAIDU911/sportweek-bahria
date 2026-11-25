import { config } from './config';

export class ApiError extends Error {
  constructor(
    public status: number,
    public code: string,
    message: string,
    public fieldErrors?: Record<string, string[]>,
    public correlationId?: string | null,
    public retryAfter?: number,
  ) { super(message); this.name = 'ApiError'; }
  get isNetwork() { return this.status === 0; }
  get isUnavailable() { return this.status === 503 || this.status === 0; }
}

type TokenGetter = () => Promise<string | null>;
let getToken: TokenGetter = async () => null;
let onUnauthorized: () => void = () => {};
export function configureApi(opts: { getToken: TokenGetter; onUnauthorized: () => void }) { getToken = opts.getToken; onUnauthorized = opts.onUnauthorized; }

type Params = Record<string, string | number | boolean | null | undefined>;

export function buildUrl(path: string, params?: Params): string {
  const url = new URL(config.apiBase + path);
  for (const [k, v] of Object.entries(params ?? {})) if (v !== undefined && v !== null && v !== '') url.searchParams.set(k, String(v));
  return url.toString();
}

async function request(method: 'GET' | 'POST', path: string, opts: { params?: Params; body?: unknown; headers?: Record<string, string>; auth?: boolean } = {}): Promise<Response> {
  const headers: Record<string, string> = { Accept: 'application/json', ...opts.headers };
  if (opts.body !== undefined) headers['Content-Type'] = 'application/json';
  if (opts.auth !== false) { const t = await getToken(); if (t) headers.Authorization = `Bearer ${t}`; }
  let res: Response;
  try {
    res = await fetch(buildUrl(path, opts.params), { method, headers, body: opts.body === undefined ? undefined : JSON.stringify(opts.body) });
  } catch {
    throw new ApiError(0, 'network', 'Cannot reach the server. Check your connection and try again.');
  }
  if (res.ok) return res;
  let body: { code?: string; message?: string; fieldErrors?: Record<string, string[]>; correlationId?: string } = {};
  try { body = await res.json(); } catch { /* non-JSON error */ }
  if (res.status === 401) onUnauthorized();
  const retry = Number(res.headers.get('Retry-After')) || undefined;
  throw new ApiError(res.status, body.code ?? `http_${res.status}`, body.message ?? defaultMessage(res.status), body.fieldErrors, body.correlationId ?? res.headers.get('X-Correlation-Id'), retry);
}

function defaultMessage(status: number): string {
  if (status === 401) return 'Your session has expired. Please sign in again.';
  if (status === 403) return 'You do not have access to this.';
  if (status === 404) return 'Not found.';
  if (status === 429) return 'Too many requests. Please slow down.';
  if (status === 503) return 'The service is temporarily unavailable. Results are safe — try again shortly.';
  return 'Something went wrong.';
}

export const api = {
  async get<T>(path: string, params?: Params, auth = true): Promise<T> { return (await request('GET', path, { params, auth })).json() as Promise<T>; },
  async post<T>(path: string, body?: unknown, headers?: Record<string, string>): Promise<T> {
    const res = await request('POST', path, { body: body ?? {}, headers });
    const text = await res.text();
    return (text ? JSON.parse(text) : undefined) as T;
  },
  /** CSV export: returns a Blob + the server-suggested filename. */
  async download(path: string, body: unknown): Promise<{ blob: Blob; filename: string }> {
    const res = await request('POST', path, { body });
    const cd = res.headers.get('Content-Disposition') ?? '';
    const m = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(cd);
    return { blob: await res.blob(), filename: m?.[1] ?? 'export.csv' };
  },
  publicGet<T>(path: string, params?: Params) { return api.get<T>(path, params, false); },
};

/** Maps server validation errors like `players[2].enrollmentNo` to form fields. */
export function fieldError(err: unknown, key: string): string | undefined {
  return err instanceof ApiError ? err.fieldErrors?.[key]?.[0] : undefined;
}
export function newIdempotencyKey(): string { return crypto.randomUUID().replace(/-/g, ''); }
