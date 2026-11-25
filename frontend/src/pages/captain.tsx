import { useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { Bell, CalendarDays, CheckCircle2, Plus, Trash2, Users } from 'lucide-react';
import { FixtureRow } from '../components/fixtures';
import { Btn, DataState, Dialog, EmptyState, Field, StatTile, StatusBadge, useToast } from '../components/ui';
import { api, ApiError, fieldError, newIdempotencyKey } from '../lib/api';
import { useAuth } from '../lib/auth';
import { fmtDateTime, relativeTime } from '../lib/format';
import { useAction, useMyFixtures, useMyTeams, useNotifications, useTournaments } from '../lib/hooks';
import type { CaptainTeam, PlayerInput, PublicTournament, WithdrawResult } from '../lib/types';

const Head = ({ eyebrow, title, children }: { eyebrow: string; title: string; children?: React.ReactNode }) =>
  <div className="ws-top"><div><div className="eyebrow" style={{ marginBottom: 8 }}>{eyebrow}</div><h2>{title}</h2></div>{children}</div>;

export function CaptainOverview() {
  const { user } = useAuth(); const teams = useMyTeams(); const fx = useMyFixtures(); const notes = useNotifications(); const tours = useTournaments('RegistrationOpen');
  const next = fx.data?.filter((f) => f.status === 'Scheduled' || f.status === 'Ready' || f.status === 'Live').sort((a, b) => (a.startsAtUtc ?? '9').localeCompare(b.startsAtUtc ?? '9'))[0];
  const unread = notes.data?.filter((n) => !n.readAtUtc).length ?? 0;
  return (
    <>
      <Head eyebrow="Captain workspace" title={`Hello, ${user?.displayName.split(' ')[0] ?? 'captain'}`}><span className="badge approved"><CheckCircle2 size={12} />Verified Google account</span></Head>
      <div className="grid g4" style={{ marginBottom: 24 }}>
        <StatTile eyebrow="Teams" value={teams.data?.length ?? '–'} label="Registered" icon={<Users size={16} />} />
        <StatTile eyebrow="Approved" value={teams.data?.filter((t) => t.status === 'Approved').length ?? '–'} label="In the draw" />
        <StatTile eyebrow="Alerts" value={unread} label="Unread" icon={<Bell size={16} />} alert={unread > 0} />
        <StatTile eyebrow="Open" value={tours.data?.items.length ?? '–'} label="Tournaments to join" />
      </div>
      <div className="grid g2">
        <section className="card stack"><h3>Your teams</h3>
          <DataState q={teams} isEmpty={(d) => d.length === 0} empty={<EmptyState title="No teams yet" action={<Link className="btn" to="/captain/teams">Register a team</Link>}>Pick an open tournament and add your roster.</EmptyState>}>
            {(d) => <>{d.slice(0, 4).map((t) => <div key={t.teamId} className="row between"><div><strong>{t.teamName}</strong><div className="muted" style={{ fontSize: 13 }}>{t.tournamentName}</div></div><StatusBadge status={t.status} /></div>)}<Link to="/captain/teams">Manage teams →</Link></>}
          </DataState></section>
        <section className="card stack"><h3>Next fixture</h3>
          {fx.isPending ? <p className="muted">Loading…</p> : next ? <FixtureRow f={next} /> : <p className="muted">No upcoming match yet. Fixtures appear after registration closes.</p>}
          <Link to="/captain/fixtures">All my fixtures →</Link></section>
      </div>
      <section className="stack" style={{ marginTop: 28 }}><h3>Open for registration</h3>
        <DataState q={tours} isEmpty={(d) => d.items.length === 0} empty={<EmptyState title="Nothing open right now" />}>{(d) => <div className="grid g3">{d.items.map((t) => <div className="card" key={t.id}><div className="eyebrow">{t.sportName}</div><h4 style={{ margin: '8px 0' }}>{t.name}</h4><p className="muted" style={{ fontSize: 13 }}>{t.approvedTeams}{t.teamLimit ? `/${t.teamLimit}` : ''} teams · roster {t.minPlayers}–{t.maxPlayers}</p></div>)}</div>}</DataState>
      </section>
    </>
  );
}

export function CaptainTeams() {
  const teams = useMyTeams(); const [reg, setReg] = useState(false); const [wd, setWd] = useState<CaptainTeam | null>(null);
  return (
    <>
      <Head eyebrow="Captain workspace" title="My teams"><Btn onClick={() => setReg(true)}><Plus size={14} />Register a team</Btn></Head>
      <DataState q={teams} isEmpty={(d) => d.length === 0} empty={<EmptyState title="No teams yet" action={<Btn onClick={() => setReg(true)}>Register a team</Btn>}>Registration takes about two minutes.</EmptyState>}>
        {(d) => <div className="grid g2">{d.map((t) => (
          <div className="card stack" key={t.teamId}>
            <div className="row between"><span className="eyebrow">{t.sportName}</span><StatusBadge status={t.status} /></div>
            <h3>{t.teamName}</h3>
            <div className="muted" style={{ fontSize: 14 }}>{t.tournamentName} · {t.department} · {t.rosterCount} players</div>
            {t.status === 'Rejected' && t.rejectionReason && <div className="banner warn" role="note"><strong>Why:</strong> {t.rejectionReason}</div>}
            {t.status === 'Pending' && <div className="banner info">Waiting for operator approval · submitted {relativeTime(t.submittedAtUtc)}</div>}
            {(t.status === 'Pending' || t.status === 'Approved') && <div><Btn size="sm" variant="danger" onClick={() => setWd(t)}>Withdraw team</Btn></div>}
          </div>))}</div>}
      </DataState>
      {reg && <RegisterTeamDialog onClose={() => setReg(false)} />}
      {wd && <WithdrawDialog team={wd} onClose={() => setWd(null)} />}
    </>
  );
}

function WithdrawDialog({ team, onClose }: { team: CaptainTeam; onClose: () => void }) {
  const toast = useToast(); const [reason, setReason] = useState('');
  const m = useAction((r: string) => api.post<WithdrawResult>(`/api/captain/teams/${team.teamId}/withdraw`, { reason: r || null }), [['my-teams'], ['my-fixtures']]);
  return (
    <Dialog title={`Withdraw ${team.teamName}?`} onClose={onClose} footer={<><Btn variant="ghost" onClick={onClose}>Keep team</Btn>
      <Btn variant="danger" loading={m.isPending} onClick={() => m.mutate(reason, { onSuccess: () => { toast.ok('Team withdrawn'); onClose(); }, onError: toast.error })}>Withdraw</Btn></>}>
      <p>Your opponents in unplayed matches will advance by walkover. This cannot be undone.</p>
      <Field label="Reason (optional)">{(p) => <textarea {...p} className="input" rows={3} maxLength={300} value={reason} onChange={(e) => setReason(e.target.value)} />}</Field>
    </Dialog>
  );
}

const empty = (): PlayerInput => ({ fullName: '', enrollmentNo: '', department: '' });

function RegisterTeamDialog({ onClose }: { onClose: () => void }) {
  const toast = useToast(); const tours = useTournaments('RegistrationOpen'); const [step, setStep] = useState(0);
  const [tid, setTid] = useState(''); const [name, setName] = useState(''); const [dept, setDept] = useState(''); const [players, setPlayers] = useState<PlayerInput[]>([empty(), empty(), empty()]);
  const [key] = useState(newIdempotencyKey);           // one key per dialog: retries after a network error never create a second team
  const [err, setErr] = useState<unknown>(null); const [done, setDone] = useState(false);
  const t: PublicTournament | undefined = tours.data?.items.find((x) => x.id === tid);
  const m = useAction((b: { name: string; dept: string; players: PlayerInput[] }) =>
    api.post<{ teamId: string }>(`/api/captain/tournaments/${tid}/teams`, { teamName: b.name, department: b.dept, players: b.players }, { 'Idempotency-Key': key }), [['my-teams'], ['tournaments']]);

  const local = useMemo(() => {
    const e: Record<string, string> = {}; const seen = new Set<string>();
    if (step >= 1) { if (name.trim().length < 3) e.teamName = 'Team name must be 3–60 characters.'; if (dept.trim().length < 2) e.department = 'Department is required.'; }
    if (step >= 2) players.forEach((p, i) => {
      if (p.fullName.trim().length < 2) e[`players[${i}].fullName`] = 'Name is required.';
      if (!/^[A-Za-z0-9][A-Za-z0-9-]{4,19}$/.test(p.enrollmentNo.trim())) e[`players[${i}].enrollmentNo`] = '5–20 letters, digits or dashes.';
      else if (seen.has(p.enrollmentNo.trim().toUpperCase())) e[`players[${i}].enrollmentNo`] = 'Duplicate in this roster.'; else seen.add(p.enrollmentNo.trim().toUpperCase());
      if (p.department.trim().length < 2) e[`players[${i}].department`] = 'Required.';
    });
    return e;
  }, [step, name, dept, players]);
  const fe = (k: string) => local[k] ?? fieldError(err, k);
  const sizeOk = !!t && players.length >= t.minPlayers && players.length <= t.maxPlayers;
  const next = () => { setErr(null); if (step === 0 && !tid) return; if (step === 1 && (local.teamName || local.department)) return; if (step === 2 && (Object.keys(local).length || !sizeOk)) return; setStep(step + 1); };
  const submit = () => m.mutate({ name, dept, players }, { onSuccess: () => { setDone(true); toast.ok('Team submitted for approval'); }, onError: (e) => setErr(e) });
  const steps = ['Tournament', 'Team', 'Roster', 'Review'];

  if (done) return <Dialog title="Submitted" onClose={onClose} footer={<Btn onClick={onClose}>Done</Btn>}><div className="state"><CheckCircle2 size={28} /><h3>{name}</h3><p>Your team is <StatusBadge status="Pending" /> — an operator will review it shortly. You’ll get a notification.</p></div></Dialog>;
  return (
    <Dialog title="Register a team" onClose={onClose} footer={<>
      {step > 0 && <Btn variant="ghost" onClick={() => setStep(step - 1)} disabled={m.isPending}>Back</Btn>}
      {step < 3 ? <Btn onClick={next}>Continue</Btn> : <Btn loading={m.isPending} onClick={submit}>Submit team</Btn>}</>}>
      <div className="stepper">{steps.map((s, i) => <div key={s} className={i === step ? 's on' : i < step ? 's done' : 's'}>{i + 1}. {s}</div>)}</div>
      {err instanceof ApiError && !err.fieldErrors && <div className="banner warn" role="alert">{err.message}</div>}
      {err instanceof ApiError && err.fieldErrors && <div className="banner warn" role="alert">Please fix the highlighted fields.</div>}
      {step === 0 && <DataState q={tours} isEmpty={(d) => d.items.length === 0} empty={<EmptyState title="No tournament is open" />}>{(d) =>
        <Field label="Tournament">{(p) => <select {...p} className="input" value={tid} onChange={(e) => { setTid(e.target.value); const mt = d.items.find((x) => x.id === e.target.value); if (mt) setPlayers(Array.from({ length: mt.minPlayers }, empty)); }}>
          <option value="">Choose…</option>{d.items.map((x) => <option key={x.id} value={x.id} disabled={!!x.teamLimit && x.approvedTeams >= x.teamLimit}>{x.name}{x.teamLimit && x.approvedTeams >= x.teamLimit ? ' (full)' : ''}</option>)}</select>}</Field>}</DataState>}
      {step === 1 && <><Field label="Team name" error={fe('teamName')}>{(p) => <input {...p} className="input" maxLength={60} value={name} onChange={(e) => setName(e.target.value)} />}</Field>
        <Field label="Department" error={fe('department')}>{(p) => <input {...p} className="input" maxLength={60} placeholder="e.g. Computer Science" value={dept} onChange={(e) => { setDept(e.target.value); setPlayers((ps) => ps.map((x) => ({ ...x, department: x.department || e.target.value }))); }} />}</Field></>}
      {step === 2 && t && <>
        <div className="row between"><span className="eyebrow">Roster {players.length} / {t.minPlayers}–{t.maxPlayers}</span><Btn size="sm" variant="ghost" disabled={players.length >= t.maxPlayers} onClick={() => setPlayers([...players, { ...empty(), department: dept }])}><Plus size={12} />Add player</Btn></div>
        {fe('players') && <div className="err">{fe('players')}</div>}
        {players.map((p, i) => <div className="player" key={i}>
          <Field label={i === 0 ? 'Full name' : ' '} error={fe(`players[${i}].fullName`)}>{(a) => <input {...a} className="input" aria-label={`Player ${i + 1} name`} value={p.fullName} onChange={(e) => setPlayers(players.map((x, j) => j === i ? { ...x, fullName: e.target.value } : x))} />}</Field>
          <Field label={i === 0 ? 'Enrollment' : ' '} error={fe(`players[${i}].enrollmentNo`)}>{(a) => <input {...a} className="input mono" aria-label={`Player ${i + 1} enrollment`} value={p.enrollmentNo} onChange={(e) => setPlayers(players.map((x, j) => j === i ? { ...x, enrollmentNo: e.target.value } : x))} />}</Field>
          <Field label={i === 0 ? 'Department' : ' '} error={fe(`players[${i}].department`)}>{(a) => <input {...a} className="input" aria-label={`Player ${i + 1} department`} value={p.department} onChange={(e) => setPlayers(players.map((x, j) => j === i ? { ...x, department: e.target.value } : x))} />}</Field>
          <Btn size="sm" variant="ghost" aria-label={`Remove player ${i + 1}`} style={{ marginTop: i === 0 ? 22 : 0 }} disabled={players.length <= t.minPlayers} onClick={() => setPlayers(players.filter((_, j) => j !== i))}><Trash2 size={12} /></Btn>
        </div>)}
        {!sizeOk && <div className="err">This sport needs {t.minPlayers}–{t.maxPlayers} players.</div>}
      </>}
      {step === 3 && <dl className="kv"><dt>Tournament</dt><dd>{t?.name}</dd><dt>Team</dt><dd>{name}</dd><dt>Department</dt><dd>{dept}</dd><dt>Roster</dt><dd>{players.map((p) => `${p.fullName} (${p.enrollmentNo})`).join(', ')}</dd></dl>}
    </Dialog>
  );
}

export function CaptainFixtures() {
  const q = useMyFixtures();
  return <><Head eyebrow="Captain workspace" title="My fixtures" /><DataState q={q} isEmpty={(d) => d.length === 0} empty={<EmptyState title="No fixtures yet">Your matches show up after fixtures are generated and scheduled.</EmptyState>}>
    {(d) => <div className="card">{d.map((f) => <div key={f.id}><FixtureRow f={f} /><div className="muted" style={{ fontSize: 12, padding: '0 4px 8px' }}><CalendarDays size={11} /> {fmtDateTime(f.startsAtUtc)}</div></div>)}</div>}</DataState></>;
}

export function CaptainNotifications() {
  const q = useNotifications(); const toast = useToast();
  const read = useAction(() => api.post('/api/captain/notifications/read-all'), [['notifications']]);
  return (
    <><Head eyebrow="Captain workspace" title="Notifications"><Btn variant="ghost" loading={read.isPending} disabled={!q.data?.some((n) => !n.readAtUtc)} onClick={() => read.mutate(undefined, { onError: toast.error })}>Mark all read</Btn></Head>
      <DataState q={q} isEmpty={(d) => d.length === 0} empty={<EmptyState title="You’re all caught up">Approvals, schedule changes and results appear here.</EmptyState>}>
        {(d) => <div className="stack">{d.map((n) => <div className="card row" key={n.id} style={{ alignItems: 'flex-start', borderLeft: n.readAtUtc ? undefined : '6px solid var(--lime)' }}>
          <div className="grow"><div className="row between"><strong>{n.title}</strong><span className="mono muted" style={{ fontSize: 11 }}>{relativeTime(n.createdAtUtc)}</span></div>{n.body && <p className="muted" style={{ fontSize: 14, marginTop: 4 }}>{n.body}</p>}</div>
          {!n.readAtUtc && <span className="dot" title="Unread" />}</div>)}</div>}</DataState></>
  );
}
