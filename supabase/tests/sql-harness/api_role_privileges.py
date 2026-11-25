import json, re, uuid, psycopg2
S=json.load(open('/tmp/sql.json'))
c=psycopg2.connect(dbname='sw_test',user='sportsweek_api',password='CHANGE_ME_BEFORE_APPLYING',host='127.0.0.1'); c.autocommit=True
cur=c.cursor()
def run(n,**p): cur.execute(re.sub(r'@(\w+)',r'%(\1)s',S[n]),p); 
def must_fail(label, sql):
    try: cur.execute(sql); print("FAIL (allowed):",label)
    except psycopg2.Error as e: print("PASS blocked:",label,"->",e.pgcode)
n=dict(search=None,pattern=None,role=None,verified=None,size=5,offset=0,**{'from':None,'to':None})
run('UsersCount',**n); print("API role can read users:",cur.fetchone()[0])
run('DashboardCounts'); print("dashboard ok")
run('AuditInsert',action='t',actor=None,entityType=None,entityId=None,result='success',correlationId=None,ip=None,metadata=None); print("audit insert ok")
cur.execute("select id from tournaments limit 1"); t=cur.fetchone()[0]
run('TournamentLock',id=t); print("tournament lock ok")
must_fail("audit update","update audit_events set action='x'")
must_fail("audit delete","delete from audit_events")
must_fail("drop table","drop table profiles")
must_fail("create table","create table x(a int)")
