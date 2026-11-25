export const config = {
  apiBase: (import.meta.env.VITE_API_BASE_URL as string | undefined)?.replace(/\/$/, '') ?? 'http://localhost:5000',
  supabaseUrl: import.meta.env.VITE_SUPABASE_URL as string | undefined,
  supabaseAnonKey: import.meta.env.VITE_SUPABASE_ANON_KEY as string | undefined,
  authMode: ((import.meta.env.VITE_AUTH_MODE as string | undefined) ?? 'supabase') as 'supabase' | 'mock',
  timezone: 'Asia/Karachi',
};
export const isMock = config.authMode === 'mock';
