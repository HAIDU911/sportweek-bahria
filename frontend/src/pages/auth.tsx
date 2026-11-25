import { useEffect, useState } from 'react';
import { Link, Navigate, useLocation, useNavigate } from 'react-router-dom';
import { Activity, MailWarning, ShieldAlert } from 'lucide-react';
import { Btn, EmptyState, SkeletonList } from '../components/ui';
import { homeFor, useAuth } from '../lib/auth';
import { isMock } from '../lib/config';

export function LoginPage({ register }: { register?: boolean }) {
  const { status, user, message, signInWithGoogle, mockSignIn, configured, sessionExpired, dismissExpired } = useAuth();
  const loc = useLocation(); const [busy, setBusy] = useState(false);
  const from = (loc.state as { from?: string } | null)?.from;
  if (status === 'authed' && user) return <Navigate to={from ?? homeFor(user.role)} replace />;
  if (status === 'rejected') return <Navigate to="/verify-email" replace />;
  return (
    <div className="login">
      <div className="art">
        <Link to="/" className="brand" style={{ color: '#fff' }}><i style={{ background: 'var(--lime)' }}><Activity size={20} color="#111" strokeWidth={2.5} /></i>SportsWeek / Bahria</Link>
        <div><h1>Play.<br />Score.<br />Repeat.</h1><p style={{ color: '#bdb9a8', marginTop: 20, maxWidth: 380 }}>Captains register teams. Operators run the matches. Everyone follows the scoreboard live.</p></div>
        <span className="eyebrow" style={{ color: '#8a8678' }}>Every result stays on record</span>
      </div>
      <div className="panel"><div className="box stack" style={{ gap: 20 }}>
        <div><div className="eyebrow">{register ? 'Create your account' : 'Welcome back'}</div><h2 style={{ marginTop: 10 }}>{register ? 'Register' : 'Sign in'}</h2></div>
        {sessionExpired && <div className="banner warn" role="alert"><ShieldAlert size={18} aria-hidden /><div>Your session expired. Please sign in again. <button className="btn sm ghost" style={{ marginLeft: 8 }} onClick={dismissExpired}>Dismiss</button></div></div>}
        {message && <div className="banner warn" role="alert"><ShieldAlert size={18} aria-hidden /><div>{message}</div></div>}
        {!configured && <div className="banner warn"><ShieldAlert size={18} aria-hidden /><div>Google sign-in is not configured for this build. Set <code>VITE_SUPABASE_URL</code> and <code>VITE_SUPABASE_ANON_KEY</code>.</div></div>}
        <Btn block loading={busy} disabled={!configured || isMock} onClick={() => { setBusy(true); void signInWithGoogle().finally(() => setBusy(false)); }}>Continue with Google</Btn>
        <p className="muted" style={{ fontSize: 14 }}>We use Google to confirm you’re a real person. Gmail accounts are accepted. No password to remember.</p>
        {isMock && <div className="card stack" style={{ gap: 10 }}><div className="eyebrow">Dev mock — pick a role</div>
          {(['captain', 'scorer', 'admin'] as const).map((r) => <Btn key={r} variant="ghost" block onClick={() => mockSignIn(r)}>Enter as {r}</Btn>)}</div>}
        <p style={{ fontSize: 14 }}>{register ? <>Already registered? <Link to="/login">Sign in</Link></> : <>New captain? <Link to="/register">Register</Link></>}</p>
      </div></div>
    </div>
  );
}

/** Supabase handles the PKCE code exchange; we just wait for the session, then route by server-assigned role. */
export function AuthCallbackPage() {
  const { status, user, message } = useAuth(); const nav = useNavigate();
  useEffect(() => {
    if (status === 'authed' && user) nav(homeFor(user.role), { replace: true });
    if (status === 'rejected') nav('/verify-email', { replace: true });
  }, [status, user, nav]);
  if (status === 'anon') return (<div className="container" style={{ padding: '64px 24px' }}><EmptyState title="Sign-in did not complete" action={<Link className="btn" to="/login">Try again</Link>}>{message ?? 'We could not confirm your Google account.'}</EmptyState></div>);
  return <div className="container" style={{ padding: '64px 24px', maxWidth: 520 }}><h3 style={{ marginBottom: 16 }}>Confirming your Google account…</h3><SkeletonList rows={2} h={48} /></div>;
}

export function VerifyEmailPage() {
  const { status, user, message, signOut } = useAuth();
  const unverified = status === 'authed' && user && !user.emailVerified;
  return (
    <div className="container" style={{ padding: '64px 24px', maxWidth: 640 }}>
      <div className="state error"><MailWarning size={28} aria-hidden />
        <h3>{unverified ? 'Verify your Google email' : 'Sign-in not permitted'}</h3>
        <p>{unverified ? 'Your Google account has no verified email address, so you can browse but cannot register a team yet. Verify the email with Google, then sign in again.' : message ?? 'This account is not allowed to use SportsWeek. Use a Gmail account that Google has verified.'}</p>
        <div className="row" style={{ justifyContent: 'center' }}><Btn onClick={() => void signOut()}>Use a different account</Btn><Link className="btn ghost" to="/">Back to scoreboard</Link></div>
      </div>
    </div>
  );
}

export const ForbiddenPage = () => <div className="container" style={{ padding: '64px 24px' }}><EmptyState title="403 — Not your area" action={<Link className="btn" to="/">Back to scoreboard</Link>}>Your role does not have access to this page.</EmptyState></div>;
export const NotFoundPage = () => <div className="container" style={{ padding: '64px 24px' }}><EmptyState title="404 — Off the pitch" action={<Link className="btn" to="/">Back to scoreboard</Link>}>That page does not exist.</EmptyState></div>;
