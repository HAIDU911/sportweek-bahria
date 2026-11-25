import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';
import type { UseQueryResult } from '@tanstack/react-query';
import { ApiError } from '../lib/api';
import type { FeedFixture } from '../lib/types';
import { Bracket } from './fixtures';
import { RequireRole } from './layouts';
import { DataState, ErrorState, Pager, StatusBadge } from './ui';

const auth = vi.hoisted(() => ({ value: { status: 'authed', user: null as unknown } }));
vi.mock('../lib/auth', () => ({ useAuth: () => auth.value, homeFor: () => '/', AuthProvider: ({ children }: { children: unknown }) => children }));

const q = (o: Partial<UseQueryResult<string[]>>) => ({ isPending: false, isError: false, data: undefined, refetch: vi.fn(), ...o }) as unknown as UseQueryResult<string[]>;

describe('DataState', () => {
  it('shows a loading skeleton, the error with retry, the empty state, and data', async () => {
    const { rerender } = render(<DataState q={q({ isPending: true })}>{() => <p>data</p>}</DataState>);
    expect(screen.getByRole('status', { name: /loading/i })).toBeInTheDocument();
    const refetch = vi.fn();
    rerender(<DataState q={q({ isError: true, error: new ApiError(500, 'x', 'Boom happened'), refetch } as never)}>{() => <p>data</p>}</DataState>);
    expect(screen.getByRole('alert')).toHaveTextContent('Boom happened'); await userEvent.click(screen.getByRole('button', { name: /retry/i })); expect(refetch).toHaveBeenCalled();
    rerender(<DataState q={q({ data: [] })} isEmpty={(d) => d.length === 0} empty={<p>nothing</p>}>{() => <p>data</p>}</DataState>); expect(screen.getByText('nothing')).toBeInTheDocument();
    rerender(<DataState q={q({ data: ['a'] })}>{(d) => <p>got {d[0]}</p>}</DataState>); expect(screen.getByText('got a')).toBeInTheDocument();
  });
  it('explains dependency outages without leaking technical details', () => {
    render(<ErrorState error={new ApiError(503, 'dependency_unavailable', 'db down at 10.0.0.5')} />);
    expect(screen.getAllByText(/temporarily offline/i).length).toBeGreaterThan(0); expect(screen.queryByText(/10\.0\.0\.5/)).not.toBeInTheDocument();
  });
  it('shows retry-after for rate limiting', () => {
    render(<ErrorState error={new ApiError(429, 'rate_limited', 'Too many requests.', undefined, null, 9)} />); expect(screen.getByText(/try again in 9s/i)).toBeInTheDocument();
  });
});

describe('small components', () => {
  it('pager disables edges and reports totals', async () => {
    const on = vi.fn(); render(<Pager page={1} totalPages={3} total={61} onPage={on} />);
    expect(screen.getByText(/61 results/)).toBeInTheDocument(); expect(screen.getByRole('button', { name: 'Prev' })).toBeDisabled();
    await userEvent.click(screen.getByRole('button', { name: 'Next' })); expect(on).toHaveBeenCalledWith(2);
  });
  it('status badges always carry text, not just colour', () => { render(<StatusBadge status="Live" />); expect(screen.getByText('Live')).toBeInTheDocument(); });
});

const fx = (o: Partial<FeedFixture>): FeedFixture => ({ id: crypto.randomUUID(), tournamentId: 't', tournamentName: 'T', sportName: 'Futsal', round: 1, slot: 1, isThirdPlace: false, status: 'Ready', homeTeam: 'A', homeDepartment: 'CS', awayTeam: 'B', awayDepartment: 'EE', homeScore: null, awayScore: null, winnerTeam: null, venue: null, startsAtUtc: null, livePeriod: null, liveClock: null, ...o });
describe('Bracket', () => {
  it('lays out rounds with labels, winners and undecided slots', () => {
    render(<Bracket fixtures={[fx({ round: 1, slot: 1, status: 'Completed', homeScore: 2, awayScore: 1, winnerTeam: 'A' }), fx({ round: 1, slot: 2, homeTeam: 'C', awayTeam: 'D' }), fx({ round: 2, slot: 1, homeTeam: 'A', awayTeam: null, status: 'Pending' }), fx({ round: 2, slot: 2, isThirdPlace: true, homeTeam: null, awayTeam: null, status: 'Pending' })]} />);
    expect(screen.getByText('Semi-final')).toBeInTheDocument(); expect(screen.getByText('Final')).toBeInTheDocument(); expect(screen.getByText('Third place')).toBeInTheDocument();
    expect(screen.getAllByText('To be decided').length).toBeGreaterThanOrEqual(3);
  });
});

describe('RequireRole guard', () => {
  const renderAt = () => render(<MemoryRouter initialEntries={['/secret']}><Routes>
    <Route path="/secret" element={<RequireRole roles={['Admin']}><p>admin area</p></RequireRole>} />
    <Route path="/login" element={<p>login page</p>} /><Route path="/forbidden" element={<p>forbidden page</p>} /><Route path="/verify-email" element={<p>verify page</p>} /></Routes></MemoryRouter>);
  it('sends anonymous users to login', () => { auth.value = { status: 'anon', user: null }; renderAt(); expect(screen.getByText('login page')).toBeInTheDocument(); });
  it('sends the wrong role to forbidden — a captain can never see the admin area', () => { auth.value = { status: 'authed', user: { role: 'Captain', emailVerified: true } }; renderAt(); expect(screen.getByText('forbidden page')).toBeInTheDocument(); });
  it('lets the right role in', () => { auth.value = { status: 'authed', user: { role: 'Admin', emailVerified: true } }; renderAt(); expect(screen.getByText('admin area')).toBeInTheDocument(); });
  it('sends rejected sign-ins to the verification page', () => { auth.value = { status: 'rejected', user: null }; renderAt(); expect(screen.getByText('verify page')).toBeInTheDocument(); });
  it('blocks unverified captains from the captain area', () => {
    auth.value = { status: 'authed', user: { role: 'Captain', emailVerified: false } };
    render(<MemoryRouter initialEntries={['/c']}><Routes><Route path="/c" element={<RequireRole roles={['Captain']}><p>captain area</p></RequireRole>} /><Route path="/verify-email" element={<p>verify page</p>} /></Routes></MemoryRouter>);
    expect(screen.getByText('verify page')).toBeInTheDocument();
  });
});
