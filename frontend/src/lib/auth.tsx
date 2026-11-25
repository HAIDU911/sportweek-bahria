import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { createClient, type SupabaseClient } from '@supabase/supabase-js';
import { api, ApiError, configureApi } from './api';
import { config, isMock } from './config';
import type { CurrentUser, Role } from './types';

type Status = 'loading' | 'anon' | 'authed' | 'rejected';
interface AuthCtx {
  status: Status; user: CurrentUser | null; message: string | null; sessionExpired: boolean;
  signInWithGoogle: () => Promise<void>; mockSignIn: (role: 'captain' | 'scorer' | 'admin') => void; signOut: () => Promise<void>;
  dismissExpired: () => void; configured: boolean;
}
const Ctx = createContext<AuthCtx | null>(null);
const MOCK_KEY = 'sw_mock_token';

let supabase: SupabaseClient | null = null;
function client(): SupabaseClient | null {
  if (supabase) return supabase;
  if (!config.supabaseUrl || !config.supabaseAnonKey) return null;
  supabase = createClient(config.supabaseUrl, config.supabaseAnonKey, { auth: { flowType: 'pkce', detectSessionInUrl: true, persistSession: true, autoRefreshToken: true } });
  return supabase;
}

export function homeFor(role: Role): string {
  return role === 'Admin' ? '/workspace/dashboard' : role === 'Scorer' ? '/ops' : role === 'Captain' ? '/captain' : '/'; // Stakeholder: public scoreboard until a stakeholder dashboard exists
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [status, setStatus] = useState<Status>('loading');
  const [user, setUser] = useState<CurrentUser | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [sessionExpired, setExpired] = useState(false);
  const tokenRef = useRef<string | null>(null);

  const loadProfile = useCallback(async () => {
    try {
      const me = await api.get<CurrentUser>('/api/me');
      setUser(me); setStatus('authed'); setMessage(null);
    } catch (e) {
      if (e instanceof ApiError && e.status === 403) { setUser(null); setStatus('rejected'); setMessage(e.message); }
      else if (e instanceof ApiError && e.status === 401) { setUser(null); setStatus('anon'); }
      else { setUser(null); setStatus('anon'); setMessage(e instanceof Error ? e.message : 'Sign-in failed.'); }
    }
  }, []);

  useEffect(() => {
    configureApi({
      getToken: async () => tokenRef.current,
      onUnauthorized: () => { if (tokenRef.current) { tokenRef.current = null; setUser(null); setStatus('anon'); setExpired(true); if (isMock) localStorage.removeItem(MOCK_KEY); else void client()?.auth.signOut(); } },
    });
    if (isMock) {
      tokenRef.current = localStorage.getItem(MOCK_KEY);
      if (tokenRef.current) void loadProfile(); else setStatus('anon');
      return;
    }
    const sb = client();
    if (!sb) { setStatus('anon'); return; }
    void sb.auth.getSession().then(({ data }) => { tokenRef.current = data.session?.access_token ?? null; if (tokenRef.current) void loadProfile(); else setStatus('anon'); });
    const { data: sub } = sb.auth.onAuthStateChange((_evt, session) => {
      tokenRef.current = session?.access_token ?? null;
      if (tokenRef.current) void loadProfile(); else { setUser(null); setStatus('anon'); }
    });
    return () => sub.subscription.unsubscribe();
  }, [loadProfile]);

  const value = useMemo<AuthCtx>(() => ({
    status, user, message, sessionExpired, configured: isMock || client() !== null,
    dismissExpired: () => setExpired(false),
    signInWithGoogle: async () => {
      const sb = client();
      if (!sb) { setMessage('Google sign-in is not configured. Set VITE_SUPABASE_URL and VITE_SUPABASE_ANON_KEY.'); return; }
      const { error } = await sb.auth.signInWithOAuth({ provider: 'google', options: { redirectTo: `${window.location.origin}/auth/callback`, queryParams: { prompt: 'select_account' } } });
      if (error) setMessage(error.message);
    },
    mockSignIn: (role) => { localStorage.setItem(MOCK_KEY, `mock-${role}`); tokenRef.current = `mock-${role}`; setStatus('loading'); void loadProfile(); },
    signOut: async () => {
      tokenRef.current = null; setUser(null); setStatus('anon');
      if (isMock) localStorage.removeItem(MOCK_KEY); else await client()?.auth.signOut();
    },
  }), [status, user, message, sessionExpired, loadProfile]);

  return <Ctx.Provider value={value}>{children}</Ctx.Provider>;
}

export function useAuth(): AuthCtx {
  const c = useContext(Ctx);
  if (!c) throw new Error('useAuth must be used inside <AuthProvider>');
  return c;
}
