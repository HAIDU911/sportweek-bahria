import { describe, expect, it } from 'vitest';
import { isFinished, localToUtcIso, relativeTime, roundLabel, scoreText } from './format';

describe('format helpers', () => {
  it('converts campus wall-clock time (Asia/Karachi, UTC+5) to UTC for scheduling', () => {
    expect(localToUtcIso('2026-12-01T09:00')).toBe('2026-12-01T04:00:00.000Z');
    expect(localToUtcIso('2026-12-01T02:30')).toBe('2026-11-30T21:30:00.000Z'); // crosses midnight backwards
    expect(localToUtcIso('2026-06-15T23:59')).toBe('2026-06-15T18:59:00.000Z');
  });
  it('labels rounds from the end of the bracket', () => {
    expect(roundLabel(3, 3, false)).toBe('Final'); expect(roundLabel(2, 3, false)).toBe('Semi-final'); expect(roundLabel(1, 3, false)).toBe('Quarter-final');
    expect(roundLabel(1, 4, false)).toBe('Round of 16'); expect(roundLabel(1, 6, false)).toBe('Round 1'); expect(roundLabel(3, 3, true)).toBe('Third place');
  });
  it('formats relative times in both directions', () => {
    const now = Date.parse('2026-12-01T12:00:00Z');
    expect(relativeTime('2026-12-01T11:59:30Z', now)).toBe('30s ago'); expect(relativeTime('2026-12-01T09:00:00Z', now)).toBe('3h ago');
    expect(relativeTime('2026-11-29T12:00:00Z', now)).toBe('2d ago'); expect(relativeTime('2026-12-01T12:10:00Z', now)).toBe('in 10m'); expect(relativeTime(null, now)).toBe('never');
  });
  it('shows dashes until a score exists, and knows finished statuses', () => {
    expect(scoreText({ homeScore: null, awayScore: null, status: 'Ready' })).toEqual(['–', '–']); expect(scoreText({ homeScore: 3, awayScore: 0, status: 'Completed' })).toEqual(['3', '0']);
    expect(isFinished('Completed')).toBe(true); expect(isFinished('Walkover')).toBe(true); expect(isFinished('Live')).toBe(false); expect(isFinished('Scheduled')).toBe(false);
  });
});
