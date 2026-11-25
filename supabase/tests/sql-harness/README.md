# SQL harness
Executes every constant in `backend/Infrastructure/Data/Sql.cs` against a real PostgreSQL (after migrations).
1. Dump constants to /tmp/sql.json (reflection over `Sql`), 2. run `python3 queries_admin_captain.py`, `queries_ops_feed.py`, `api_role_privileges.py`.
Needs `psycopg2-binary`. Reset the database between scripts (they truncate/mutate demo data).
