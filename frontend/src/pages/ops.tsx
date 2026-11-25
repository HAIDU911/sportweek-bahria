import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Link, useParams } from 'react-router-dom';
import { Check, Play, Plus, RefreshCw, Shuffle, X } from 'lucide-react';
import { Btn, Chips, DataState, Dialog, Drawer, EmptyState, ErrorState, Field, Pager, SkeletonList, StatusBadge, Tabs, useDebounced, useToast } from '../components/ui';
import { api, ApiError, fieldError } from '../lib/api';
import { fmtDateTime, isFinished, localToUtcIso, relativeTime, roundLabel } from '../lib/format';
import { useAction, useOpsFixtures, useOpsTeams, useOpsTournaments, useSports } from '../lib/hooks';
import type { OpsFixture, OpsTournament, RegenerationPreview, TeamDetail, TeamSummary, TournamentStatus, WithdrawResult } from '../lib/types';

const Head = ({ eyebrow, title, children }: { eyebrow: string; title: string; children?: React.ReactNode }) =>
  <div className="ws-top"><div><div className="eyebrow" style={{ marginBottom: 8 }}>{eyebrow}</div><h2>{title}</h2></div><div className="row">{children}</div></div>;

const nextStatus: Partial<Record<TournamentStatus, { to: TournamentStatus; label: string }[]>> = {
  Draft: [{ to: 'RegistrationOpen', label: 'Open registration' }],
  RegistrationOpen: [{ to: 'RegistrationClosed', label: 'Close registration' }],
  RegistrationClosed: [{ to: 'RegistrationOpen', label: 'Reopen' }],
};

export function OpsTournaments() {
  const q = useOpsTournaments(); const toast = useToast(); const [create, setCreate] = useState(false);
  const status = useAction((b: { id: string; status: TournamentStatus }) => api.post(`/api/ops/tournaments/${b.id}/status`, { status: b.status }), [['ops-tournaments'], ['tournaments']]);
  return (
    <>
      <Head eyebrow="Operator desk" title="Tournaments"><Btn onClick={() => setCreate(true)}><Plus size={14} />New tournament</Btn></Head>
      <DataState q={q} isEmpty={(d) => d.length === 0} empty={<EmptyState title="No tournaments yet" action={<Btn onClick={() => setCreate(true)}>Create one</Btn>} />}>
        {(d) => <div className="grid g2">{d.map((t) => (
          <div className="card stack" key={t.id}>
            <div className="row between"><span className="eyebrow">{t.sportName}</span><StatusBadge status={t.status} /></div>
            <h3>{t.name}</h3>
            <div className="row" style={{ gap: 18, fontSize: 14 }}><span><b className="mono">{t.approvedTeams}</b>{t.teamLimit ? `/${t.teamLimit}` : ''} approved</span><span><b className="mono">{t.pendingTeams}</b> pending</span><span><b className="mono">{t.fixturesResolved}/{t.fixturesTotal}</b> matches</span></div>
            <div className="row">
              <Link className="btn" to={`/ops/tournaments/${t.id}`}>Manage</Link>
              {(nextStatus[t.status] ?? []).map((n) => <Btn key={n.to} variant="ghost" loading={status.isPending && status.variables?.id === t.id} onClick={() => status.mutate({ id: t.id, status: n.to }, { onSuccess: () => toast.ok(`${t.name}: ${n.label.toLowerCase()} done`), onError: toast.error })}>{n.label}</Btn>)}
            </div>
          </div>))}</div>}
      </DataState>
      {create && <CreateTournament onClose={() => setCreate(false)} />}
    </>
  );
}

function CreateTournament({ onClose }: { onClose: () => void }) {
  const sports = useSports(); const toast = useToast(); const [f, setF] = useState({ sportId: '', name: '', starts: '', ends: '', limit: '16', third: true }); const [err, setErr] = useState<unknown>(null);
  const m = useAction(() => api.post('/api/ops/tournaments', { sportId: f.sportId, name: f.name, startsAtUtc: f.starts ? localToUtcIso(f.starts) : null, endsAtUtc: f.ends ? localToUtcIso(f.ends) : null, teamLimit: f.limit ? Number(f.limit) : null, thirdPlaceMatch: f.third }), [['ops-tournaments']]);
  return (
    <Dialog title="New tournament" onClose={onClose} footer={<><Btn variant="ghost" onClick={onClose}>Cancel</Btn><Btn loading={m.isPending} disabled={!f.sportId} onClick={() => m.mutate(undefined, { onSuccess: () => { toast.ok('Tournament created as Draft'); onClose(); }, onError: setErr })}>Create</Btn></>}>
      {err instanceof ApiError && !err.fieldErrors && <div className="banner warn" role="alert">{err.message}</div>}
      <Field label="Sport">{(p) => <select {...p} className="input" value={f.sportId} onChange={(e) => setF({ ...f, sportId: e.target.value })}><option value="">Choose…</option>{sports.data?.map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}</select>}</Field>
      <Field label="Name" error={fieldError(err, 'name')}>{(p) => <input {...p} className="input" value={f.name} onChange={(e) => setF({ ...f, name: e.target.value })} />}</Field>
      <div className="grid g2"><Field label="Starts (campus time)">{(p) => <input {...p} type="datetime-local" className="input" value={f.starts} onChange={(e) => setF({ ...f, starts: e.target.value })} />}</Field>
        <Field label="Ends" error={fieldError(err, 'endsAtUtc')}>{(p) => <input {...p} type="datetime-local" className="input" value={f.ends} onChange={(e) => setF({ ...f, ends: e.target.value })} />}</Field></div>
      <div className="grid g2"><Field label="Team limit (2–64)" error={fieldError(err, 'teamLimit')}>{(p) => <input {...p} type="number" min={2} max={64} className="input" value={f.limit} onChange={(e) => setF({ ...f, limit: e.target.value })} />}</Field>
        <Field label="Third-place match">{(p) => <select {...p} className="input" value={String(f.third)} onChange={(e) => setF({ ...f, third: e.target.value === 'true' })}><option value="true">Yes</option><option value="false">No</option></select>}</Field></div>
    </Dialog>
  );
}

export function OpsTeams() {
  const [status, setStatus] = useState('Pending'); const [s, setS] = useState(''); const [page, setPage] = useState(1); const search = useDebounced(s);
  const q = useOpsTeams({ status: status || undefined, search: search || undefined, page }); const [open, setOpen] = useState<TeamSummary | null>(null);
  return (
    <>
      <Head eyebrow="Operator desk" title="Team review"><input className="input" style={{ width: 240 }} placeholder="Search team" aria-label="Search" value={s} onChange={(e) => { setS(e.target.value); setPage(1); }} /></Head>
      <div style={{ marginBottom: 16 }}><Chips value={status} onChange={(v) => { setStatus(v); setPage(1); }} options={['Pending', 'Approved', 'Rejected', 'Withdrawn', ''].map((x) => ({ id: x, label: x || 'All' }))} /></div>
      <DataState q={q} isEmpty={(d) => d.items.length === 0} empty={<EmptyState title={status === 'Pending' ? 'Review queue is clear' : 'No teams'}>New registrations appear here.</EmptyState>}>
        {(d) => <><div className="table-wrap"><table className="t cards"><thead><tr><th>Team</th><th>Tournament</th><th>Captain</th><th>Roster</th><th>Submitted</th><th>Status</th></tr></thead><tbody>
          {d.items.map((t) => { const stale = t.status === 'Pending' && Date.now() - new Date(t.submittedAtUtc).getTime() > 48 * 3600e3;
            return <tr key={t.teamId} className="click" tabIndex={0} onClick={() => setOpen(t)} onKeyDown={(e) => e.key === 'Enter' && setOpen(t)}>
              <td data-label="Team"><strong>{t.teamName}</strong><div className="muted" style={{ fontSize: 12 }}>{t.department}</div></td><td data-label="Tournament">{t.tournamentName}</td><td data-label="Captain">{t.captainName}<div className="muted mono" style={{ fontSize: 11 }}>{t.captainEmail}</div></td>
              <td data-label="Roster" className="mono">{t.rosterCount}</td><td data-label="Submitted" style={stale ? { color: '#b3360a', fontWeight: 700 } : undefined}>{relativeTime(t.submittedAtUtc)}{stale && ' · overdue'}</td><td data-label="Status"><StatusBadge status={t.status} /></td></tr>; })}</tbody></table></div>
          <Pager page={d.page} totalPages={d.totalPages} total={d.totalCount} onPage={setPage} /></>}
      </DataState>
      {open && <ReviewDrawer team={open} onClose={() => setOpen(null)} />}
    </>
  );
}

function ReviewDrawer({ team, onClose }: { team: TeamSummary; onClose: () => void }) {
  const toast = useToast(); const [reject, setReject] = useState(false); const [reason, setReason] = useState(''); const [err, setErr] = useState<unknown>(null);
  const detail = useDetail(team.teamId);
  const m = useAction((b: { decision: 'approve' | 'reject' }) => api.post(`/api/ops/teams/${team.teamId}/review`, { decision: b.decision, reason: b.decision === 'reject' ? reason : null, version: team.version }), [['ops-teams'], ['ops-tournaments']]);
  const go = (decision: 'approve' | 'reject') => m.mutate({ decision }, { onSuccess: () => { toast.ok(decision === 'approve' ? 'Team approved — captain notified' : 'Team rejected — captain notified'); onClose(); }, onError: (e) => { setErr(e); if (e instanceof ApiError && e.code === 'version_conflict') toast.error(e); } });
  return (
    <Drawer title={team.teamName} onClose={onClose}>
      <dl className="kv"><dt>Tournament</dt><dd>{team.tournamentName}</dd><dt>Department</dt><dd>{team.department}</dd><dt>Captain</dt><dd>{team.captainName}</dd><dt>Submitted</dt><dd>{fmtDateTime(team.submittedAtUtc)}</dd><dt>Status</dt><dd><StatusBadge status={team.status} /></dd></dl>
      <h4>Roster ({team.rosterCount})</h4>
      <DataState q={detail} skeleton={<SkeletonList rows={3} h={32} />}>{(d) => <div className="table-wrap"><table className="t"><tbody>{d.roster.map((p) => <tr key={p.playerId}><td>{p.fullName}</td><td className="mono">{p.enrollmentNo}</td><td>{p.department}</td></tr>)}</tbody></table></div>}</DataState>
      {err instanceof ApiError && <div className="banner warn" role="alert">{err.message}</div>}
      {team.status === 'Pending' && (reject ? <div className="stack"><Field label="Rejection reason (shown to the captain)" error={fieldError(err, 'reason')}>{(p) => <textarea {...p} className="input" rows={3} maxLength={300} value={reason} onChange={(e) => setReason(e.target.value)} />}</Field>
        <div className="row"><Btn variant="ghost" onClick={() => setReject(false)}>Back</Btn><Btn variant="danger" loading={m.isPending} onClick={() => go('reject')}>Confirm rejection</Btn></div></div>
        : <div className="row"><Btn loading={m.isPending} onClick={() => go('approve')}><Check size={14} />Approve</Btn><Btn variant="danger" onClick={() => setReject(true)}><X size={14} />Reject…</Btn></div>)}
    </Drawer>
  );
}
const useDetail = (id: string) => useQuery({ queryKey: ['ops-team', id], queryFn: () => api.get<TeamDetail>(`/api/ops/teams/${id}`) });

// ---------------------------------------------------------------- manage one tournament
export function OpsTournamentPage() {
  const { id = '' } = useParams(); const tours = useOpsTournaments(); const fx = useOpsFixtures(id); const toast = useToast();
  const t: OpsTournament | undefined = tours.data?.find((x) => x.id === id);
  const [tab, setTab] = useState<'fixtures' | 'regen'>('fixtures'); const [modal, setModal] = useState<{ kind: 'schedule' | 'result' | 'live'; f: OpsFixture } | null>(null);
  const gen = useAction(() => api.post<number>(`/api/ops/tournaments/${id}/fixtures/generate`), [['ops-fixtures', id], ['ops-tournaments']]);
  const start = useAction((f: OpsFixture) => api.post(`/api/ops/fixtures/${f.id}/start`, { version: f.version }), [['ops-fixtures', id], ['home']]);
  const withdraw = useAction((b: { teamId: string }) => api.post<WithdrawResult>(`/api/ops/teams/${b.teamId}/withdraw`, { reason: 'Withdrawn by operator' }), [['ops-fixtures', id], ['ops-teams']]);
  const [wd, setWd] = useState<{ teamId: string; name: string } | null>(null);
  if (tours.isPending) return <SkeletonList rows={3} h={80} />;
  if (tours.isError) return <ErrorState error={tours.error} onRetry={() => void tours.refetch()} />;
  if (!t) return <EmptyState title="Tournament not found"><Link to="/ops">Back</Link></EmptyState>;
  const rounds = fx.data ? [...new Set(fx.data.filter((f) => !f.isThirdPlace).map((f) => f.round))].sort((a, b) => a - b) : []; const maxRound = rounds[rounds.length - 1] ?? 1;
  return (
    <>
      <div className="eyebrow" style={{ marginBottom: 8 }}><Link to="/ops">Tournaments</Link> / {t.sportName}</div>
      <Head eyebrow={t.status === 'InProgress' ? 'Matches in play' : t.status} title={t.name}><StatusBadge status={t.status} /></Head>
      {t.status === 'RegistrationClosed' && <div className="banner info" style={{ marginBottom: 20 }}><Shuffle size={18} /><div className="grow"><strong>Ready to draw.</strong> {t.approvedTeams} approved teams will be seeded by registration time (earliest = top seed); byes go to the top seeds.</div>
        <Btn loading={gen.isPending} disabled={t.approvedTeams < 2} onClick={() => gen.mutate(undefined, { onSuccess: (n) => toast.ok(`Generated ${n} fixtures`), onError: toast.error })}>Generate fixtures</Btn></div>}
      {t.status === 'RegistrationOpen' && <div className="banner info" style={{ marginBottom: 20 }}>Registration is open. {t.pendingTeams} team{t.pendingTeams === 1 ? '' : 's'} awaiting review. <Link to="/ops/teams">Review queue →</Link></div>}
      {t.status === 'InProgress' && <Tabs value={tab} onChange={setTab} tabs={[{ id: 'fixtures', label: 'Fixtures' }, { id: 'regen', label: 'Regeneration' }]} />}
      <div style={{ marginTop: 20 }}>
        {tab === 'regen' && t.status === 'InProgress' ? <Regen id={id} /> :
          <DataState q={fx} isEmpty={(d) => d.length === 0} empty={<EmptyState title="No fixtures yet">Close registration, then generate the bracket.</EmptyState>}>
            {(list) => <div className="stack" style={{ gap: 26 }}>{[...rounds, ...(list.some((f) => f.isThirdPlace) ? [-1] : [])].map((r) => (
              <section key={r}><h3 style={{ marginBottom: 10 }}>{r === -1 ? 'Third place' : roundLabel(r, maxRound, false)}</h3>
                <div className="table-wrap"><table className="t cards"><thead><tr><th>Match</th><th>Score</th><th>Status</th><th>When / where</th><th>Actions</th></tr></thead><tbody>
                  {list.filter((f) => (r === -1 ? f.isThirdPlace : f.round === r && !f.isThirdPlace)).map((f) => (
                    <tr key={f.id}><td data-label="Match"><strong>{f.homeTeam ?? 'TBD'}</strong> <span className="muted">vs</span> <strong>{f.awayTeam ?? 'TBD'}</strong></td>
                      <td data-label="Score" className="mono">{f.homeScore ?? '–'} : {f.awayScore ?? '–'}</td><td data-label="Status"><StatusBadge status={f.status} /></td>
                      <td data-label="When">{f.startsAtUtc ? <>{fmtDateTime(f.startsAtUtc)}<div className="muted" style={{ fontSize: 12 }}>{f.venue}</div></> : <span className="muted">Unscheduled</span>}</td>
                      <td data-label="Actions"><div className="row" style={{ gap: 6 }}>
                        {(f.status === 'Ready' || f.status === 'Scheduled') && <Btn size="sm" variant="ghost" onClick={() => setModal({ kind: 'schedule', f })}>Schedule</Btn>}
                        {(f.status === 'Ready' || f.status === 'Scheduled') && <Btn size="sm" variant="lime" loading={start.isPending && start.variables?.id === f.id} onClick={() => start.mutate(f, { onSuccess: () => toast.ok('Match is live'), onError: toast.error })}><Play size={11} />Start</Btn>}
                        {f.status === 'Live' && <Btn size="sm" variant="ghost" onClick={() => setModal({ kind: 'live', f })}>Clock</Btn>}
                        {(f.status === 'Ready' || f.status === 'Scheduled' || f.status === 'Live') && <Btn size="sm" onClick={() => setModal({ kind: 'result', f })}>Result</Btn>}
                        {!isFinished(f.status) && f.homeTeamId && <Btn size="sm" variant="danger" onClick={() => setWd({ teamId: f.homeTeamId!, name: f.homeTeam ?? '' })} aria-label={`Withdraw ${f.homeTeam}`}>W/D home</Btn>}
                        {!isFinished(f.status) && f.awayTeamId && <Btn size="sm" variant="danger" onClick={() => setWd({ teamId: f.awayTeamId!, name: f.awayTeam ?? '' })} aria-label={`Withdraw ${f.awayTeam}`}>W/D away</Btn>}
                      </div></td></tr>))}</tbody></table></div></section>))}</div>}
          </DataState>}
      </div>
      {modal?.kind === 'schedule' && <ScheduleDialog f={modal.f} tid={id} onClose={() => setModal(null)} />}
      {modal?.kind === 'result' && <ResultDialog f={modal.f} tid={id} onClose={() => setModal(null)} />}
      {modal?.kind === 'live' && <LiveDialog f={modal.f} onClose={() => setModal(null)} />}
      {wd && <Dialog title={`Withdraw ${wd.name}?`} onClose={() => setWd(null)} footer={<><Btn variant="ghost" onClick={() => setWd(null)}>Cancel</Btn><Btn variant="danger" loading={withdraw.isPending}
        onClick={() => withdraw.mutate({ teamId: wd.teamId }, { onSuccess: (r) => { toast.ok(`Withdrawn · ${r.walkoversApplied} walkover(s) applied${r.manualWalkoversNeeded ? ` · ${r.manualWalkoversNeeded} need a manual walkover` : ''}`); setWd(null); }, onError: toast.error })}>Withdraw team</Btn></>}>
        <p>Opponents in unplayed matches advance by walkover. Matches whose opponent isn’t decided yet will need a manual walkover once it is.</p></Dialog>}
    </>
  );
}

function ScheduleDialog({ f, tid, onClose }: { f: OpsFixture; tid: string; onClose: () => void }) {
  const toast = useToast(); const [venue, setVenue] = useState(f.venue ?? ''); const [start, setStart] = useState(''); const [mins, setMins] = useState('60'); const [err, setErr] = useState<unknown>(null);
  const m = useAction(() => { const s = localToUtcIso(start); return api.post(`/api/ops/fixtures/${f.id}/schedule`, { venue, startsAtUtc: s, endsAtUtc: new Date(new Date(s).getTime() + Number(mins) * 60000).toISOString(), version: f.version }); }, [['ops-fixtures', tid], ['home'], ['feed']]);
  return (
    <Dialog title={`Schedule: ${f.homeTeam} vs ${f.awayTeam}`} onClose={onClose} footer={<><Btn variant="ghost" onClick={onClose}>Cancel</Btn><Btn loading={m.isPending} disabled={!start || !venue} onClick={() => m.mutate(undefined, { onSuccess: () => { toast.ok('Scheduled — captains notified'); onClose(); }, onError: setErr })}>Save schedule</Btn></>}>
      {err instanceof ApiError && <div className="banner warn" role="alert"><div><strong>{err.code === 'schedule.clash' ? 'Scheduling conflict' : 'Could not schedule'}</strong><br />{err.message}</div></div>}
      <Field label="Venue" error={fieldError(err, 'venue')}>{(p) => <input {...p} className="input" placeholder="Gymnasium Court A" value={venue} onChange={(e) => setVenue(e.target.value)} />}</Field>
      <div className="grid g2"><Field label="Start (campus time)">{(p) => <input {...p} type="datetime-local" className="input" value={start} onChange={(e) => setStart(e.target.value)} />}</Field>
        <Field label="Duration (min)">{(p) => <input {...p} type="number" min={10} max={360} className="input" value={mins} onChange={(e) => setMins(e.target.value)} />}</Field></div>
      <p className="muted" style={{ fontSize: 13 }}>Checks run on save: venue double-booking, a team playing twice at once, and 30 minutes recovery between a team’s matches.</p>
    </Dialog>
  );
}

function ResultDialog({ f, tid, onClose }: { f: OpsFixture; tid: string; onClose: () => void }) {
  const toast = useToast(); const [outcome, setOutcome] = useState('Completed'); const [h, setH] = useState(String(f.homeScore ?? '')); const [a, setA] = useState(String(f.awayScore ?? '')); const [wo, setWo] = useState(f.homeTeamId ?? ''); const [err, setErr] = useState<unknown>(null);
  const m = useAction(() => api.post(`/api/ops/fixtures/${f.id}/result`, { outcome, homeScore: outcome === 'Completed' ? Number(h) : null, awayScore: outcome === 'Completed' ? Number(a) : null, walkoverWinnerId: outcome === 'Walkover' ? wo : null, version: f.version }), [['ops-fixtures', tid], ['ops-tournaments'], ['home'], ['feed']]);
  return (
    <Dialog title={`Result: ${f.homeTeam} vs ${f.awayTeam}`} onClose={onClose} footer={<><Btn variant="ghost" onClick={onClose}>Cancel</Btn>
      <Btn loading={m.isPending} disabled={outcome === 'Completed' && (h === '' || a === '')} onClick={() => m.mutate(undefined, { onSuccess: () => { toast.ok('Result recorded — winner advanced'); onClose(); }, onError: setErr })}>Record result</Btn></>}>
      {err instanceof ApiError && <div className="banner warn" role="alert">{err.code === 'version_conflict' ? 'Someone else updated this match. Close and reopen to see the latest.' : err.message}</div>}
      <Field label="Outcome">{(p) => <select {...p} className="input" value={outcome} onChange={(e) => setOutcome(e.target.value)}><option value="Completed">Played — enter score</option><option value="Walkover">Walkover</option><option value="DoubleForfeit">Double forfeit</option><option value="Abandoned">Abandoned</option></select>}</Field>
      {outcome === 'Completed' && <div className="grid g2"><Field label={f.homeTeam ?? 'Home'}>{(p) => <input {...p} type="number" min={0} className="input mono" value={h} onChange={(e) => setH(e.target.value)} />}</Field><Field label={f.awayTeam ?? 'Away'}>{(p) => <input {...p} type="number" min={0} className="input mono" value={a} onChange={(e) => setA(e.target.value)} />}</Field></div>}
      {outcome === 'Walkover' && <Field label="Winner">{(p) => <select {...p} className="input" value={wo} onChange={(e) => setWo(e.target.value)}><option value={f.homeTeamId ?? ''}>{f.homeTeam}</option><option value={f.awayTeamId ?? ''}>{f.awayTeam}</option></select>}</Field>}
      <p className="muted" style={{ fontSize: 13 }}>Knockout matches can’t end level. The winner is calculated by the server and moves to the next round automatically. Recorded results can’t be overwritten.</p>
    </Dialog>
  );
}

function LiveDialog({ f, onClose }: { f: OpsFixture; onClose: () => void }) {
  const toast = useToast(); const [period, setPeriod] = useState('1st half'); const [clock, setClock] = useState('');
  const m = useAction(() => api.post(`/api/ops/fixtures/${f.id}/live`, { period, clock }), [['home']]);
  return <Dialog title="Live clock" onClose={onClose} footer={<><Btn variant="ghost" onClick={onClose}>Close</Btn><Btn loading={m.isPending} onClick={() => m.mutate(undefined, { onSuccess: () => toast.ok('Live display updated'), onError: toast.error })}>Update display</Btn></>}>
    <Field label="Period">{(p) => <input {...p} className="input" maxLength={30} value={period} onChange={(e) => setPeriod(e.target.value)} />}</Field>
    <Field label="Clock" help="Shown on the public live desk, e.g. 34'">{(p) => <input {...p} className="input mono" maxLength={12} value={clock} onChange={(e) => setClock(e.target.value)} />}</Field></Dialog>;
}

function Regen({ id }: { id: string }) {
  const toast = useToast(); const [prev, setPrev] = useState<RegenerationPreview | null>(null);
  const preview = useAction(() => api.post<RegenerationPreview>(`/api/ops/tournaments/${id}/regeneration/preview`));
  const confirm = useAction(() => api.post<number>(`/api/ops/tournaments/${id}/regeneration/confirm`), [['ops-fixtures', id], ['ops-tournaments']]);
  return (
    <div className="stack"><div className="banner info"><RefreshCw size={18} /><div>Use regeneration after withdrawals or late changes. Completed matches stay as history; eliminated, withdrawn and rejected teams are excluded; no completed pairing is ever replayed.</div></div>
      <div><Btn loading={preview.isPending} onClick={() => preview.mutate(undefined, { onSuccess: setPrev, onError: toast.error })}>Preview new bracket</Btn></div>
      {prev && <div className="grid g2"><div className="card"><h4>Included ({prev.included.length})</h4><ul>{prev.included.map((n) => <li key={n}>{n}</li>)}</ul></div><div className="card"><h4>Excluded ({prev.excluded.length})</h4><ul>{prev.excluded.map((n) => <li key={n}>{n}</li>)}</ul></div>
        <div className="card" style={{ gridColumn: '1 / -1' }}><p>{prev.canConfirm ? <>This will replace all unplayed fixtures with <b>{prev.newFixtures}</b> new ones.</> : prev.reason}</p>
          <Btn disabled={!prev.canConfirm} loading={confirm.isPending} style={{ marginTop: 12 }} onClick={() => confirm.mutate(undefined, { onSuccess: (n) => { toast.ok(`Regenerated ${n} fixtures`); setPrev(null); }, onError: toast.error })}>Confirm regeneration</Btn></div></div>}
    </div>
  );
}
