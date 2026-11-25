import { useMemo, useState } from 'react';
import { Link, useParams, useSearchParams } from 'react-router-dom';
import { ArrowRight, Check, Clock, Grid2x2, ShieldCheck, Trophy, Users } from 'lucide-react';
import { Bracket, FixtureRow, ScoreCard } from '../components/fixtures';
import { Chips, DataState, EmptyState, ErrorState, PageHead, Pager, Segmented, Skeleton, SkeletonList, StatTile, StatusBadge, Tabs, useDebounced } from '../components/ui';
import { fmtDate, fmtDay, fmtTime } from '../lib/format';
import { useFeed, useLeaderboard, usePublicHome, usePublicTeams, useSports, useStandings, useTournament, useTournaments } from '../lib/hooks';
import type { FeedFixture, LeaderboardRow } from '../lib/types';

const pad = (n: number) => String(n).padStart(2, '0');

export function HomePage() {
  const q = usePublicHome();
  const [sport, setSport] = useState('ALL');
  if (q.isError) return <div className="container" style={{ padding: '48px 24px' }}><ErrorState error={q.error} onRetry={() => void q.refetch()} /></div>;
  const d = q.data;
  const sportsInToday = d ? [...new Set(d.today.map((f) => f.sportName))] : [];
  const today = d?.today.filter((f) => sport === 'ALL' || f.sportName === sport) ?? [];
  const maxPoints = Math.max(1, ...(d?.standings.map((s) => s.points) ?? [1]));
  return (
    <>
      <div className="container hero">
        <div>
          <div className="eyebrow row" style={{ gap: 6, marginBottom: 18 }}><ShieldCheck size={13} aria-hidden />Official athletic bulletin</div>
          <h1>The campus board, <span className="outline">live.</span></h1>
          <p className="lede">Fixtures, scores and standings for Bahria University Sports Week. Everything you need, straight from the scorer’s desk — no login needed.</p>
          <div className="row"><Link className="btn" to="/fixtures">Explore fixtures <ArrowRight size={14} /></Link><Link className="btn ghost" to="/leaderboards"><Trophy size={14} />Standings</Link></div>
          {d?.nextUp && <div className="nextup"><Clock size={16} aria-hidden /><span>Next up: {d.nextUp.sportName} — {d.nextUp.homeTeam ?? 'TBD'} vs {d.nextUp.awayTeam ?? 'TBD'} at {fmtTime(d.nextUp.startsAtUtc)}.</span></div>}
        </div>
        <section aria-label="Live desk">
          <div className="livedesk-head eyebrow"><span>● Live desk</span><span>Auto refresh · 5s</span></div>
          {q.isPending ? <SkeletonList rows={2} h={150} /> : d && d.live.length > 0 ? d.live.slice(0, 3).map((f) => <ScoreCard key={f.id} f={f} />) : (
            <div className="empty-dash"><Clock size={14} aria-hidden />No match is live right now.{d?.nextUp ? ` Next kick-off: ${fmtTime(d.nextUp.startsAtUtc)} ${d.nextUp.sportName} — ${d.nextUp.venue ?? 'venue TBC'}.` : ''}</div>)}
        </section>
      </div>

      <div className="band"><div className="container grid g4">
        {q.isPending ? Array.from({ length: 4 }, (_, i) => <Skeleton key={i} h={130} />) : d && <>
          <StatTile eyebrow="Official disciplines" value={pad(d.stats.sportsActive)} label="Sports active" icon={<Grid2x2 size={16} />} />
          <StatTile eyebrow="Roster validation" value={pad(d.stats.teamsApproved)} label="Teams approved" icon={<Users size={16} />} />
          <StatTile eyebrow="Progress meter" value={pad(d.stats.matchesPlayed)} label="Matches played" icon={<Check size={16} />} />
          <StatTile eyebrow="Faculty enrolment" value={pad(d.stats.departments)} label="Departments" icon={<ShieldCheck size={16} />} />
        </>}
      </div></div>

      <div className="container section">
        <div className="grid" style={{ gridTemplateColumns: 'minmax(0,1.6fr) minmax(0,1fr)', gap: 40 }} data-collapse>
          <section>
            <div className="section-head"><div><div className="eyebrow" style={{ marginBottom: 10 }}>Order of play · {d ? fmtDay(d.generatedAtUtc) : ''}</div><h2>Today’s fixtures</h2></div>
              <Chips value={sport} onChange={setSport} options={[{ id: 'ALL', label: 'All' }, ...sportsInToday.map((s) => ({ id: s, label: s }))]} /></div>
            {q.isPending ? <SkeletonList /> : today.length === 0 ? <EmptyState title="No fixtures today">Matches appear here as soon as the operators schedule them.</EmptyState> : <div>{today.map((f) => <FixtureRow key={f.id} f={f} />)}</div>}
          </section>
          <section>
            <div className="section-head"><h2>Latest results</h2></div>
            {q.isPending ? <SkeletonList rows={3} h={120} /> : d && d.latest.length === 0 ? <EmptyState title="No results yet">Final scores will show up here.</EmptyState> : d?.latest.slice(0, 3).map((f) => <ScoreCard key={f.id} f={f} />)}
          </section>
        </div>
      </div>

      <div className="container section">
        <div className="section-head"><div><div className="eyebrow" style={{ marginBottom: 10 }}>Points · 3 per win</div><h2>Department standings</h2></div><Link className="btn ghost" to="/leaderboards">Full table</Link></div>
        {q.isPending ? <SkeletonList rows={5} h={40} /> : d && d.standings.length === 0 ? <EmptyState title="Standings open after the first match">Departments earn 3 points for every win.</EmptyState> :
          <div className="card">{d?.standings.map((s, i) => <LbRow key={s.department} rank={i + 1} row={s} max={maxPoints} />)}</div>}
      </div>

      <div className="container section">
        <PageHead eyebrow="Six disciplines" title="Sports" />
        <div className="grid g3">{(d?.sports ?? []).map((s) => <Link key={s.id} className="card lift sport-tile" to={`/sports/${s.id}`}><div><div className="eyebrow">{s.slug.replace(/-/g, ' ')}</div><h3 style={{ marginTop: 10 }}>{s.name}</h3></div><div className="row between"><span className="muted" style={{ fontSize: 14 }}>{s.description}</span><ArrowRight size={16} /></div></Link>)}</div>
      </div>
      <style>{`@media (max-width: 960px) { [data-collapse] { grid-template-columns: 1fr !important; } }`}</style>
    </>
  );
}

function LbRow({ rank, row, max }: { rank: number; row: LeaderboardRow; max: number }) {
  return (
    <div className="lb"><span className="rk">{pad(rank)}</span>
      <div><div className="row between"><strong>{row.department}</strong><span className="mono" style={{ fontSize: 12 }}>{row.matchesWon} wins · {row.teamsApproved} teams</span></div><div className="bar" style={{ marginTop: 6 }} role="img" aria-label={`${row.points} points`}><i style={{ width: `${(row.points / max) * 100}%` }} /></div></div>
      <span className="mono right" style={{ fontSize: 22, fontWeight: 700 }}>{row.points}<span className="muted" style={{ fontSize: 11 }}> pts</span></span>
    </div>
  );
}

export function SportsPage() {
  const sports = useSports(); const tours = useTournaments();
  return (
    <div className="container section">
      <PageHead eyebrow="Disciplines" title="Sports" />
      <DataState q={sports} isEmpty={(d) => d.length === 0} empty={<EmptyState title="No sports yet">Sports appear once the organisers publish them.</EmptyState>}>
        {(list) => <div className="grid g3">{list.map((s) => {
          const t = tours.data?.items.filter((x) => x.sportId === s.id) ?? [];
          const open = t.some((x) => x.registrationOpen);
          return <Link key={s.id} to={`/sports/${s.id}`} className="card lift sport-tile"><div><div className="row between"><span className="eyebrow">{t.length} tournament{t.length === 1 ? '' : 's'}</span>{open && <span className="badge live"><span className="dot" />Registration open</span>}</div><h3 style={{ marginTop: 12 }}>{s.name}</h3></div><p className="muted">{s.description}</p></Link>;
        })}</div>}
      </DataState>
    </div>
  );
}

export function SportDetailPage() {
  const { id = '' } = useParams(); const sports = useSports(); const tours = useTournaments(); const [tab, setTab] = useState<'tournaments' | 'rules'>('tournaments');
  const sport = sports.data?.find((s) => s.id === id);
  if (sports.isPending) return <div className="container section"><SkeletonList rows={3} /></div>;
  if (sports.isError) return <div className="container section"><ErrorState error={sports.error} onRetry={() => void sports.refetch()} /></div>;
  if (!sport) return <div className="container section"><EmptyState title="Sport not found"><Link to="/sports">Back to sports</Link></EmptyState></div>;
  const items = tours.data?.items.filter((t) => t.sportId === id) ?? [];
  return (
    <div className="container section">
      <PageHead eyebrow="Sport" title={sport.name} />
      <Tabs value={tab} onChange={setTab} tabs={[{ id: 'tournaments', label: 'Tournaments' }, { id: 'rules', label: 'Rules' }]} />
      <div style={{ marginTop: 24 }}>
        {tab === 'rules' ? <div className="card"><p>{sport.description ?? 'Rules are published by the organising committee.'}</p>{items[0] && <p className="muted" style={{ marginTop: 12 }}>Roster: {items[0].minPlayers}–{items[0].maxPlayers} players per team.</p>}</div> :
          <DataState q={tours} skeleton={<SkeletonList rows={2} h={120} />} isEmpty={() => items.length === 0} empty={<EmptyState title="No tournaments yet">Check back when registration opens.</EmptyState>}>
            {() => <div className="grid g2">{items.map((t) => <TournamentCard key={t.id} t={t} />)}</div>}
          </DataState>}
      </div>
    </div>
  );
}

function TournamentCard({ t }: { t: import('../lib/types').PublicTournament }) {
  const pct = t.teamLimit ? Math.min(100, (t.approvedTeams / t.teamLimit) * 100) : 0;
  const prog = t.fixturesTotal ? Math.round((t.fixturesCompleted / t.fixturesTotal) * 100) : 0;
  return (
    <Link to={`/tournaments/${t.id}`} className="card lift stack">
      <div className="row between"><span className="eyebrow">{t.sportName}</span><StatusBadge status={t.status} /></div>
      <h3>{t.name}</h3>
      <div className="muted" style={{ fontSize: 14 }}>{fmtDate(t.startsAtUtc)} → {fmtDate(t.endsAtUtc)}</div>
      {t.teamLimit && <div><div className="row between eyebrow" style={{ marginBottom: 6 }}><span>Teams</span><span>{t.approvedTeams}/{t.teamLimit}</span></div><div className="bar"><i style={{ width: `${pct}%` }} /></div></div>}
      {t.fixturesTotal > 0 && <div><div className="row between eyebrow" style={{ marginBottom: 6 }}><span>Progress</span><span>{t.fixturesCompleted}/{t.fixturesTotal} matches</span></div><div className="bar"><i style={{ width: `${prog}%` }} /></div></div>}
    </Link>
  );
}

export function TournamentsPage() {
  const [status, setStatus] = useState('');
  const q = useTournaments(status || undefined);
  return (
    <div className="container section">
      <PageHead eyebrow="Competitions" title="Tournaments"><Chips value={status} onChange={setStatus} options={[{ id: '', label: 'All' }, { id: 'RegistrationOpen', label: 'Registration open' }, { id: 'InProgress', label: 'In progress' }, { id: 'Completed', label: 'Completed' }]} /></PageHead>
      <DataState q={q} skeleton={<div className="grid g2"><Skeleton h={180} /><Skeleton h={180} /></div>} isEmpty={(d) => d.items.length === 0} empty={<EmptyState title="No tournaments match">Try another filter.</EmptyState>}>
        {(d) => <div className="grid g2">{d.items.map((t) => <TournamentCard key={t.id} t={t} />)}</div>}
      </DataState>
    </div>
  );
}

export function TournamentDetailPage() {
  const { id = '' } = useParams(); const t = useTournament(id);
  const [tab, setTab] = useState<'overview' | 'fixtures' | 'standings'>('overview'); const [view, setView] = useState<'list' | 'bracket'>('bracket');
  const feed = useFeed({ tournamentId: id }, true); const st = useStandings(id);
  if (t.isPending) return <div className="container section"><SkeletonList rows={3} h={90} /></div>;
  if (t.isError) return <div className="container section"><ErrorState error={t.error} onRetry={() => void t.refetch()} /></div>;
  const d = t.data; const fx = feed.data?.items ?? []; const maxRound = Math.max(1, ...fx.map((f) => f.round));
  const next = fx.filter((f) => f.status === 'Scheduled' || f.status === 'Ready').sort((a, b) => (a.startsAtUtc ?? '9').localeCompare(b.startsAtUtc ?? '9'))[0];
  const pct = d.fixturesTotal ? Math.round((d.fixturesCompleted / d.fixturesTotal) * 100) : 0;
  return (
    <div className="container section">
      <div className="eyebrow" style={{ marginBottom: 12 }}><Link to="/tournaments">Tournaments</Link> / {d.sportName}</div>
      <div className="row between" style={{ marginBottom: 20 }}><h2>{d.name}</h2><StatusBadge status={d.status} /></div>
      <Tabs value={tab} onChange={setTab} tabs={[{ id: 'overview', label: 'Overview' }, { id: 'fixtures', label: 'Fixtures' }, { id: 'standings', label: 'Standings & teams' }]} />
      <div style={{ marginTop: 24 }}>
        {tab === 'overview' && <div className="stack">
          <div className="grid g3">
            <StatTile eyebrow="Dates" value={fmtDate(d.startsAtUtc).slice(0, 6)} label={`to ${fmtDate(d.endsAtUtc).slice(0, 6)}`} />
            <StatTile eyebrow="Teams" value={pad(d.approvedTeams)} label={d.teamLimit ? `of ${d.teamLimit} places` : 'approved'} />
            <StatTile eyebrow="Progress" value={`${pct}%`} label={`${d.fixturesCompleted} of ${d.fixturesTotal} matches`} />
          </div>
          {d.registrationOpen && <div className="banner info"><Users size={18} aria-hidden /><div><strong>Registration is open.</strong> Captains can register a team of {d.minPlayers}–{d.maxPlayers} players. <Link to="/captain/teams">Register a team →</Link></div></div>}
          {next && <div className="card"><div className="eyebrow">Next match</div><div style={{ marginTop: 8 }}><FixtureRow f={next} maxRound={maxRound} /></div></div>}
          {fx.length > 0 && <div><h3 style={{ marginBottom: 12 }}>Bracket preview</h3><Bracket fixtures={fx} /></div>}
        </div>}
        {tab === 'fixtures' && <div className="stack">
          <Segmented value={view} onChange={setView} options={[{ id: 'bracket', label: 'Bracket' }, { id: 'list', label: 'List' }]} />
          <DataState q={feed} isEmpty={(x) => x.items.length === 0} empty={<EmptyState title="Fixtures not published yet">The bracket appears once registration closes and the operators generate it.</EmptyState>}>
            {(x) => view === 'bracket' ? <Bracket fixtures={x.items} /> : <FixtureGroups fixtures={x.items} maxRound={maxRound} />}
          </DataState>
        </div>}
        {tab === 'standings' && <DataState q={st} isEmpty={(r) => r.length === 0} empty={<EmptyState title="No approved teams yet" />}>
          {(rows) => <div className="table-wrap"><table className="t cards"><thead><tr><th>#</th><th>Team</th><th>Dept</th><th>P</th><th>W</th><th>L</th></tr></thead>
            <tbody>{rows.map((r, i) => <tr key={r.team}><td data-label="#">{i + 1}</td><td data-label="Team"><strong>{r.team}</strong></td><td data-label="Dept">{r.department}</td><td data-label="P" className="mono">{r.played}</td><td data-label="W" className="mono">{r.wins}</td><td data-label="L" className="mono">{r.losses}</td></tr>)}</tbody></table></div>}
        </DataState>}
      </div>
    </div>
  );
}

function FixtureGroups({ fixtures, maxRound }: { fixtures: FeedFixture[]; maxRound: number }) {
  const groups = useMemo(() => {
    const m = new Map<string, FeedFixture[]>();
    [...fixtures].sort((a, b) => (a.startsAtUtc ?? '9').localeCompare(b.startsAtUtc ?? '9') || a.round - b.round).forEach((f) => { const k = fmtDay(f.startsAtUtc); m.set(k, [...(m.get(k) ?? []), f]); });
    return [...m.entries()];
  }, [fixtures]);
  return <div className="stack" style={{ gap: 28 }}>{groups.map(([day, list]) => <section key={day}><h3 style={{ marginBottom: 6 }}>{day}</h3>{list.map((f) => <FixtureRow key={f.id} f={f} maxRound={maxRound} />)}</section>)}</div>;
}

export function FixturesPage() {
  const [sp, setSp] = useSearchParams();
  const status = sp.get('status') ?? ''; const sportId = sp.get('sportId') ?? ''; const day = sp.get('day') ?? '';
  const sports = useSports(); const q = useFeed({ status: status || undefined, sportId: sportId || undefined, day: day || undefined }, true);
  const set = (k: string, v: string) => { const n = new URLSearchParams(sp); if (v) n.set(k, v); else n.delete(k); setSp(n, { replace: true }); };
  return (
    <div className="container section">
      <PageHead eyebrow="Order of play" title="Fixtures" />
      <div className="row" style={{ marginBottom: 20, gap: 14 }}>
        <Chips value={status} onChange={(v) => set('status', v)} options={[{ id: '', label: 'All' }, { id: 'Live', label: 'Live' }, { id: 'Scheduled', label: 'Upcoming' }, { id: 'Completed', label: 'Results' }]} />
        <select className="input" style={{ width: 'auto' }} aria-label="Sport" value={sportId} onChange={(e) => set('sportId', e.target.value)}><option value="">All sports</option>{sports.data?.map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}</select>
        <input className="input" style={{ width: 'auto' }} type="date" aria-label="Day" value={day} onChange={(e) => set('day', e.target.value)} />
      </div>
      <DataState q={q} isEmpty={(d) => d.items.length === 0} empty={<EmptyState title="No fixtures match">Try clearing a filter.</EmptyState>}>
        {(d) => <FixtureGroups fixtures={d.items} maxRound={Math.max(1, ...d.items.map((f) => f.round))} />}
      </DataState>
    </div>
  );
}

export function LeaderboardsPage() {
  const q = useLeaderboard();
  return (
    <div className="container section">
      <PageHead eyebrow="3 points per win" title="Leaderboards" />
      <DataState q={q} isEmpty={(d) => d.length === 0} empty={<EmptyState title="Standings open after the first match" />}>
        {(rows) => { const max = Math.max(1, ...rows.map((r) => r.points)); return <div className="card">{rows.map((r, i) => <LbRow key={r.department} rank={i + 1} row={r} max={max} />)}</div>; }}
      </DataState>
    </div>
  );
}

export function TeamsDirectoryPage() {
  const [s, setS] = useState(''); const [page, setPage] = useState(1); const search = useDebounced(s);
  const q = usePublicTeams(search, page);
  return (
    <div className="container section">
      <PageHead eyebrow="Approved teams" title="Teams"><input className="input" style={{ maxWidth: 280 }} placeholder="Search team or department" aria-label="Search teams" value={s} onChange={(e) => { setS(e.target.value); setPage(1); }} /></PageHead>
      <p className="muted" style={{ marginBottom: 20, fontSize: 14 }}>Player details are private.</p>
      <DataState q={q} isEmpty={(d) => d.items.length === 0} empty={<EmptyState title="No teams found" />}>
        {(d) => <><div className="grid g3">{d.items.map((t) => <div className="card" key={t.id}><div className="eyebrow">{t.department}</div><h3 style={{ margin: '8px 0' }}>{t.name}</h3><p className="muted" style={{ fontSize: 14 }}>{t.sportName} · {t.tournamentName}</p></div>)}</div><Pager page={d.page} totalPages={d.totalPages} total={d.totalCount} onPage={setPage} /></>}
      </DataState>
    </div>
  );
}

