import { config } from './config';
import type { FeedFixture, FixtureStatus } from './types';

const tz = config.timezone;
export const fmtTime = (iso: string | null) => iso ? new Intl.DateTimeFormat('en-GB', { hour: '2-digit', minute: '2-digit', timeZone: tz, hour12: false }).format(new Date(iso)) : '—';
export const fmtDate = (iso: string | null) => iso ? new Intl.DateTimeFormat('en-GB', { day: '2-digit', month: 'short', year: 'numeric', timeZone: tz }).format(new Date(iso)) : '—';
export const fmtDateTime = (iso: string | null) => iso ? `${fmtDate(iso)} · ${fmtTime(iso)}` : '—';
export const fmtDay = (iso: string | null) => iso ? new Intl.DateTimeFormat('en-GB', { weekday: 'short', day: '2-digit', month: 'short', timeZone: tz }).format(new Date(iso)) : 'Unscheduled';

export function relativeTime(iso: string | null, now = Date.now()): string {
  if (!iso) return 'never';
  const s = Math.round((now - new Date(iso).getTime()) / 1000);
  const abs = Math.abs(s); const fut = s < 0;
  const unit = abs < 60 ? [abs, 's'] : abs < 3600 ? [Math.round(abs / 60), 'm'] : abs < 86400 ? [Math.round(abs / 3600), 'h'] : [Math.round(abs / 86400), 'd'];
  return fut ? `in ${unit[0]}${unit[1]}` : `${unit[0]}${unit[1]} ago`;
}

export function roundLabel(round: number, maxRound: number, isThird: boolean): string {
  if (isThird) return 'Third place';
  const fromEnd = maxRound - round;
  return fromEnd === 0 ? 'Final' : fromEnd === 1 ? 'Semi-final' : fromEnd === 2 ? 'Quarter-final' : fromEnd === 3 ? 'Round of 16' : `Round ${round}`;
}

export const isFinished = (s: FixtureStatus) => ['Completed', 'Walkover', 'DoubleForfeit', 'Abandoned', 'Bye'].includes(s);

export function scoreText(f: Pick<FeedFixture, 'homeScore' | 'awayScore' | 'status'>): [string, string] {
  if (f.homeScore === null || f.awayScore === null) return ['–', '–'];
  return [String(f.homeScore), String(f.awayScore)];
}

/** Escapes CSV-significant data? No: exports are server-generated. This only builds a safe download URL. */
export function downloadBlob(blob: Blob, filename: string) {
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a'); a.href = url; a.download = filename.replace(/[^A-Za-z0-9._-]/g, '_'); a.click();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}

/** datetime-local value (campus wall time) → UTC ISO string. */
export function localToUtcIso(local: string): string {
  // Campus zone is Asia/Karachi (UTC+5, no DST). Compute offset generically to stay correct if the zone changes.
  const asUtc = new Date(local + ':00Z').getTime();
  const probe = new Date(asUtc);
  const campus = new Date(probe.toLocaleString('en-US', { timeZone: tz }));
  const utcWall = new Date(probe.toLocaleString('en-US', { timeZone: 'UTC' }));
  const offset = campus.getTime() - utcWall.getTime();
  return new Date(asUtc - offset).toISOString();
}
