import json, re, uuid, psycopg2, psycopg2.extras, datetime as dt
S=json.load(open('/tmp/sql.json'))
conn=psycopg2.connect(dbname='sw_test',user='postgres',host='/var/run/postgresql'); conn.autocommit=True
cur=conn.cursor(cursor_factory=psycopg2.extras.RealDictCursor)
RC=[0]
def q(_n,**p):
    cur.execute(re.sub(r'@(\w+)',r'%(\1)s',S[_n]),p)
    RC[0]=cur.rowcount
    try: return cur.fetchall()
    except psycopg2.ProgrammingError: return []
def n(): return str(uuid.uuid4())
def ok(c,l): print(("PASS " if c else "FAIL ")+l); assert c

cur.execute("truncate team_players, fixtures, teams, profiles, notifications cascade")
owners=[n() for _ in range(4)]
for i,o in enumerate(owners): cur.execute("insert into profiles(id,email,email_normalized,display_name,email_verified) values (%s,%s,%s,%s,true)",(o,f'c{i}@gmail.com',f'c{i}@gmail.com',f'Cap {i}'))
cur.execute("select t.id from tournaments t join sports s on s.id=t.sport_id where s.slug='futsal'"); tid=cur.fetchone()['id']
teams=[]
for i,o in enumerate(owners):
    teams.append(q('TeamInsert',tournament=tid,owner=o,name=f'Team {i}',nameNormalized=f'team {i}',department=['CS','EE','BBA','SE'][i],key=f'idem-{i:04d}')[0]['id'])

# review: version-checked
t=q('OpsTeam',id=teams[0])[0]; ok(t['status']=='Pending' and t['tournament_status']=='RegistrationOpen','OpsTeam')
ok(q('OpsTeamSetStatus',id=teams[0],status='Approved',reason=None,expected=99)==[] and RC[0]==0,'stale version updates 0 rows')
q('OpsTeamSetStatus',id=teams[0],status='Approved',reason=None,expected=t['version']); ok(RC[0]==1,'approve with right version')
for i in (1,2,3): q('OpsTeamSetStatus',id=teams[i],status='Approved',reason=None,expected=-1)
q('OpsTeamSetStatus',id=teams[3],status='Rejected',reason='Incomplete roster',expected=-1); ok(RC[0]==1,'system update (-1) skips version')
q('OpsTeamSetStatus',id=teams[3],status='Approved',reason=None,expected=-1)
q('NotificationInsert',user=owners[0],type='team_approved',title='Approved',body=None,entityType='team',entityId=teams[0])

# tournament lifecycle + create + duplicate
q('OpsTournamentStatus',id=tid,status='RegistrationClosed'); ok(q('OpsTournament',id=tid)[0]['status']=='RegistrationClosed','status set')
cur.execute("select id from sports limit 1"); sp=cur.fetchone()['id']
nid=q('OpsTournamentCreate',sport=sp,name='Fresh Cup',starts=None,ends=None,limit=8,third=True); ok(len(nid)==1,'tournament created')
ok(q('OpsTournamentCreate',sport=sp,name='Fresh Cup',starts=None,ends=None,limit=8,third=True)==[],'duplicate tournament -> no row')
ok(len(q('OpsTeams',id=tid))==4,'OpsTeams')

# generate 4-team bracket exactly as the engine would (round1 x2, final, third place)
ids={k:n() for k in ('a','b','f','tp')}
def ins(**k):
    base=dict(tournament=tid,third=False,home=None,away=None,status='Pending',winner=None,hs=None,**{'as':None},nr=None,ns=None,nh=False,lr=None,ls=None,lh=False); base.update(k); q('OpsFixtureInsert',**base)
ins(id=ids['a'],round=1,slot=1,home=teams[0],away=teams[1],status='Ready',nr=2,ns=1,nh=True,lr=2,ls=2,lh=True)
ins(id=ids['b'],round=1,slot=2,home=teams[2],away=teams[3],status='Ready',nr=2,ns=1,nh=False,lr=2,ls=2,lh=False)
ins(id=ids['f'],round=2,slot=1); ins(id=ids['tp'],round=2,slot=2,third=True)
q('OpsTournamentStatus',id=tid,status='InProgress')
fx=q('OpsFixtures',id=tid); ok(len(fx)==4 and q('OpsFixtureTournament',id=ids['a'])[0]['tournament_id']==tid,'fixtures stored/read with links')

# schedule + slots + clash data
st=dt.datetime(2026,12,1,9,0,tzinfo=dt.timezone.utc)
ver=[f for f in fx if str(f['id'])==ids['a']][0]['version']
q('OpsSchedule',id=ids['a'],venue='Court 1',start=st,end=st+dt.timedelta(hours=1),expected=ver); ok(RC[0]==1,'schedule saved')
q('OpsSchedule',id=ids['a'],venue='Court 1',start=st,end=st,expected=ver); ok(RC[0]==0,'schedule with stale version rejected')
ok(len(q('OpsSlots',**{'from':st-dt.timedelta(days=1),'to':st+dt.timedelta(days=1)}))==1,'OpsSlots')

# live + detail + result save (optimistic, all-or-nothing semantics per row)
v=q('OpsFixtures',id=tid); va=[f for f in v if str(f['id'])==ids['a']][0]['version']
q('OpsFixtureSave',id=ids['a'],home=teams[0],away=teams[1],status='Live',winner=None,hs=None,**{'as':None},expected=va); ok(RC[0]==1,'start match')
q('OpsLiveDetail',id=ids['a'],period='2nd half',clock="34'"); ok(RC[0]==1,'live detail')
home=q('HomeStats')[0]; ok(home['live_now']==1,'home stats live_now=1')
live=q('HomeLive'); ok(len(live)==1 and live[0]['live_period']=='2nd half' and live[0]['home_department']=='CS','home live feed with period+department')
va=[f for f in q('OpsFixtures',id=tid) if str(f['id'])==ids['a']][0]['version']
q('OpsFixtureSave',id=ids['a'],home=teams[0],away=teams[1],status='Completed',winner=teams[0],hs=3,**{'as':1},expected=va); ok(RC[0]==1,'record result')
vf=[f for f in q('OpsFixtures',id=tid) if str(f['id'])==ids['f']][0]['version']
q('OpsFixtureSave',id=ids['f'],home=teams[0],away=None,status='Pending',winner=None,hs=None,**{'as':None},expected=vf); ok(RC[0]==1,'advance winner into final')
q('OpsFixtureSave',id=ids['a'],home=teams[0],away=teams[1],status='Completed',winner=teams[1],hs=0,**{'as':9},expected=va); ok(RC[0]==0,'double-submit with old version blocked')

# public feed/home/standings
ok(q('HomeStats')[0]['matches_played']==1,'matches_played')
ok(len(q('HomeLatest'))==1,'latest results')
ok(q('FeedCount',status='Completed',tournament=None,sport=None,day=None)[0]['count']==1,'feed filter by status')
ok(q('FeedCount',status=None,tournament=None,sport=None,day=st.date())[0]['count']==1,'feed filter by campus day')
rows=q('FeedPage',status=None,tournament=str(tid),sport=None,day=None,size=50,offset=0); ok(len(rows)==4,'feed page ('+str(len(rows))+')')
nu=q('HomeNextUp'); print('next up rows:',len(nu))
stand=q('Standings',id=tid); print([ (r['team'],r['played'],r['wins'],r['losses']) for r in stand]); ok(stand[0]['wins']==1 and stand[0]['team']=='Team 0','standings leader')
ok(len(q('LeaderboardTop5'))>=1,'top5 leaderboard')
# captain extras
ok(len(q('Notifications',user=owners[0],limit=20))==1,'notifications list')
ok(len(q('OwnerFixtures',owner=owners[0]))>=1 and len(q('OwnerFixtures',owner=n()))==0,'owner fixtures are scoped to the owner')
q('NotificationsMarkRead',user=owners[0]); ok(RC[0]==1,'mark all read')
q('NotificationsMarkRead',user=owners[0]); ok(RC[0]==0,'mark read is idempotent')
# regeneration delete never touches resolved history (trigger) 
q('OpsFixtureDeleteUnresolved',id=tid); cur.execute("select count(*) c from fixtures where tournament_id=%s",(tid,)); ok(cur.fetchone()['c']==1,'delete-unresolved keeps completed history (1 row left)')
print("OPS SQL OK")
