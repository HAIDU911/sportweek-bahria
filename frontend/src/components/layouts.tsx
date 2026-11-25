import { useEffect, useState, type ReactNode } from 'react';
import { Link, NavLink, Navigate, Outlet, useLocation, useNavigate } from 'react-router-dom';
import { Activity, Bell, Gauge, LayoutGrid, Layers, ListChecks, LogOut, Menu, ShieldCheck, Trophy, Users, UserSquare2, Building2, Download, CalendarDays, X } from 'lucide-react';
import { homeFor, useAuth } from '../lib/auth';
import { isMock } from '../lib/config';
import { usePublicHome, useNotifications } from '../lib/hooks';
import type { Role } from '../lib/types';
import { Btn, cx, Dialog, SkeletonList } from './ui';

const Logo = () => <i aria-hidden><Activity size={20} color="#C6F432" strokeWidth={2.5} /></i>;

export function PublicLayout() {
  const { user, status, signOut } = useAuth();
  const nav = useNavigate(); const loc = useLocation();
  const [open, setOpen] = useState(false);
  const home = usePublicHome();
  useEffect(() => setOpen(false), [loc.pathname]);
  const live = home.data?.live ?? [];
  const ticker = [...live.map((f) => `${f.sportName}: ${f.homeTeam} ${f.homeScore ?? 0}–${f.awayScore ?? 0} ${f.awayTeam}${f.liveClock ? ` (${f.liveClock})` : ''}`),
    ...(home.data?.today ?? []).filter((f) => f.status !== 'Live' && f.status !== 'Completed').slice(0, 4).map((f) => `Next: ${f.sportName} — ${f.homeTeam ?? 'TBD'} v ${f.awayTeam ?? 'TBD'}`)];
  return (
    <>
      <a className="skip" href="#main">Skip to content</a>
      {isMock && <div className="dev-banner">Dev mock mode — fake data, no real authentication</div>}
      <header className="nav"><div className="container nav-in">
        <Link to="/" className="brand"><Logo />SportsWeek / Bahria</Link>
        <nav className={cx('nav-links', open && 'open')} aria-label="Primary">
          {[['/sports', 'Sports'], ['/tournaments', 'Tournaments'], ['/fixtures', 'Fixtures'], ['/leaderboards', 'Leaderboards'], ['/teams', 'Teams']].map(([to, l]) => <NavLink key={to} to={to}>{l}</NavLink>)}
          <Link className="m-only" to={user ? homeFor(user.role) : '/login'}>{user ? 'My workspace' : 'Sign in'}</Link>
        </nav>
        <div className="row nav-actions" style={{ marginLeft: 'auto' }}>
          {live.length > 0 && <Link to="/fixtures?status=Live" className="btn lime" style={{ boxShadow: 'var(--shadow-sm)' }}><span className="dot" style={{ animation: 'pulse 1.2s infinite' }} />Live now ({live.length})</Link>}
          {status === 'authed' && user ? (<><Link className="btn ghost" to={homeFor(user.role)}>My workspace</Link><Btn variant="ghost" onClick={() => void signOut().then(() => nav('/'))} aria-label="Sign out"><LogOut size={14} /></Btn></>)
            : <Link className="btn" to="/login">Continue with Google</Link>}
        </div>
        <Btn className="menu-btn" variant="ghost" aria-label="Menu" aria-expanded={open} onClick={() => setOpen(!open)}>{open ? <X size={16} /> : <Menu size={16} />}</Btn>
      </div></header>
      {ticker.length > 0 && <div className="ticker" aria-label="Live ticker"><span className="tag">LIVE DESK</span><div className="track">{[...ticker, ...ticker].map((t, i) => <span key={i}>• {t}</span>)}</div></div>}
      <main id="main"><Outlet /></main>
      <footer className="footer"><div className="container row between"><div><div className="brand" style={{ color: '#fff' }}>SportsWeek / Bahria</div><p className="muted" style={{ marginTop: 8, color: '#bdb9a8' }}>Every result stays on record.</p></div>
        <p className="mono" style={{ fontSize: 11, color: '#bdb9a8' }}>Scores refresh every 5 s · Times shown in campus time (Asia/Karachi)</p></div></footer>
    </>
  );
}

interface Item { to: string; label: string; icon: ReactNode }
const NAV: Record<'captain' | 'ops' | 'admin', { title: string; items: (Item | string)[] }> = {
  captain: { title: 'Captain', items: [
    { to: '/captain', label: 'Overview', icon: <Gauge size={15} /> }, { to: '/captain/teams', label: 'My teams', icon: <Users size={15} /> },
    { to: '/captain/fixtures', label: 'My fixtures', icon: <CalendarDays size={15} /> }, { to: '/captain/notifications', label: 'Notifications', icon: <Bell size={15} /> }] },
  ops: { title: 'Operator desk', items: [
    { to: '/ops', label: 'Tournaments', icon: <Trophy size={15} /> }, { to: '/ops/teams', label: 'Team review', icon: <ListChecks size={15} /> }] },
  admin: { title: 'Admin console', items: [
    { to: '/workspace/dashboard', label: 'Dashboard', icon: <Gauge size={15} /> }, 'People',
    { to: '/workspace/users', label: 'Users', icon: <UserSquare2 size={15} /> }, { to: '/workspace/participants', label: 'Participants', icon: <Users size={15} /> },
    'Competition', { to: '/workspace/teams', label: 'Teams', icon: <Layers size={15} /> }, { to: '/workspace/tournaments', label: 'Tournaments', icon: <Trophy size={15} /> },
    { to: '/workspace/departments', label: 'Departments', icon: <Building2 size={15} /> }, 'Governance',
    { to: '/workspace/audit', label: 'Audit log', icon: <ShieldCheck size={15} /> }, { to: '/workspace/exports', label: 'Exports', icon: <Download size={15} /> }] },
};

export function WorkspaceLayout({ kind }: { kind: 'captain' | 'ops' | 'admin' }) {
  const { user, signOut } = useAuth(); const nav = useNavigate(); const loc = useLocation();
  const [open, setOpen] = useState(false); const [confirm, setConfirm] = useState(false);
  const notes = useNotifications(kind === 'captain');
  const unread = kind === 'captain' ? (notes.data ?? []).filter((n) => !n.readAtUtc).length : 0;
  useEffect(() => setOpen(false), [loc.pathname]);
  const cfg = NAV[kind];
  return (
    <div className="ws">
      <a className="skip" href="#main">Skip to content</a>
      <aside className={cx('side', open && 'open')} aria-label={`${cfg.title} navigation`}>
        <Link to="/" className="brand"><Logo />SportsWeek</Link>
        <div className="row" style={{ padding: '0 12px 8px' }}><span className={cx('badge', kind === 'admin' && 'admin')}>{kind === 'admin' ? 'Read-only admin' : cfg.title}</span></div>
        {cfg.items.map((it) => typeof it === 'string' ? <div className="sep" key={it}>{it}</div> :
          <NavLink key={it.to} to={it.to} end={it.to === '/captain' || it.to === '/ops'} className={({ isActive }) => cx('nl', isActive && 'active')}>{it.icon}{it.label}{it.label === 'Notifications' && unread > 0 && <span className="badge" style={{ marginLeft: 'auto' }}>{unread}</span>}</NavLink>)}
        <div style={{ flex: 1 }} />
        <NavLink to="/" className="nl"><LayoutGrid size={15} />Public site</NavLink>
        <div className="sep">{user?.displayName}</div>
        <button className="nl" onClick={() => setConfirm(true)}><LogOut size={15} />Sign out</button>
      </aside>
      <div style={{ minWidth: 0 }}>
        {isMock && <div className="dev-banner">Dev mock mode — fake data</div>}
        <div className="ws-main" id="main">
          <Btn className="ws-mobile" variant="ghost" style={{ marginBottom: 16 }} onClick={() => setOpen(true)}><Menu size={14} />Menu</Btn>
          <Outlet />
        </div>
      </div>
      {confirm && <Dialog title="Sign out?" onClose={() => setConfirm(false)} footer={<><Btn variant="ghost" onClick={() => setConfirm(false)}>Cancel</Btn><Btn onClick={() => void signOut().then(() => nav('/'))}>Sign out</Btn></>}><p>You will return to the public scoreboard.</p></Dialog>}
    </div>
  );
}

export function RequireRole({ roles, children }: { roles: Role[]; children: ReactNode }) {
  const { status, user } = useAuth(); const loc = useLocation();
  if (status === 'loading') return <div className="container" style={{ padding: 48 }}><SkeletonList rows={3} h={80} /></div>;
  if (status === 'rejected') return <Navigate to="/verify-email" replace />;
  if (!user) return <Navigate to="/login" replace state={{ from: loc.pathname }} />;
  if (!roles.includes(user.role)) return <Navigate to="/forbidden" replace />;
  if (user.role === 'Captain' && !user.emailVerified) return <Navigate to="/verify-email" replace />;
  return <>{children}</>;
}
