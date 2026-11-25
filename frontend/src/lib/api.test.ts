import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiError, api, buildUrl, configureApi, fieldError, newIdempotencyKey } from './api';

const json = (status: number, body: unknown, headers: Record<string, string> = {}) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json', ...headers } });

describe('api client', () => {
  const unauthorized = vi.fn();
  beforeEach(() => { unauthorized.mockReset(); configureApi({ getToken: async () => 'tok-123', onUnauthorized: unauthorized }); });
  afterEach(() => vi.restoreAllMocks());

  it('builds URLs and skips empty params', () => {
    const u = new URL(buildUrl('/api/x', { a: 1, b: '', c: undefined, d: null, e: false, f: 'hi there' }));
    expect(u.searchParams.get('a')).toBe('1'); expect(u.searchParams.has('b')).toBe(false); expect(u.searchParams.has('c')).toBe(false);
    expect(u.searchParams.has('d')).toBe(false); expect(u.searchParams.get('e')).toBe('false'); expect(u.searchParams.get('f')).toBe('hi there');
  });

  it('sends the bearer token on authenticated calls only', async () => {
    const f = vi.spyOn(globalThis, 'fetch').mockImplementation(async () => json(200, { ok: true }));
    await api.get('/api/me'); await api.publicGet('/api/public/sports');
    expect((f.mock.calls[0][1]!.headers as Record<string, string>).Authorization).toBe('Bearer tok-123');
    expect((f.mock.calls[1][1]!.headers as Record<string, string>).Authorization).toBeUndefined();
  });

  it('maps the backend error contract onto ApiError, including field errors and correlation id', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(json(400, { code: 'validation_failed', message: 'bad', fieldErrors: { 'players[0].enrollmentNo': ['Invalid'] }, correlationId: 'abc' }));
    const e = await api.post('/api/x', {}).catch((x) => x) as ApiError;
    expect(e).toBeInstanceOf(ApiError); expect(e.status).toBe(400); expect(e.code).toBe('validation_failed'); expect(e.correlationId).toBe('abc');
    expect(fieldError(e, 'players[0].enrollmentNo')).toBe('Invalid'); expect(fieldError(e, 'nope')).toBeUndefined(); expect(fieldError(new Error('x'), 'a')).toBeUndefined();
  });

  it('signals session expiry on 401 and rate-limit info on 429', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce(json(401, {})).mockResolvedValueOnce(json(429, { code: 'rate_limited', message: 'Slow' }, { 'Retry-After': '12' }));
    const a = await api.get('/api/me').catch((x) => x) as ApiError;
    expect(a.status).toBe(401); expect(unauthorized).toHaveBeenCalledOnce(); expect(a.message).toMatch(/expired/i);
    const b = await api.get('/api/me').catch((x) => x) as ApiError; expect(b.retryAfter).toBe(12);
  });

  it('treats network failures and 503 as "unavailable" and never leaks raw errors', async () => {
    vi.spyOn(globalThis, 'fetch').mockRejectedValueOnce(new TypeError('Failed to fetch')).mockResolvedValueOnce(new Response('<html>boom</html>', { status: 503 }));
    const n = await api.get('/x').catch((x) => x) as ApiError; expect(n.isNetwork).toBe(true); expect(n.isUnavailable).toBe(true); expect(n.message).not.toMatch(/Failed to fetch/);
    const s = await api.get('/x').catch((x) => x) as ApiError; expect(s.isUnavailable).toBe(true); expect(s.message).not.toMatch(/boom/);
  });

  it('forwards the Idempotency-Key header and parses empty responses', async () => {
    const f = vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(null, { status: 204 }));
    expect(await api.post('/api/x', { a: 1 }, { 'Idempotency-Key': 'k1' })).toBeUndefined();
    expect((f.mock.calls[0][1]!.headers as Record<string, string>)['Idempotency-Key']).toBe('k1');
  });

  it('reads the filename from Content-Disposition for CSV downloads', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response('a,b', { status: 200, headers: { 'Content-Disposition': 'attachment; filename="users-2026.csv"' } }));
    const d = await api.download('/api/admin/exports/users', {}); expect(d.filename).toBe('users-2026.csv'); expect(await d.blob.text()).toBe('a,b');
  });

  it('generates unique 32-char idempotency keys', () => {
    const k = new Set(Array.from({ length: 50 }, newIdempotencyKey)); expect(k.size).toBe(50); expect([...k][0]).toMatch(/^[a-f0-9]{32}$/);
  });
});
