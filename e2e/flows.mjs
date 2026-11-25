// End-to-end flows against the DEV MOCK API (or a real stack with VITE_AUTH_MODE=mock off — adapt login).
// Run: node e2e/flows.mjs   (requires `npm run mock-api` and `npm run dev:mock` in frontend/, and playwright installed)
import { createRequire } from 'node:module';
const require = createRequire(process.env.PW_MODULES ?? '/opt/npm-tools/node_modules/');
const { chromium } = require('playwright');
const base = process.env.BASE ?? 'http://localhost:5173';
const browser = await chromium.launch({ executablePath: process.env.CHROME, args: ['--no-sandbox'] });
let pass = 0, fail = 0;
const ok = (c, m) => { c ? pass++ : fail++; console.log(`${c ? 'PASS' : 'FAIL'}  ${m}`); };
const as = async (role) => { const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 } }); if (role) await ctx.addInitScript((t) => localStorage.setItem('sw_mock_token', t), `mock-${role}`); return [ctx, await ctx.newPage()]; };
const visible = async (page, text, t = 4000) => page.getByText(text, { exact: false }).first().waitFor({ state: 'visible', timeout: t }).then(() => true, () => false);

// ---- public
{ const [c, p] = await as(); await p.goto(base + '/'); await p.waitForLoadState('networkidle');
  ok(await visible(p, 'THE CAMPUS BOARD', 3000) || await visible(p, 'campus board'), 'public home renders without login');
  ok(await p.locator('.sc').count() > 0, 'live score card is shown'); await p.getByRole('link', { name: 'Tournaments' }).first().click(); await p.waitForLoadState('networkidle');
  await p.getByRole('link', { name: /Futsal/ }).first().click(); await p.getByRole('tab', { name: 'Fixtures' }).click();
  ok(await p.locator('.bnode').count() >= 4, 'bracket nodes are drawn'); await p.getByRole('button', { name: 'List' }).click(); ok(await visible(p, 'Quarter-final'), 'list view shows round labels');
  await p.goto(base + '/workspace/dashboard'); ok(await p.waitForURL(/\/login/, { timeout: 4000 }).then(() => true, () => false), 'anonymous user is redirected to login from admin area'); await c.close(); }

// ---- captain: register a team with server + client validation
{ const [c, p] = await as('captain'); await p.goto(base + '/captain/teams'); await p.waitForLoadState('networkidle');
  await p.getByRole('button', { name: /Register a team/ }).first().click();
  await p.getByLabel('Tournament').selectOption({ index: 1 }); await p.getByRole('button', { name: 'Continue' }).click();
  await p.getByRole('button', { name: 'Continue' }).click(); ok(await visible(p, 'Team name must be 3'), 'empty team name is rejected before the server is called');
  await p.getByLabel('Team name').fill('Test Titans'); await p.getByLabel('Department').fill('Computer Science'); await p.getByRole('button', { name: 'Continue' }).click();
  await p.getByRole('button', { name: 'Continue' }).click(); ok(await visible(p, 'Name is required'), 'blank roster rows are flagged');
  for (let i = 0; i < 11; i++) { const n = i + 1; await p.getByLabel(`Player ${n} name`).fill(`Player ${n}`).catch(() => {}); await p.getByLabel(`Player ${n} enrollment`).fill(i === 1 ? 'ABC-10000' : `ABC-1000${i}`).catch(() => {}); await p.getByLabel(`Player ${n} department`).fill('CS').catch(() => {}); }
  await p.getByLabel('Player 2 enrollment').fill('ABC-10000'); await p.getByRole('button', { name: 'Continue' }).click(); ok(await visible(p, 'Duplicate in this roster'), 'duplicate enrollment inside the roster is caught');
  await p.getByLabel('Player 2 enrollment').fill('ABC-20001');
  // roster size comes from the sport: Cricket needs 11–15
  const count = await p.getByLabel(/Player \d+ name/).count(); ok(count >= 11, `roster starts at the sport minimum (${count})`);
  await p.getByRole('button', { name: 'Continue' }).click(); await p.getByRole('button', { name: 'Submit team' }).click(); ok(await visible(p, 'Pending'), 'submitted team shows Pending approval');
  await c.close(); }

// ---- scorer: review queue + scheduling clash + tie
{ const [c, p] = await as('scorer'); await p.goto(base + '/ops/teams'); await p.waitForLoadState('networkidle');
  await p.locator('tr.click').first().click(); await p.getByRole('button', { name: 'Reject…' }).click();
  await p.getByRole('button', { name: 'Confirm rejection' }).click(); ok(await visible(p, 'rejection reason of 5'), 'rejection requires a reason (server message shown)');
  await p.getByLabel(/Rejection reason/).fill('Roster incomplete — three students unverified.'); await p.getByRole('button', { name: 'Confirm rejection' }).click(); ok(await visible(p, 'Team rejected'), 'rejection succeeds and captain is notified');
  await p.goto(base + '/ops'); await p.waitForLoadState('networkidle'); await p.locator('.card', { hasText: 'Futsal' }).getByRole('link', { name: 'Manage' }).click(); await p.waitForLoadState('networkidle');
  await p.locator('tr', { hasText: 'Cyberstrikers' }).getByRole('button', { name: 'Schedule' }).click();
  await p.getByLabel('Venue').fill('Gymnasium Court A');
  const d = new Date(Date.now() + 36e5); const pad = (n) => String(n).padStart(2, '0'); // overlaps the live match venue window (campus time wall clock)
  const camp = new Date(Date.now() - 0.2 * 36e5 + 5 * 36e5); await p.getByLabel(/Start/).fill(`${camp.getUTCFullYear()}-${pad(camp.getUTCMonth() + 1)}-${pad(camp.getUTCDate())}T${pad(camp.getUTCHours())}:${pad(camp.getUTCMinutes())}`);
  await p.getByRole('button', { name: 'Save schedule' }).click(); ok(await visible(p, 'already booked'), 'venue double-booking is rejected with a clear message'); void d;
  await p.keyboard.press('Escape');
  await p.locator('tr', { hasText: 'Voltrunners' }).getByRole('button', { name: 'Result' }).click(); await p.locator('input[type=number]').first().fill('1'); await p.locator('input[type=number]').nth(1).fill('1');
  await p.getByRole('button', { name: 'Record result' }).click(); ok(await visible(p, 'cannot end level'), 'a tied knockout score is rejected');
  await c.close(); }

// ---- admin: masked PII, audited reveal, sensitive export needs a reason
{ const [c, p] = await as('admin'); await p.goto(base + '/workspace/users'); await p.waitForLoadState('networkidle');
  ok(await p.locator('td.mono', { hasText: '***@' }).count() > 0, 'emails are masked in the user list');
  ok(await p.getByRole('button', { name: /Edit|Delete/ }).count() === 0, 'no edit/delete controls exist in the admin console');
  await p.locator('tr.click').nth(2).click(); await p.getByRole('button', { name: /Reveal/ }).click(); ok(await visible(p, 'recorded in the audit log'), 'reveal asks for confirmation and mentions auditing');
  await p.getByRole('button', { name: 'Reveal and record' }).click(); ok(await visible(p, 'Revealed'), 'full email is shown after confirmation');
  await p.keyboard.press('Escape'); await p.keyboard.press('Escape');
  await p.getByRole('button', { name: 'Export CSV' }).click(); await p.getByLabel(/Include sensitive/).check(); await p.getByRole('button', { name: 'Download' }).click();
  ok(await visible(p, 'at least 10 characters'), 'sensitive export without a reason is refused');
  await p.goto(base + '/workspace/audit'); await p.waitForLoadState('networkidle'); ok(await visible(p, 'admin.user.revealed_email'), 'the reveal appears in the audit log'); await c.close(); }

// ---- role isolation
{ const [c, p] = await as('captain'); await p.goto(base + '/workspace/dashboard'); await p.waitForLoadState('networkidle'); ok(p.url().includes('/forbidden'), 'captain cannot open the admin console');
  await p.goto(base + '/ops'); await p.waitForLoadState('networkidle'); ok(p.url().includes('/forbidden'), 'captain cannot open the operator desk'); await c.close(); }
{ const [c, p] = await as('admin'); await p.goto(base + '/ops'); await p.waitForLoadState('networkidle'); ok(p.url().includes('/forbidden'), 'admin is read-only: cannot open the operator desk'); await c.close(); }

await browser.close(); console.log(`\n${pass} passed, ${fail} failed`); process.exit(fail ? 1 : 0);
