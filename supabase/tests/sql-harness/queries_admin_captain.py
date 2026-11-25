import json, re, uuid, psycopg2, psycopg2.extras
S = json.load(open('/tmp/sql.json'))
conn = psycopg2.connect(dbname='sw_test', user='postgres', host='/var/run/postgresql'); conn.autocommit = True
cur = conn.cursor(cursor_factory=psycopg2.extras.RealDictCursor)
def q(_n, **p):
    sql = re.sub(r'@(\w+)', r'%(\1)s', S[_n])
    cur.execute(sql, p)
    try: return cur.fetchall()
    except psycopg2.ProgrammingError: return []

# fresh demo data
cur.execute("truncate team_players, fixtures, teams, profiles cascade")
admin, cap1, cap2 = (str(uuid.uuid4()) for _ in range(3))
for i,(pid,email,name,role) in enumerate([(admin,'admin@gmail.com','Admin','Admin'),(cap1,'hamza@gmail.com','Hamza','Captain'),(cap2,'sara@gmail.com','Sara','Captain')]):
    cur.execute("insert into profiles(id,email,email_normalized,display_name,role,email_verified) values (%s,%s,%s,%s,%s,true)",(pid,email,email,name,role))
cur.execute("select t.id, s.min_players from tournaments t join sports s on s.id=t.sport_id where s.slug='futsal'"); tid=cur.fetchone()['id']
cur.execute("select id from tournaments where name like 'Cricket%'"); tid2=cur.fetchone()['id']

none_users = dict(search=None,pattern=None,role=None,verified=None,**{'from':None,'to':None},size=25,offset=0)
print("users count:", q('UsersCount', **none_users)[0]['count'])
print("users page:", [(r['display_name'], r['role'], r['teams_owned']) for r in q('UsersPage', **none_users)])
f = dict(none_users, search='ham', pattern='%ham%', role='Captain', verified=True)
print("users filtered:", [r['display_name'] for r in q('UsersPage', **f)], q('UsersCount', **f)[0]['count'])
# LIKE-escape check: a literal % must not match everything
f = dict(none_users, search='%', pattern='%\\%%'); print("users '%' literal ->", q('UsersCount', **f)[0]['count'])

# captain flow through the SQL used by CaptainStore
r = q('TournamentRules', id=tid)[0]; print("rules:", dict(r))
tm = q('TeamInsert', tournament=tid, owner=cap1, name='Falcons', nameNormalized='falcons', department='CS', key='idem-0001')[0]['id']
for n,e in enumerate(['01-100001','01-100002','01-100003','01-100004','01-100005']):
    q('PlayerInsert', team=tm, tournament=tid, fullName=f'Player {n}', enrollmentNo=e, enrollmentNormalized=e, department='CS')
tm2 = q('TeamInsert', tournament=tid, owner=cap2, name='Hawks', nameNormalized='hawks', department='EE', key='idem-0002')[0]['id']
for n,e in enumerate(['02-200001','02-200002','02-200003','02-200004','02-200005']):
    q('PlayerInsert', team=tm2, tournament=tid, fullName=f'Hawk {n}', enrollmentNo=e, enrollmentNormalized=e, department='EE')
cur.execute("update teams set status='Approved' where id=%s",(tm,))
print("lock:", q('TournamentLock', id=tid)[0], "active:", q('ActiveTeamCount', id=tid)[0]['count'])
print("idempotency:", q('TeamByIdempotency', owner=cap1, key='idem-0001')[0]['id']==tm)
print("conflicts:", [r['enrollment_no'] for r in q('ConflictingEnrollments', tournament=tid, normalized=['01-100001','99-999999'])])
print("owned:", [(r['team_name'], r['status'], r['roster_count']) for r in q('OwnedTeams', owner=cap1)])

none_p = dict(search=None,pattern=None,tournament=None,sport=None,department=None,status=None,size=25,offset=0)
print("participants:", q('ParticipantsCount', **none_p)[0]['count'], "|", dict(q('ParticipantsPage', **none_p)[0]))
print("participants dept=ee:", q('ParticipantsCount', **dict(none_p, department='ee'))[0]['count'],
      "status=Approved:", q('ParticipantsCount', **dict(none_p, status='Approved'))[0]['count'],
      "search enrollment:", q('ParticipantsCount', **dict(none_p, search='200003', pattern='%200003%'))[0]['count'])
none_t = dict(search=None,pattern=None,tournament=None,status=None,size=25,offset=0)
print("teams:", q('TeamsCount', **none_t)[0]['count'], [ (r['team_name'], r['roster_count'], r['captain_email']) for r in q('TeamsPage', **none_t)])
print("team detail:", q('TeamById', id=tm)[0]['team_name'], len(q('TeamRoster', id=tm)))

d = q('DashboardCounts')[0]; print("dashboard:", dict(d))
print("by role:", [dict(r) for r in q('UsersByRole')]); print("by dept:", [dict(r) for r in q('ParticipantsByDepartment')])
print("per day:", [(str(r['day_utc'])[:10], r['count']) for r in q('RegistrationsPerDay')]); print("dept stats:", [dict(r) for r in q('DepartmentStats')])

# audit insert/list (metadata json)
q('AuditInsert', action='admin.user.viewed', actor=admin, entityType='profile', entityId=cap1, result='success', correlationId='c-1', ip='127.0.0.1', metadata='{"revealed":false}')
q('AuditInsert', action='admin.export.users', actor=admin, entityType='export', entityId=None, result='success', correlationId='c-2', ip=None, metadata=None)
none_a = dict(action=None,actor=None,entity=None,**{'from':None,'to':None},size=50,offset=0)
print("audit:", q('AuditCount', **none_a)[0]['count'], [(r['action'], r['actor_name'], r['metadata_json']) for r in q('AuditPage', **none_a)])
print("audit for user:", len(q('AuditForUser', id=admin)), "filtered:", q('AuditCount', **dict(none_a, action='admin.export.users'))[0]['count'])

# identity upsert incl. role change detection
print("upsert:", dict(q('ProfileUpsert', id=cap2, googleSubject='g-77', email='sara@gmail.com', emailNormalized='sara@gmail.com', displayName='Sara K', avatarUrl=None, role='Scorer', roleSource='allowlist:scorer', emailVerified=True)[0]))
print("user detail:", dict(q('UserById', id=cap2)[0]))
print("user teams:", [dict(r) for r in q('UserTeams', id=cap1)])

# public
print("public sports:", len(q('PublicSports')))
print("public tournaments:", q('PublicTournamentCount', status=None)[0]['count'], dict(q('PublicTournamentPage', status=None, size=2, offset=0)[0]))
print("public tournament by id:", q('PublicTournamentById', id=tid)[0]['approved_teams'])
print("public teams (approved only):", [dict(r) for r in q('PublicTeamPage', search=None, pattern=None, size=10, offset=0)])
print("public fixtures:", q('PublicFixtureCount', id=tid)[0]['count'], "| leaderboard:", [dict(r) for r in q('PublicLeaderboard')])
print("ALL QUERIES EXECUTED")
