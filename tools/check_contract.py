#!/usr/bin/env python3
"""Fails if the frontend TypeScript types drift from the backend C# DTOs (field names, camelCase JSON)."""
import re, sys, pathlib
root = pathlib.Path(__file__).resolve().parent.parent
cs = (root / 'backend/Contracts/Dtos.cs').read_text()
ts = (root / 'frontend/src/lib/types.ts').read_text()

def split_top(s, sep=','):
    out, depth, cur = [], 0, ''
    for ch in s:
        if ch in '<([{': depth += 1
        if ch in '>)]}': depth -= 1
        if ch == sep and depth == 0: out.append(cur); cur = ''
        else: cur += ch
    if cur.strip(): out.append(cur)
    return out

def camel(n): return n[0].lower() + n[1:]

def cs_records():
    recs = {}
    for m in re.finditer(r'public sealed record (\w+)(?:<\w+>)?\(([^;{]*?)\)\s*(?:;|\{)', cs, re.S):
        fields = [camel(p.strip().split()[-1].split('=')[0].strip()) for p in split_top(m.group(2)) if p.strip()]
        recs[m.group(1)] = set(fields)
    return recs

def ts_interfaces():
    out = {}
    for m in re.finditer(r'export interface (\w+)(?:<\w+>)?(?: extends ([^{]+))? \{', ts):
        i = m.end(); depth = 1; j = i
        while depth: depth += {'{': 1, '}': -1}.get(ts[j], 0); j += 1
        body = ts[i:j - 1]; fields = set()
        for part in split_top(re.sub(r'\n', ' ', body).replace(';', ',')):
            fm = re.match(r'\s*(\w+)\??\s*:', part)
            if fm: fields.add(fm.group(1))
        out[m.group(1)] = (fields, (m.group(2) or '').strip())
    return out

R, T = cs_records(), ts_interfaces()
pairs = {  # TS interface -> C# record
 'Paged': 'PagedResult', 'CurrentUser': 'CurrentUserDto', 'PublicSport': 'PublicSportDto', 'PublicTournament': 'PublicTournamentDto', 'PublicTeam': 'PublicTeamDto',
 'LeaderboardRow': 'PublicLeaderboardRow', 'StandingRow': 'StandingRow', 'FeedFixture': 'FeedFixtureDto', 'PlayerInput': 'PlayerInput', 'RegisterTeamRequest': 'RegisterTeamRequest',
 'CaptainTeam': 'CaptainTeamDto', 'Notification': 'NotificationDto', 'OpsTournament': 'OpsTournamentDto', 'OpsFixture': 'OpsFixtureDto', 'RegenerationPreview': 'RegenerationPreviewDto',
 'WithdrawResult': 'WithdrawResultDto', 'UserSummary': 'UserSummaryDto', 'OwnedTeam': 'OwnedTeamDto', 'AuditEvent': 'AuditEventDto', 'Participant': 'ParticipantDto',
 'TeamSummary': 'TeamSummaryDto', 'Dashboard': 'DashboardDto', 'DepartmentStat': 'DepartmentStatDto', 'NamedCount': 'NamedCount',
}
bad = 0
def report(msg): 
    global bad; bad += 1; print('DRIFT  ' + msg)
for t, c in pairs.items():
    if t not in T: report(f'TS interface {t} missing'); continue
    if c not in R: report(f'C# record {c} missing'); continue
    tf = set(T[t][0]); cf = set(R[c])
    if t == 'Paged': cf.add('totalPages')  # computed property on the C# record, serialized to JSON
    if tf - cf: report(f'{t}: frontend expects {sorted(tf - cf)} which {c} does not send')
    if cf - tf and t != 'RegisterTeamRequest': report(f'{t}: backend sends {sorted(cf - tf)} the frontend type does not declare')
# composites
ud_ts = T['UserDetail'][0] | (T['UserSummary'][0] - {'teamsOwned'}); ud_cs = R['UserDetailDto']
if ud_ts ^ ud_cs: report(f'UserDetail differs: {sorted(ud_ts ^ ud_cs)}')
td = T['TeamDetail'][0]; 
if td != {'team', 'roster'} or R['TeamDetailDto'] != {'team', 'roster'}: report('TeamDetail shape differs')
home_cs = R['HomeDto']; home_ts = T['Home'][0]
if home_cs ^ home_ts: report(f'Home differs: {sorted(home_cs ^ home_ts)}')
if R['PublicStatsDto'] ^ {'sportsActive', 'teamsApproved', 'matchesPlayed', 'departments', 'liveNow'}: report('PublicStats differs')
print(f'{len(pairs) + 4} contracts compared, {bad} drift(s)')
sys.exit(1 if bad else 0)
