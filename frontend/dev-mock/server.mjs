// DEV ONLY — a tiny in-memory stand-in for the ASP.NET API so the UI can be explored before Supabase is configured.
// It mimics the response shapes in docs/API.md. It is NOT the source of truth for business rules (those live in the C# backend).
import http from 'node:http';
import { randomUUID as uid } from 'node:crypto';

const PORT = Number(process.env.PORT ?? 5000);
const now = Date.now(); const H = 3600e3;
const at = (h) => new Date(now + h * H).toISOString();
const sports = ['Futsal', 'Cricket', 'Tug of War', 'Chess', 'Badminton', 'Table Tennis'].map((name, i) => ({ id: uid(), name, slug: name.toLowerCase().replace(/ /g, '-'), description: ['5-a-side indoor football', 'Knockout cricket', '8 vs 8 rope pulling', 'Team chess', 'Team badminton', 'Team table tennis'][i], min: [5, 11, 8, 3, 2, 2][i], max: [10, 15, 10, 5, 4, 4][i] }));
const S = (n) => sports.find((s) => s.name === n);
const tour = (name, sport, status, extra = {}) => ({ id: uid(), name, sportId: S(sport).id, sportName: sport, status, registrationOpen: status === 'RegistrationOpen', startsAtUtc: at(-24), endsAtUtc: at(96), timezone: 'Asia/Karachi', teamLimit: 16, thirdPlace: true, ...extra });
const T = { fut: tour('Futsal — Sports Week 2026', 'Futsal', 'InProgress'), cri: tour('Cricket — Sports Week 2026', 'Cricket', 'RegistrationOpen', { teamLimit: 8 }), bad: tour('Badminton — Sports Week 2026', 'Badminton', 'RegistrationClosed', { teamLimit: 8 }), chess: tour('Chess — Sports Week 2026', 'Chess', 'Completed') };
const tournaments = Object.values(T);
const depts = ['CS', 'EE', 'BBA', 'SE', 'MGT', 'AI', 'CE', 'ME'];
const people = ['Hamza Khan', 'Sara Malik', 'Ali Raza', 'Noor Fatima', 'Bilal Ahmed', 'Hira Shah', 'Usman Tariq', 'Zainab Ali'];
const users = [
  { id: 'u-admin', displayName: 'Admin User', email: 'admin@gmail.com', role: 'Admin', emailVerified: true, isActive: true, roleSource: 'allowlist:admin' },
  { id: 'u-scorer', displayName: 'Scorer Desk', email: 'scorer@gmail.com', role: 'Scorer', emailVerified: true, isActive: true, roleSource: 'allowlist:scorer' },
  { id: 'u-captain', displayName: 'Hamza Khan', email: 'hamza.khan@gmail.com', role: 'Captain', emailVerified: true, isActive: true, roleSource: 'default:captain' },
  ...people.slice(1).map((n, i) => ({ id: `u-c${i}`, displayName: n, email: `${n.toLowerCase().replace(' ', '.')}@gmail.com`, role: 'Captain', emailVerified: i !== 4, isActive: true, roleSource: i === 4 ? 'unverified:captain' : 'default:captain' })),
].map((u, i) => ({ ...u, googleSubject: `1089${i}37721`, lastSignInAtUtc: at(-i * 3 - 1), createdAtUtc: at(-24 * (i + 3)) }));
const teamNames = ['Cyberstrikers', 'Voltrunners', 'Smashers', 'Codeshuttles', 'Management Kings', 'SE Strikers', 'Neural Nine', 'Iron Foxes', 'Bridge Builders', 'Gear Heads'];
let teams = []; let players = [];
const mkTeam = (name, ti, di, status, ownerIdx, hoursAgo) => {
  const t = { id: uid(), name, tournamentId: ti.id, department: depts[di % depts.length], status, ownerId: users[2 + (ownerIdx % 7)].id, version: 1, createdAtUtc: at(-hoursAgo), rejectionReason: status === 'Rejected' ? 'Roster has only 3 verified students.' : null };
  teams.push(t);
  for (let p = 0; p < (ti === T.fut ? 5 : 3); p++) players.push({ id: uid(), teamId: t.id, fullName: `${['Ahmed', 'Fatima', 'Omar', 'Ayesha', 'Hassan', 'Maryam'][p % 6]} ${['Khan', 'Siddiqui', 'Butt', 'Qureshi'][(di + p) % 4]}`, enrollmentNo: `01-1342${10 + di}-${String(1000 + teams.length * 10 + p)}`, department: t.department, createdAtUtc: t.createdAtUtc });
  return t;
};
const futTeams = teamNames.slice(0, 8).map((n, i) => mkTeam(n, T.fut, i, 'Approved', i, 200 - i));
teamNames.slice(0, 5).forEach((n, i) => mkTeam(`${n} XI`, T.cri, i + 1, i < 2 ? 'Approved' : 'Pending', i, 30 + i * 20));
mkTeam('Late Entry', T.cri, 3, 'Rejected', 2, 60); mkTeam('Quit FC', T.fut, 5, 'Withdrawn', 1, 150);
teamNames.slice(0, 4).forEach((n, i) => mkTeam(`${n} Shuttle`, T.bad, i, 'Approved', i, 90));

// Futsal bracket: 8 teams → 7 fixtures + third place
let fixtures = [];
const fx = (round, slot, h, a, extra = {}) => { const f = { id: uid(), tournamentId: T.fut.id, round, slot, isThirdPlace: false, status: 'Pending', homeTeamId: h?.id ?? null, awayTeamId: a?.id ?? null, homeScore: null, awayScore: null, winnerTeamId: null, venue: null, startsAtUtc: null, endsAtUtc: null, version: 1, livePeriod: null, liveClock: null, ...extra }; fixtures.push(f); return f; };
const order = [0, 7, 3, 4, 1, 6, 2, 5];
const r1 = [0, 1, 2, 3].map((s) => { const h = futTeams[order[s * 2]], a = futTeams[order[s * 2 + 1]]; return [h, a]; });
const done = (h, a, hs, as, hrs, v) => fx(1, 0, h, a, { status: 'Completed', homeScore: hs, awayScore: as, winnerTeamId: hs > as ? h.id : a.id, venue: v, startsAtUtc: at(hrs), endsAtUtc: at(hrs + 1) });
const m1 = done(...r1[0], 4, 1, -20, 'Gymnasium Court A'); m1.slot = 1;
const m2 = done(...r1[1], 2, 3, -19, 'Gymnasium Court B'); m2.slot = 2;
const m3 = fx(1, 3, ...r1[2], { status: 'Live', livePeriod: '2nd half', liveClock: "34'", homeScore: 2, awayScore: 1, venue: 'Gymnasium Court A', startsAtUtc: at(-0.6), endsAtUtc: at(0.4) });
const m4 = fx(1, 4, ...r1[3], { status: 'Scheduled', venue: 'Gymnasium Court B', startsAtUtc: at(1.5), endsAtUtc: at(2.5) });
const sf1 = fx(2, 1, futTeams[order[0]], futTeams[order[3]] === undefined ? null : futTeams.find((t) => t.id === m2.winnerTeamId));
sf1.homeTeamId = m1.winnerTeamId; sf1.awayTeamId = m2.winnerTeamId; sf1.status = 'Scheduled'; sf1.venue = 'Main Hall'; sf1.startsAtUtc = at(5); sf1.endsAtUtc = at(6);
fx(2, 2, null, null); fx(3, 1, null, null);
fx(3, 2, null, null, { isThirdPlace: true });
fixtures.forEach((f, i) => { if (f.round === 1 && f.slot === 0) f.slot = i + 1; });

const notes = [
  { id: uid(), type: 'team_approved', title: 'Cyberstrikers was approved', body: "You're in. Fixtures will be published after registration closes.", readAtUtc: null, createdAtUtc: at(-120) },
  { id: uid(), type: 'fixture_scheduled', title: 'Match scheduled', body: 'Main Hall · 06:00', readAtUtc: null, createdAtUtc: at(-3) },
  { id: uid(), type: 'match_result', title: 'Match result recorded', body: 'Final score 4–1.', readAtUtc: at(-18), createdAtUtc: at(-19) },
];
const audit = [
  ['admin.user.viewed', 'profile', 'u-captain'], ['admin.export.users', 'export', null], ['ops.result.recorded', 'fixture', m1.id], ['auth.role_changed', 'profile', 'u-scorer'], ['ops.team.approved', 'team', futTeams[0].id], ['auth.profile_created', 'profile', 'u-captain'],
].map(([action, entityType, entityId], i) => ({ id: uid(), occurredAtUtc: at(-i * 2 - 0.5), actorId: 'u-admin', actorName: i === 2 || i === 4 ? 'Scorer Desk' : 'Admin User', actorRole: i === 2 || i === 4 ? 'Scorer' : 'Admin', action, entityType, entityId, correlationId: uid().replace(/-/g, '').slice(0, 16), result: 'success', metadataJson: JSON.stringify({ rows: 12, sensitive: false }) }));

// ---------- helpers
const tname = (id) => teams.find((t) => t.id === id);
const feed = (f) => { const h = tname(f.homeTeamId), a = tname(f.awayTeamId), t = tournaments.find((x) => x.id === f.tournamentId); return { id: f.id, tournamentId: f.tournamentId, tournamentName: t.name, sportName: t.sportName, round: f.round, slot: f.slot, isThirdPlace: f.isThirdPlace, status: f.status, homeTeam: h?.name ?? null, homeDepartment: h?.department ?? null, awayTeam: a?.name ?? null, awayDepartment: a?.department ?? null, homeScore: f.homeScore, awayScore: f.awayScore, winnerTeam: tname(f.winnerTeamId)?.name ?? null, venue: f.venue, startsAtUtc: f.startsAtUtc, livePeriod: f.livePeriod, liveClock: f.liveClock }; };
const publicFixtures = () => fixtures.filter((f) => tournaments.find((t) => t.id === f.tournamentId).status !== 'Draft');
const paged = (arr, q) => { const page = Math.max(1, +q.get('page') || 1), size = Math.min(100, +q.get('pageSize') || 25); return { items: arr.slice((page - 1) * size, page * size), page, pageSize: size, totalCount: arr.length, totalPages: Math.ceil(arr.length / size) }; };
const mask = (e) => { const i = e.lastIndexOf('@'); return `${e.slice(0, Math.min(2, i))}***${e.slice(i)}`; };
const maskEn = (e) => `****-${e.slice(-4)}`;
const pubT = (t) => ({ id: t.id, name: t.name, sportId: t.sportId, sportName: t.sportName, status: t.status, registrationOpen: t.registrationOpen, startsAtUtc: t.startsAtUtc, endsAtUtc: t.endsAtUtc, timezone: t.timezone, approvedTeams: teams.filter((x) => x.tournamentId === t.id && x.status === 'Approved').length, teamLimit: t.teamLimit, fixturesTotal: fixtures.filter((f) => f.tournamentId === t.id).length, fixturesCompleted: fixtures.filter((f) => f.tournamentId === t.id && ['Completed', 'Walkover'].includes(f.status)).length, minPlayers: S(t.sportName).min, maxPlayers: S(t.sportName).max });
const leaderboard = () => { const m = {}; teams.filter((t) => t.status === 'Approved').forEach((t) => { m[t.department] ??= { department: t.department, points: 0, matchesWon: 0, teamsApproved: 0 }; m[t.department].teamsApproved++; }); fixtures.filter((f) => ['Completed', 'Walkover'].includes(f.status)).forEach((f) => { const d = tname(f.winnerTeamId)?.department; if (d) { m[d].points += 3; m[d].matchesWon++; } }); return Object.values(m).sort((a, b) => b.points - a.points || a.department.localeCompare(b.department)); };
const teamSummary = (t) => { const o = users.find((u) => u.id === t.ownerId), tr = tournaments.find((x) => x.id === t.tournamentId); return { teamId: t.id, teamName: t.name, department: t.department, status: t.status, sportName: tr.sportName, tournamentName: tr.name, tournamentId: tr.id, captainName: o.displayName, captainEmail: mask(o.email), rosterCount: players.filter((p) => p.teamId === t.id).length, submittedAtUtc: t.createdAtUtc, rejectionReason: t.rejectionReason, version: t.version }; };
const audited = (action, entityType, entityId, actor) => audit.unshift({ id: uid(), occurredAtUtc: new Date().toISOString(), actorId: actor.id, actorName: actor.displayName, actorRole: actor.role, action, entityType, entityId, correlationId: uid().replace(/-/g, '').slice(0, 16), result: 'success', metadataJson: null });

// ---------- http
const send = (res, status, body, headers = {}) => { res.writeHead(status, { 'Content-Type': 'application/json', ...headers }); res.end(body === undefined ? '' : JSON.stringify(body)); };
const err = (res, status, code, message, fieldErrors = null) => send(res, status, { code, message, fieldErrors, correlationId: uid().replace(/-/g, '') });
const readBody = (req) => new Promise((r) => { let d = ''; req.on('data', (c) => (d += c)); req.on('end', () => { try { r(d ? JSON.parse(d) : {}); } catch { r({}); } }); });

http.createServer(async (req, res) => {
  const origin = req.headers.origin ?? '*';
  const cors = { 'Access-Control-Allow-Origin': origin, 'Access-Control-Allow-Headers': 'Authorization, Content-Type, Idempotency-Key, X-Correlation-Id', 'Access-Control-Allow-Methods': 'GET, POST, OPTIONS', 'Access-Control-Expose-Headers': 'Content-Disposition, Retry-After, X-Correlation-Id' };
  for (const [k, v] of Object.entries(cors)) res.setHeader(k, v);
  if (req.method === 'OPTIONS') return send(res, 204);
  const url = new URL(req.url, `http://${req.headers.host}`); const q = url.searchParams; const path = url.pathname; const m = req.method;
  const tok = (req.headers.authorization ?? '').replace('Bearer ', '');
  const me = { 'mock-admin': users[0], 'mock-scorer': users[1], 'mock-captain': users[2] }[tok];
  const need = (...roles) => { if (!me) { err(res, 401, 'unauthorized', 'Your session has expired. Please sign in again.'); return false; } if (!roles.includes(me.role)) { err(res, 403, 'forbidden', 'You do not have access to this.'); return false; } return true; };
  const body = m === 'POST' ? await readBody(req) : {};
  let g;
  try {
    if (path === '/health') return send(res, 200, { status: 'Healthy' });
    // ---- public
    if (path === '/api/public/sports') return send(res, 200, sports.map(({ min, max, ...s }) => s));
    if (path === '/api/public/home') {
      const pf = publicFixtures().map(feed); const today = pf.filter((f) => f.startsAtUtc && new Date(f.startsAtUtc).toDateString() === new Date().toDateString()).sort((a, b) => a.startsAtUtc.localeCompare(b.startsAtUtc));
      return send(res, 200, { generatedAtUtc: new Date().toISOString(), stats: { sportsActive: 6, teamsApproved: teams.filter((t) => t.status === 'Approved').length, matchesPlayed: fixtures.filter((f) => ['Completed', 'Walkover'].includes(f.status)).length, departments: new Set(teams.filter((t) => t.status === 'Approved').map((t) => t.department)).size, liveNow: pf.filter((f) => f.status === 'Live').length },
        live: pf.filter((f) => f.status === 'Live'), today, latest: pf.filter((f) => f.status === 'Completed').slice(-3).reverse(), nextUp: pf.filter((f) => f.status === 'Scheduled' && new Date(f.startsAtUtc) > new Date()).sort((a, b) => a.startsAtUtc.localeCompare(b.startsAtUtc))[0] ?? null, standings: leaderboard().slice(0, 5), sports: sports.map(({ min, max, ...s }) => s) });
    }
    if (path === '/api/public/fixtures') { let a = publicFixtures().map(feed); if (q.get('status')) a = a.filter((f) => f.status === q.get('status') || (q.get('status') === 'Scheduled' && f.status === 'Ready')); if (q.get('tournamentId')) a = a.filter((f) => f.tournamentId === q.get('tournamentId')); if (q.get('sportId')) a = a.filter((f) => sports.find((s) => s.id === q.get('sportId'))?.name === f.sportName); return send(res, 200, paged(a, q)); }
    if (path === '/api/public/tournaments') { let a = tournaments.filter((t) => t.status !== 'Draft').map(pubT); if (q.get('status')) a = a.filter((t) => t.status === q.get('status')); return send(res, 200, paged(a, q)); }
    if ((g = path.match(/^\/api\/public\/tournaments\/([^/]+)(\/standings)?$/))) { const t = tournaments.find((x) => x.id === g[1]); if (!t) return err(res, 404, 'not_found', 'Tournament not found.');
      if (g[2]) return send(res, 200, teams.filter((x) => x.tournamentId === t.id && x.status === 'Approved').map((x) => { const pl = fixtures.filter((f) => ['Completed', 'Walkover'].includes(f.status) && [f.homeTeamId, f.awayTeamId].includes(x.id)); const w = pl.filter((f) => f.winnerTeamId === x.id).length; return { team: x.name, department: x.department, played: pl.length, wins: w, losses: pl.length - w }; }).sort((a, b) => b.wins - a.wins || a.team.localeCompare(b.team)));
      return send(res, 200, pubT(t)); }
    if (path === '/api/public/teams') { let a = teams.filter((t) => t.status === 'Approved').map((t) => ({ id: t.id, name: t.name, department: t.department, sportName: tournaments.find((x) => x.id === t.tournamentId).sportName, tournamentName: tournaments.find((x) => x.id === t.tournamentId).name })); const s = q.get('search')?.toLowerCase(); if (s) a = a.filter((t) => t.name.toLowerCase().includes(s) || t.department.toLowerCase().includes(s)); return send(res, 200, paged(a, q)); }
    if (path === '/api/public/leaderboards/overall') return send(res, 200, leaderboard());
    // ---- identity
    if (path === '/api/me') { if (!need('Captain', 'Scorer', 'Admin')) return; return send(res, 200, { id: me.id, email: me.email, displayName: me.displayName, role: me.role, emailVerified: me.emailVerified }); }
    // ---- captain
    if (path.startsWith('/api/captain')) {
      if (!need('Captain')) return;
      if (path === '/api/captain/teams' && m === 'GET') return send(res, 200, teams.filter((t) => t.ownerId === me.id).map((t) => { const tr = tournaments.find((x) => x.id === t.tournamentId); return { teamId: t.id, teamName: t.name, department: t.department, status: t.status, rejectionReason: t.rejectionReason, tournamentId: tr.id, tournamentName: tr.name, sportName: tr.sportName, rosterCount: players.filter((p) => p.teamId === t.id).length, submittedAtUtc: t.createdAtUtc }; }));
      if (path === '/api/captain/notifications') return send(res, 200, notes);
      if (path === '/api/captain/notifications/read-all') { notes.forEach((n) => (n.readAtUtc ??= new Date().toISOString())); return send(res, 200, { marked: 1 }); }
      if (path === '/api/captain/fixtures') { const mine = new Set(teams.filter((t) => t.ownerId === me.id).map((t) => t.id)); return send(res, 200, publicFixtures().filter((f) => mine.has(f.homeTeamId) || mine.has(f.awayTeamId)).map(feed)); }
      if ((g = path.match(/^\/api\/captain\/tournaments\/([^/]+)\/teams$/))) {
        const t = tournaments.find((x) => x.id === g[1]); if (!t) return err(res, 404, 'not_found', 'Tournament not found.'); if (!t.registrationOpen) return err(res, 422, 'registration.closed', 'Registration is not open for this tournament.');
        const sp = S(t.sportName), fe = {}; if (!body.teamName || body.teamName.trim().length < 3) fe.teamName = ['Team name must be 3–60 characters.']; if ((body.players?.length ?? 0) < sp.min || body.players.length > sp.max) fe.players = [`This sport requires ${sp.min}–${sp.max} players.`];
        if (Object.keys(fe).length) return err(res, 400, 'validation_failed', 'One or more fields are invalid.', fe);
        if (teams.some((x) => x.tournamentId === t.id && x.name.toLowerCase() === body.teamName.trim().toLowerCase())) return err(res, 409, 'team.duplicate_name', 'A team with this name is already registered in this tournament.');
        const nt = mkTeam(body.teamName.trim(), t, 1, 'Pending', 0, 0); nt.ownerId = me.id; players = players.filter((p) => p.teamId !== nt.id); body.players.forEach((p) => players.push({ id: uid(), teamId: nt.id, ...p, createdAtUtc: nt.createdAtUtc })); return send(res, 201, { teamId: nt.id });
      }
      if ((g = path.match(/^\/api\/captain\/teams\/([^/]+)\/withdraw$/))) { const t = teams.find((x) => x.id === g[1] && x.ownerId === me.id); if (!t) return err(res, 403, 'forbidden', 'You cannot withdraw this team.'); t.status = 'Withdrawn'; return send(res, 200, { walkoversApplied: 0, manualWalkoversNeeded: 0 }); }
    }
    // ---- operator desk
    if (path.startsWith('/api/ops')) {
      if (!need('Scorer')) return;
      if (path === '/api/ops/tournaments' && m === 'GET') return send(res, 200, tournaments.map((t) => ({ id: t.id, name: t.name, sportName: t.sportName, status: t.status, teamLimit: t.teamLimit, approvedTeams: teams.filter((x) => x.tournamentId === t.id && x.status === 'Approved').length, pendingTeams: teams.filter((x) => x.tournamentId === t.id && x.status === 'Pending').length, fixturesTotal: fixtures.filter((f) => f.tournamentId === t.id).length, fixturesResolved: fixtures.filter((f) => ['Completed', 'Walkover', 'Bye'].includes(f.status) && f.tournamentId === t.id).length })));
      if (path === '/api/ops/tournaments' && m === 'POST') { if (!body.name || body.name.trim().length < 3) return err(res, 400, 'validation_failed', 'One or more fields are invalid.', { name: ['Name must be 3–80 characters.'] }); const t = tour(body.name, sports.find((s) => s.id === body.sportId).name, 'Draft', { teamLimit: body.teamLimit }); tournaments.push(t); return send(res, 201, { id: t.id }); }
      if ((g = path.match(/^\/api\/ops\/tournaments\/([^/]+)\/status$/))) { const t = tournaments.find((x) => x.id === g[1]); t.status = body.status; t.registrationOpen = body.status === 'RegistrationOpen'; return send(res, 200, body.status); }
      if ((g = path.match(/^\/api\/ops\/tournaments\/([^/]+)\/fixtures$/))) return send(res, 200, fixtures.filter((f) => f.tournamentId === g[1]).sort((a, b) => a.round - b.round || a.slot - b.slot).map((f) => ({ ...f, homeTeam: tname(f.homeTeamId)?.name ?? null, awayTeam: tname(f.awayTeamId)?.name ?? null })));
      if ((g = path.match(/^\/api\/ops\/tournaments\/([^/]+)\/fixtures\/generate$/))) return err(res, 409, 'fixtures.exist', 'Mock: generation is implemented by the real backend (tested in the C# suite).');
      if (path.endsWith('/regeneration/preview')) return send(res, 200, { included: futTeams.slice(0, 5).map((t) => t.name), excluded: ['Quit FC (Withdrawn)', 'Iron Foxes (Eliminated)'], newFixtures: 4, canConfirm: true, reason: null });
      if (path.endsWith('/regeneration/confirm')) return send(res, 200, 4);
      if (path === '/api/ops/teams') { let a = teams.map(teamSummary); if (q.get('status')) a = a.filter((t) => t.status === q.get('status')); const s = q.get('search')?.toLowerCase(); if (s) a = a.filter((t) => t.teamName.toLowerCase().includes(s)); return send(res, 200, paged(a, q)); }
      if ((g = path.match(/^\/api\/ops\/teams\/([^/]+)(\/review)?$/))) { const t = teams.find((x) => x.id === g[1]); if (!t) return err(res, 404, 'not_found', 'Team not found.');
        if (!g[2]) return send(res, 200, { team: teamSummary(t), roster: players.filter((p) => p.teamId === t.id).map((p) => ({ playerId: p.id, fullName: p.fullName, enrollmentNo: maskEn(p.enrollmentNo), department: p.department })) });
        if (t.version !== body.version) return err(res, 409, 'version_conflict', 'Someone else changed this team. Reload and try again.'); if (body.decision === 'reject' && (body.reason ?? '').length < 5) return err(res, 400, 'validation_failed', 'One or more fields are invalid.', { reason: ['A rejection reason of 5–300 characters is required.'] });
        t.status = body.decision === 'approve' ? 'Approved' : 'Rejected'; t.rejectionReason = body.reason ?? null; t.version++; return send(res, 200, t.status); }
      if ((g = path.match(/^\/api\/ops\/fixtures\/([^/]+)\/(\w+)$/))) { const f = fixtures.find((x) => x.id === g[1]); if (!f) return err(res, 404, 'not_found', 'Fixture not found.');
        if (g[2] === 'start') { f.status = 'Live'; f.homeScore = 0; f.awayScore = 0; f.version++; }
        else if (g[2] === 'live') { f.livePeriod = body.period; f.liveClock = body.clock; }
        else if (g[2] === 'schedule') { if (fixtures.some((x) => x.id !== f.id && x.venue?.toLowerCase() === body.venue.toLowerCase() && x.startsAtUtc && new Date(body.startsAtUtc) < new Date(x.endsAtUtc) && new Date(x.startsAtUtc) < new Date(body.endsAtUtc))) return err(res, 409, 'schedule.clash', `Venue '${body.venue}' is already booked at that time.`); Object.assign(f, { venue: body.venue, startsAtUtc: body.startsAtUtc, endsAtUtc: body.endsAtUtc, status: 'Scheduled', version: f.version + 1 }); }
        else if (g[2] === 'result') { if (body.outcome === 'Completed' && body.homeScore === body.awayScore) return err(res, 422, 'score.tie', 'A knockout match cannot end level. Record the deciding score.'); Object.assign(f, { status: body.outcome === 'Completed' ? 'Completed' : body.outcome, homeScore: body.homeScore, awayScore: body.awayScore, winnerTeamId: body.walkoverWinnerId ?? (body.homeScore > body.awayScore ? f.homeTeamId : f.awayTeamId), version: f.version + 1 }); }
        audited(`ops.fixture.${g[2]}`, 'fixture', f.id, me); return send(res, 200, true); }
      if ((g = path.match(/^\/api\/ops\/teams\/([^/]+)\/withdraw$/))) { const t = teams.find((x) => x.id === g[1]); t.status = 'Withdrawn'; return send(res, 200, { walkoversApplied: 1, manualWalkoversNeeded: 0 }); }
    }
    // ---- admin (read-only)
    if (path.startsWith('/api/admin')) {
      if (!need('Admin')) return;
      if (path === '/api/admin/dashboard') { const by = (k) => Object.entries(Object.groupBy(users, (u) => u[k])).map(([name, v]) => ({ name, count: v.length })); const dep = Object.entries(Object.groupBy(players, (p) => p.department)).map(([name, v]) => ({ name, count: v.length })).sort((a, b) => b.count - a.count);
        return send(res, 200, { generatedAtUtc: new Date().toISOString(), totalUsers: users.length, activeCaptains: users.filter((u) => u.role === 'Captain').length, totalTeams: teams.length, totalParticipants: players.length, totalTournaments: tournaments.length, pendingTeams: teams.filter((t) => t.status === 'Pending').length, approvedTeams: teams.filter((t) => t.status === 'Approved').length, rejectedTeams: teams.filter((t) => t.status === 'Rejected').length, withdrawnTeams: teams.filter((t) => t.status === 'Withdrawn').length, stalePendingTeams: teams.filter((t) => t.status === 'Pending' && now - new Date(t.createdAtUtc) > 48 * H).length, emailFailures: 2, usersByRole: by('role'), participantsByDepartment: dep, registrationsPerDay: Array.from({ length: 10 }, (_, i) => ({ dayUtc: at(-24 * (9 - i)), count: [1, 3, 2, 6, 4, 8, 5, 9, 4, 7][i] })) }); }
      if (path === '/api/admin/users') { let a = users.map((u) => ({ ...u, email: mask(u.email), teamsOwned: teams.filter((t) => t.ownerId === u.id).length })); if (q.get('role')) a = a.filter((u) => u.role === q.get('role')); if (q.get('verified')) a = a.filter((u) => String(u.emailVerified) === q.get('verified')); const s = q.get('search')?.toLowerCase(); if (s) a = a.filter((u) => u.displayName.toLowerCase().includes(s)); return send(res, 200, paged(a, q)); }
      if ((g = path.match(/^\/api\/admin\/users\/([^/]+)$/))) { const u = users.find((x) => x.id === g[1]); if (!u) return err(res, 404, 'not_found', 'User not found.'); const reveal = q.get('reveal') === 'true'; audited(reveal ? 'admin.user.revealed_email' : 'admin.user.viewed', 'profile', u.id, me);
        return send(res, 200, { ...u, email: reveal ? u.email : mask(u.email), teams: teams.filter((t) => t.ownerId === u.id).map((t) => ({ teamId: t.id, teamName: t.name, status: t.status, tournamentName: tournaments.find((x) => x.id === t.tournamentId).name, sportName: tournaments.find((x) => x.id === t.tournamentId).sportName })), recentActivity: audit.filter((a) => a.actorId === u.id).slice(0, 5) }); }
      if (path === '/api/admin/participants') { let a = players.map((p) => { const t = teams.find((x) => x.id === p.teamId), tr = tournaments.find((x) => x.id === t.tournamentId); return { playerId: p.id, fullName: p.fullName, enrollmentNo: maskEn(p.enrollmentNo), department: p.department, teamId: t.id, teamName: t.name, teamStatus: t.status, sportName: tr.sportName, tournamentName: tr.name, tournamentId: tr.id, captainName: users.find((u) => u.id === t.ownerId).displayName, registeredAtUtc: p.createdAtUtc }; });
        for (const [k, f] of [['tournamentId', 'tournamentId'], ['teamStatus', 'teamStatus'], ['department', 'department']]) if (q.get(k)) a = a.filter((x) => x[f] === q.get(k)); if (q.get('sportId')) a = a.filter((x) => x.sportName === sports.find((s) => s.id === q.get('sportId'))?.name); const s = q.get('search')?.toLowerCase(); if (s) a = a.filter((x) => x.fullName.toLowerCase().includes(s) || x.teamName.toLowerCase().includes(s)); return send(res, 200, paged(a, q)); }
      if (path === '/api/admin/teams') { let a = teams.map(teamSummary); if (q.get('status')) a = a.filter((t) => t.status === q.get('status')); const s = q.get('search')?.toLowerCase(); if (s) a = a.filter((t) => t.teamName.toLowerCase().includes(s)); return send(res, 200, paged(a, q)); }
      if ((g = path.match(/^\/api\/admin\/teams\/([^/]+)$/))) { const t = teams.find((x) => x.id === g[1]); return send(res, 200, { team: teamSummary(t), roster: players.filter((p) => p.teamId === t.id).map((p) => ({ playerId: p.id, fullName: p.fullName, enrollmentNo: maskEn(p.enrollmentNo), department: p.department })) }); }
      if (path === '/api/admin/departments') return send(res, 200, Object.entries(Object.groupBy(players, (p) => p.department)).map(([department, v]) => ({ department, participants: v.length, teams: new Set(v.map((p) => p.teamId)).size, approvedTeams: new Set(v.filter((p) => teams.find((t) => t.id === p.teamId).status === 'Approved').map((p) => p.teamId)).size })).sort((a, b) => b.participants - a.participants));
      if (path === '/api/admin/audit') { let a = audit; if (q.get('action')) a = a.filter((x) => x.action === q.get('action')); return send(res, 200, paged(a, q)); }
      if (path.startsWith('/api/admin/exports/')) { if (body.includeSensitive && (body.reason ?? '').trim().length < 10) return err(res, 400, 'validation_failed', 'One or more fields are invalid.', { reason: ['A reason of at least 10 characters is required for sensitive exports.'] }); audited(`admin.export.${path.split('/').pop()}`, 'export', null, me);
        res.writeHead(200, { 'Content-Type': 'text/csv; charset=utf-8', 'Content-Disposition': `attachment; filename="${path.split('/').pop()}-export.csv"` }); return res.end('id,name\r\n1,example\r\n'); }
    }
    return err(res, 404, 'not_found', 'Not found.');
  } catch (e) { console.error(e); return err(res, 500, 'internal_error', 'Something went wrong.'); }
}).listen(PORT, () => console.log(`DEV MOCK API on http://localhost:${PORT} — tokens: mock-captain | mock-scorer | mock-admin`));
