import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Download, Eye, EyeOff, ShieldCheck } from 'lucide-react';
import { Btn, Chips, DataState, Dialog, Drawer, EmptyState, Field, Pager, RoleBadge, SkeletonList, StatTile, StatusBadge, useDebounced, useToast } from '../components/ui';
import { api, ApiError, fieldError } from '../lib/api';
import { downloadBlob, fmtDate, fmtDateTime, relativeTime } from '../lib/format';
import { useAdminList, useDashboard, useDepartments, useSports, useTournaments } from '../lib/hooks';
import type { AuditEvent, Dashboard, Participant, TeamDetail, TeamSummary, UserDetail, UserSummary } from '../lib/types';

const Head = ({ eyebrow, title, children }: { eyebrow: string; title: string; children?: React.ReactNode }) =>
  <div className="ws-top"><div><div className="eyebrow" style={{ marginBottom: 8 }}>{eyebrow}</div><h2>{title}</h2></div><div className="row">{children}</div></div>;
const ReadOnly = () => <p className="eyebrow">Read-only console · no edit or delete actions exist</p>;
const Bars = ({ rows }: { rows: { name: string; count: number }[] }) => { const max = Math.max(1, ...rows.map((r) => r.count)); return <div>{rows.map((r) => <div className="chart-row" key={r.name}><span>{r.name}</span><div className="bar"><i style={{ width: `${(r.count / max) * 100}%` }} /></div><span className="n">{r.count}</span></div>)}{rows.length === 0 && <p className="muted">No data yet.</p>}</div>; };

export function AdminDashboard() {
  const q = useDashboard();
  return (
    <><Head eyebrow="Read-only admin" title="Dashboard"><span className="eyebrow">{q.data ? `Updated ${new Date(q.data.generatedAtUtc).toLocaleTimeString('en-GB')}` : ''}</span></Head>
      <DataState q={q} skeleton={<div className="grid g4">{Array.from({ length: 8 }, (_, i) => <div key={i} className="skel" style={{ height: 120 }} />)}</div>}>{(d: Dashboard) => (
        <div className="stack" style={{ gap: 24 }}>
          {(d.stalePendingTeams > 0 || d.emailFailures > 0) && <div className="banner warn" role="alert"><ShieldCheck size={18} /><div>{d.stalePendingTeams > 0 && <div><b>{d.stalePendingTeams}</b> team(s) pending for more than 48 hours.</div>}{d.emailFailures > 0 && <div><b>{d.emailFailures}</b> email deliveries failed.</div>}</div></div>}
          <div className="grid g4">
            <StatTile eyebrow="Accounts" value={d.totalUsers} label="Total users" /><StatTile eyebrow="Captains" value={d.activeCaptains} label="Active" />
            <StatTile eyebrow="Competition" value={d.totalTeams} label="Teams" /><StatTile eyebrow="Players" value={d.totalParticipants} label="Participants" />
            <StatTile eyebrow="Pending" value={d.pendingTeams} label="Awaiting review" alert={d.stalePendingTeams > 0} /><StatTile eyebrow="Approved" value={d.approvedTeams} label="In the draw" />
            <StatTile eyebrow="Tournaments" value={d.totalTournaments} label="Total" /><StatTile eyebrow="Email" value={d.emailFailures} label="Failures" alert={d.emailFailures > 0} />
          </div>
          <div className="grid g2">
            <section className="card"><h4>Registrations · last 14 days</h4><div className="cols" role="img" aria-label="Registrations per day">{d.registrationsPerDay.map((x) => <div className="c" key={x.dayUtc}><i style={{ height: `${(x.count / Math.max(1, ...d.registrationsPerDay.map((y) => y.count))) * 100}%` }} /><span>{new Date(x.dayUtc).getUTCDate()}</span></div>)}{d.registrationsPerDay.length === 0 && <p className="muted">No registrations yet.</p>}</div></section>
            <section className="card"><h4 style={{ marginBottom: 10 }}>Team status</h4><Bars rows={[{ name: 'Pending', count: d.pendingTeams }, { name: 'Approved', count: d.approvedTeams }, { name: 'Rejected', count: d.rejectedTeams }, { name: 'Withdrawn', count: d.withdrawnTeams }]} /></section>
            <section className="card"><h4 style={{ marginBottom: 10 }}>Participants by department</h4><Bars rows={d.participantsByDepartment} /></section>
            <section className="card"><h4 style={{ marginBottom: 10 }}>Users by role</h4><Bars rows={d.usersByRole} /></section>
          </div><ReadOnly />
        </div>)}</DataState></>
  );
}

function ExportDialog({ kind, filters, onClose }: { kind: 'users' | 'participants'; filters: Record<string, unknown>; onClose: () => void }) {
  const toast = useToast(); const [sens, setSens] = useState(false); const [reason, setReason] = useState(''); const [busy, setBusy] = useState(false); const [err, setErr] = useState<unknown>(null);
  const run = async () => {
    setBusy(true); setErr(null);
    try { const { blob, filename } = await api.download(`/api/admin/exports/${kind}`, { ...filters, includeSensitive: sens, reason: sens ? reason : null }); downloadBlob(blob, filename); toast.ok('Export downloaded and recorded in the audit log'); onClose(); }
    catch (e) { setErr(e); } finally { setBusy(false); }
  };
  return (
    <Dialog title={`Export ${kind} (CSV)`} onClose={onClose} footer={<><Btn variant="ghost" onClick={onClose}>Cancel</Btn><Btn loading={busy} onClick={() => void run()}><Download size={14} />Download</Btn></>}>
      <div className="banner info"><ShieldCheck size={18} /><div>Every export is written to the audit log with your name, filters and row count. Emails and enrollment numbers are <b>masked</b> unless you include sensitive fields. Up to 5,000 rows; 5 exports per 10 minutes.</div></div>
      {err instanceof ApiError && <div className="banner warn" role="alert">{err.message}</div>}
      <label className="row" style={{ cursor: 'pointer' }}><input type="checkbox" checked={sens} onChange={(e) => setSens(e.target.checked)} /> Include sensitive fields (unmasked)</label>
      {sens && <Field label="Reason (min. 10 characters)" error={fieldError(err, 'reason')}>{(p) => <textarea {...p} className="input" rows={3} value={reason} onChange={(e) => setReason(e.target.value)} />}</Field>}
    </Dialog>
  );
}

export function AdminUsers() {
  const [s, setS] = useState(''); const [role, setRole] = useState(''); const [ver, setVer] = useState(''); const [page, setPage] = useState(1); const search = useDebounced(s);
  const q = useAdminList<UserSummary>('/api/admin/users', { search: search || undefined, role: role || undefined, verified: ver === '' ? undefined : ver === 'yes', page, pageSize: 25 });
  const [open, setOpen] = useState<string | null>(null); const [exp, setExp] = useState(false);
  return (
    <><Head eyebrow="People" title="Users"><Btn variant="ghost" onClick={() => setExp(true)}><Download size={14} />Export CSV</Btn></Head>
      <div className="row" style={{ marginBottom: 16, gap: 14 }}><input className="input" style={{ maxWidth: 260 }} placeholder="Search name or email" aria-label="Search users" value={s} onChange={(e) => { setS(e.target.value); setPage(1); }} />
        <Chips value={role} onChange={(v) => { setRole(v); setPage(1); }} options={['', 'Captain', 'Scorer', 'Stakeholder', 'Admin'].map((x) => ({ id: x, label: x || 'All roles' }))} />
        <Chips value={ver} onChange={(v) => { setVer(v); setPage(1); }} options={[{ id: '', label: 'Any verification' }, { id: 'yes', label: 'Verified' }, { id: 'no', label: 'Unverified' }]} /></div>
      <DataState q={q} isEmpty={(d) => d.items.length === 0} empty={<EmptyState title="No users match" />}>{(d) => <><div className="table-wrap"><table className="t cards"><thead><tr><th>User</th><th>Email (masked)</th><th>Role</th><th>Verified</th><th>Teams</th><th>Last sign-in</th></tr></thead><tbody>
        {d.items.map((u) => <tr key={u.id} className="click" tabIndex={0} onClick={() => setOpen(u.id)} onKeyDown={(e) => e.key === 'Enter' && setOpen(u.id)}><td data-label="User"><strong>{u.displayName}</strong>{!u.isActive && <span className="badge withdrawn" style={{ marginLeft: 8 }}>Inactive</span>}</td><td data-label="Email" className="mono">{u.email}</td><td data-label="Role"><RoleBadge role={u.role} /></td>
          <td data-label="Verified">{u.emailVerified ? <span className="badge approved">Verified</span> : <span className="badge pending">Unverified</span>}</td><td data-label="Teams" className="mono">{u.teamsOwned}</td><td data-label="Last sign-in">{relativeTime(u.lastSignInAtUtc)}</td></tr>)}</tbody></table></div>
        <Pager page={d.page} totalPages={d.totalPages} total={d.totalCount} onPage={setPage} /></>}</DataState><ReadOnly />
      {open && <UserDrawer id={open} onClose={() => setOpen(null)} />}
      {exp && <ExportDialog kind="users" filters={{ search: search || undefined, role: role || undefined, verified: ver === '' ? undefined : ver === 'yes' }} onClose={() => setExp(false)} />}</>
  );
}

function UserDrawer({ id, onClose }: { id: string; onClose: () => void }) {
  const [reveal, setReveal] = useState(false); const [ask, setAsk] = useState(false);
  const q = useQuery({ queryKey: ['admin-user', id, reveal], queryFn: () => api.get<UserDetail>(`/api/admin/users/${id}`, { reveal }), staleTime: 0 });
  return (
    <Drawer title="User detail" onClose={onClose}>
      <DataState q={q} skeleton={<SkeletonList rows={4} h={36} />}>{(u) => <>
        <div className="row between"><h3>{u.displayName}</h3><RoleBadge role={u.role} /></div>
        <dl className="kv"><dt>Email</dt><dd className="mono">{u.email} {!reveal && <Btn size="sm" variant="ghost" onClick={() => setAsk(true)}><Eye size={12} />Reveal</Btn>}{reveal && <span className="badge"><EyeOff size={10} />Revealed · audited</span>}</dd>
          <dt>Verified</dt><dd>{u.emailVerified ? 'Yes' : 'No'}</dd><dt>Role source</dt><dd className="mono">{u.roleSource}</dd><dt>Google subject</dt><dd className="mono">{u.googleSubject ? `${u.googleSubject.slice(0, 6)}…` : '—'}</dd><dt>Joined</dt><dd>{fmtDate(u.createdAtUtc)}</dd><dt>Last sign-in</dt><dd>{fmtDateTime(u.lastSignInAtUtc)}</dd></dl>
        <h4>Teams owned</h4>{u.teams.length === 0 ? <p className="muted">None.</p> : u.teams.map((t) => <div className="row between" key={t.teamId}><span><strong>{t.teamName}</strong> <span className="muted">· {t.tournamentName}</span></span><StatusBadge status={t.status} /></div>)}
        <h4>Recent activity</h4>{u.recentActivity.length === 0 ? <p className="muted">No recorded activity.</p> : u.recentActivity.map((a) => <div key={a.id} className="row between" style={{ fontSize: 13 }}><span className="mono">{a.action}</span><span className="muted">{relativeTime(a.occurredAtUtc)}</span></div>)}
        <p className="eyebrow">Opening this view was recorded in the audit log.</p></>}</DataState>
      {ask && <Dialog title="Reveal full email?" onClose={() => setAsk(false)} footer={<><Btn variant="ghost" onClick={() => setAsk(false)}>Cancel</Btn><Btn onClick={() => { setReveal(true); setAsk(false); }}>Reveal and record</Btn></>}><p>This access is recorded in the audit log with your identity.</p></Dialog>}
    </Drawer>
  );
}

export function AdminParticipants() {
  const [s, setS] = useState(''); const [tid, setTid] = useState(''); const [sid, setSid] = useState(''); const [st, setSt] = useState(''); const [dept, setDept] = useState(''); const [page, setPage] = useState(1); const search = useDebounced(s);
  const tours = useTournaments(); const sports = useSports(); const depts = useDepartments();
  const f = { search: search || undefined, tournamentId: tid || undefined, sportId: sid || undefined, teamStatus: st || undefined, department: dept || undefined };
  const q = useAdminList<Participant>('/api/admin/participants', { ...f, page, pageSize: 25 }); const [exp, setExp] = useState(false);
  const reset = (fn: (v: string) => void) => (e: React.ChangeEvent<HTMLSelectElement>) => { fn(e.target.value); setPage(1); };
  return (
    <><Head eyebrow="People" title="Participants"><Btn variant="ghost" onClick={() => setExp(true)}><Download size={14} />Export CSV</Btn></Head>
      {q.data && <p className="eyebrow" style={{ marginBottom: 12 }}>{q.data.totalCount} participants matching filters</p>}
      <div className="row" style={{ marginBottom: 16, gap: 10 }}><input className="input" style={{ maxWidth: 240 }} placeholder="Name, enrollment or team" aria-label="Search" value={s} onChange={(e) => { setS(e.target.value); setPage(1); }} />
        <select className="input" style={{ width: 'auto' }} aria-label="Tournament" value={tid} onChange={reset(setTid)}><option value="">All tournaments</option>{tours.data?.items.map((t) => <option key={t.id} value={t.id}>{t.name}</option>)}</select>
        <select className="input" style={{ width: 'auto' }} aria-label="Sport" value={sid} onChange={reset(setSid)}><option value="">All sports</option>{sports.data?.map((t) => <option key={t.id} value={t.id}>{t.name}</option>)}</select>
        <select className="input" style={{ width: 'auto' }} aria-label="Department" value={dept} onChange={reset(setDept)}><option value="">All departments</option>{depts.data?.map((t) => <option key={t.department}>{t.department}</option>)}</select>
        <select className="input" style={{ width: 'auto' }} aria-label="Team status" value={st} onChange={reset(setSt)}><option value="">Any status</option>{['Pending', 'Approved', 'Rejected', 'Withdrawn'].map((x) => <option key={x}>{x}</option>)}</select></div>
      <DataState q={q} isEmpty={(d) => d.items.length === 0} empty={<EmptyState title="No participants match" />}>{(d) => <><div className="table-wrap"><table className="t cards"><thead><tr><th>Player</th><th>Enrollment</th><th>Dept</th><th>Team</th><th>Sport</th><th>Captain</th><th>Status</th></tr></thead><tbody>
        {d.items.map((p) => <tr key={p.playerId}><td data-label="Player"><strong>{p.fullName}</strong></td><td data-label="Enrollment" className="mono">{p.enrollmentNo}</td><td data-label="Dept">{p.department}</td><td data-label="Team">{p.teamName}</td><td data-label="Sport">{p.sportName}</td><td data-label="Captain">{p.captainName}</td><td data-label="Status"><StatusBadge status={p.teamStatus} /></td></tr>)}</tbody></table></div>
        <Pager page={d.page} totalPages={d.totalPages} total={d.totalCount} onPage={setPage} /></>}</DataState><ReadOnly />
      {exp && <ExportDialog kind="participants" filters={f} onClose={() => setExp(false)} />}</>
  );
}

export function AdminTeams() {
  const [st, setSt] = useState(''); const [s, setS] = useState(''); const [page, setPage] = useState(1); const search = useDebounced(s); const [open, setOpen] = useState<string | null>(null);
  const q = useAdminList<TeamSummary>('/api/admin/teams', { status: st || undefined, search: search || undefined, page, pageSize: 25 });
  return (
    <><Head eyebrow="Competition" title="Teams"><input className="input" style={{ width: 240 }} placeholder="Search team" aria-label="Search" value={s} onChange={(e) => { setS(e.target.value); setPage(1); }} /></Head>
      <div style={{ marginBottom: 16 }}><Chips value={st} onChange={(v) => { setSt(v); setPage(1); }} options={['', 'Pending', 'Approved', 'Rejected', 'Withdrawn'].map((x) => ({ id: x, label: x || 'All' }))} /></div>
      <DataState q={q} isEmpty={(d) => d.items.length === 0} empty={<EmptyState title="No teams" />}>{(d) => <><div className="table-wrap"><table className="t cards"><thead><tr><th>Team</th><th>Tournament</th><th>Captain</th><th>Roster</th><th>Queue age</th><th>Status</th></tr></thead><tbody>
        {d.items.map((t) => { const stale = t.status === 'Pending' && Date.now() - new Date(t.submittedAtUtc).getTime() > 48 * 3600e3; return <tr key={t.teamId} className="click" tabIndex={0} onClick={() => setOpen(t.teamId)} onKeyDown={(e) => e.key === 'Enter' && setOpen(t.teamId)}>
          <td data-label="Team"><strong>{t.teamName}</strong><div className="muted" style={{ fontSize: 12 }}>{t.department}</div></td><td data-label="Tournament">{t.tournamentName}</td><td data-label="Captain">{t.captainName}</td><td data-label="Roster" className="mono">{t.rosterCount}</td>
          <td data-label="Queue age" style={stale ? { color: '#b3360a', fontWeight: 700 } : undefined}>{t.status === 'Pending' ? relativeTime(t.submittedAtUtc) : '—'}{stale && ' · >48h'}</td><td data-label="Status"><StatusBadge status={t.status} /></td></tr>; })}</tbody></table></div>
        <Pager page={d.page} totalPages={d.totalPages} total={d.totalCount} onPage={setPage} /></>}</DataState><ReadOnly />
      {open && <TeamDrawer id={open} onClose={() => setOpen(null)} />}</>
  );
}
function TeamDrawer({ id, onClose }: { id: string; onClose: () => void }) {
  const q = useQuery({ queryKey: ['admin-team', id], queryFn: () => api.get<TeamDetail>(`/api/admin/teams/${id}`), staleTime: 0 });
  return <Drawer title="Team detail" onClose={onClose}><DataState q={q} skeleton={<SkeletonList rows={4} h={36} />}>{(d) => <>
    <div className="row between"><h3>{d.team.teamName}</h3><StatusBadge status={d.team.status} /></div>
    <dl className="kv"><dt>Tournament</dt><dd>{d.team.tournamentName}</dd><dt>Captain</dt><dd>{d.team.captainName} <span className="mono muted">{d.team.captainEmail}</span></dd><dt>Submitted</dt><dd>{fmtDateTime(d.team.submittedAtUtc)}</dd>{d.team.rejectionReason && <><dt>Rejection</dt><dd>{d.team.rejectionReason}</dd></>}</dl>
    <h4>Roster</h4><div className="table-wrap"><table className="t"><tbody>{d.roster.map((p) => <tr key={p.playerId}><td>{p.fullName}</td><td className="mono">{p.enrollmentNo}</td><td>{p.department}</td></tr>)}</tbody></table></div></>}</DataState></Drawer>;
}

export function AdminTournaments() {
  const q = useTournaments();
  return <><Head eyebrow="Competition" title="Tournaments" /><DataState q={q} isEmpty={(d) => d.items.length === 0} empty={<EmptyState title="No tournaments" />}>{(d) => <div className="table-wrap"><table className="t cards"><thead><tr><th>Tournament</th><th>Sport</th><th>Status</th><th>Dates</th><th>Teams</th><th>Matches</th></tr></thead><tbody>
    {d.items.map((t) => <tr key={t.id}><td data-label="Tournament"><strong>{t.name}</strong></td><td data-label="Sport">{t.sportName}</td><td data-label="Status"><StatusBadge status={t.status} /></td><td data-label="Dates">{fmtDate(t.startsAtUtc)} → {fmtDate(t.endsAtUtc)}</td><td data-label="Teams" className="mono">{t.approvedTeams}{t.teamLimit ? `/${t.teamLimit}` : ''}</td><td data-label="Matches" className="mono">{t.fixturesCompleted}/{t.fixturesTotal}</td></tr>)}</tbody></table></div>}</DataState><ReadOnly /></>;
}

export function AdminDepartments() {
  const q = useDepartments();
  return <><Head eyebrow="Governance" title="Departments" /><DataState q={q} isEmpty={(d) => d.length === 0} empty={<EmptyState title="No participation yet" />}>{(d) => { const max = Math.max(1, ...d.map((x) => x.participants));
    return <div className="grid g2">{d.map((x) => <div className="card stack" key={x.department}><div className="row between"><h3>{x.department}</h3><span className="mono"><b style={{ fontSize: 24 }}>{x.participants}</b> players</span></div><div className="bar"><i style={{ width: `${(x.participants / max) * 100}%` }} /></div><div className="muted" style={{ fontSize: 14 }}>{x.teams} teams · {x.approvedTeams} approved</div></div>)}</div>; }}</DataState><ReadOnly /></>;
}

export function AdminAudit({ exportsOnly }: { exportsOnly?: boolean }) {
  const [action, setAction] = useState(exportsOnly ? 'admin.export.users' : ''); const [page, setPage] = useState(1); const [open, setOpen] = useState<AuditEvent | null>(null);
  const q = useAdminList<AuditEvent>('/api/admin/audit', { action: action || undefined, page, pageSize: 25 });
  const opts = exportsOnly ? [{ id: 'admin.export.users', label: 'User exports' }, { id: 'admin.export.participants', label: 'Participant exports' }] : [{ id: '', label: 'All' }, { id: 'admin.user.viewed', label: 'User views' }, { id: 'admin.user.revealed_email', label: 'Email reveals' }, { id: 'admin.export.users', label: 'Exports' }, { id: 'ops.result.recorded', label: 'Results' }, { id: 'auth.role_changed', label: 'Role changes' }];
  return (
    <><Head eyebrow="Governance" title={exportsOnly ? 'Export history' : 'Audit log'} />
      <div className="banner info" style={{ marginBottom: 16 }}><ShieldCheck size={18} /><div>Audit events are append-only — the database blocks edits and deletes, even for the API.</div></div>
      <div style={{ marginBottom: 16 }}><Chips value={action} onChange={(v) => { setAction(v); setPage(1); }} options={opts} /></div>
      <DataState q={q} isEmpty={(d) => d.items.length === 0} empty={<EmptyState title="No events" />}>{(d) => <><div className="table-wrap"><table className="t cards"><thead><tr><th>Time</th><th>Actor</th><th>Action</th><th>Entity</th><th>Result</th></tr></thead><tbody>
        {d.items.map((a) => <tr key={a.id} className="click" tabIndex={0} onClick={() => setOpen(a)} onKeyDown={(e) => e.key === 'Enter' && setOpen(a)}><td data-label="Time" className="mono">{fmtDateTime(a.occurredAtUtc)}</td><td data-label="Actor">{a.actorName ?? '—'} {a.actorRole && <RoleBadge role={a.actorRole} />}</td><td data-label="Action" className="mono">{a.action}</td><td data-label="Entity" className="mono">{a.entityType ?? '—'}</td><td data-label="Result"><span className={`badge ${a.result === 'success' ? 'approved' : 'rejected'}`}>{a.result}</span></td></tr>)}</tbody></table></div>
        <Pager page={d.page} totalPages={d.totalPages} total={d.totalCount} onPage={setPage} /></>}</DataState>
      {open && <Drawer title="Audit event" onClose={() => setOpen(null)}><dl className="kv"><dt>Time (UTC)</dt><dd className="mono">{open.occurredAtUtc}</dd><dt>Action</dt><dd className="mono">{open.action}</dd><dt>Actor</dt><dd>{open.actorName ?? '—'}</dd><dt>Entity</dt><dd className="mono">{open.entityType} {open.entityId}</dd><dt>Correlation</dt><dd className="mono">{open.correlationId ?? '—'}</dd></dl><h4>Metadata</h4><pre className="card mono" style={{ fontSize: 12, overflow: 'auto', margin: 0 }}>{open.metadataJson ? JSON.stringify(JSON.parse(open.metadataJson), null, 2) : 'None'}</pre></Drawer>}</>
  );
}

export function AdminExports() {
  const [kind, setKind] = useState<'users' | 'participants' | null>(null);
  return (
    <><Head eyebrow="Governance" title="Exports" />
      <div className="grid g2"><div className="card stack"><h3>Users</h3><p className="muted">Directory of accounts with roles and verification. Emails masked by default.</p><div><Btn onClick={() => setKind('users')}><Download size={14} />New users export</Btn></div></div>
        <div className="card stack"><h3>Participants</h3><p className="muted">Every player with team, sport and department. Enrollment numbers masked by default.</p><div><Btn onClick={() => setKind('participants')}><Download size={14} />New participants export</Btn></div></div></div>
      <div style={{ marginTop: 32 }}><AdminAudit exportsOnly /></div>
      {kind && <ExportDialog kind={kind} filters={{}} onClose={() => setKind(null)} />}</>
  );
}
