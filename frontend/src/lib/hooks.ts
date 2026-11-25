import { useMutation, useQuery, useQueryClient, keepPreviousData } from '@tanstack/react-query';
import { api } from './api';
import type * as T from './types';

export const live = 5000; // matches the 5 s public cache window

export const usePublicHome = () => useQuery({ queryKey: ['home'], queryFn: () => api.publicGet<T.Home>('/api/public/home'), refetchInterval: live });
export const useSports = () => useQuery({ queryKey: ['sports'], queryFn: () => api.publicGet<T.PublicSport[]>('/api/public/sports') });
export const useTournaments = (status?: string) => useQuery({ queryKey: ['tournaments', status], queryFn: () => api.publicGet<T.Paged<T.PublicTournament>>('/api/public/tournaments', { status, pageSize: 100 }) });
export const useTournament = (id: string) => useQuery({ queryKey: ['tournament', id], queryFn: () => api.publicGet<T.PublicTournament>(`/api/public/tournaments/${id}`) });
export const useFeed = (p: { status?: string; sportId?: string; tournamentId?: string; day?: string; page?: number }, poll = false) =>
  useQuery({ queryKey: ['feed', p], queryFn: () => api.publicGet<T.Paged<T.FeedFixture>>('/api/public/fixtures', { ...p, pageSize: 100 }), placeholderData: keepPreviousData, refetchInterval: poll ? live : false });
export const useStandings = (id: string) => useQuery({ queryKey: ['standings', id], queryFn: () => api.publicGet<T.StandingRow[]>(`/api/public/tournaments/${id}/standings`) });
export const useLeaderboard = () => useQuery({ queryKey: ['leaderboard'], queryFn: () => api.publicGet<T.LeaderboardRow[]>('/api/public/leaderboards/overall') });
export const usePublicTeams = (search: string, page: number) => useQuery({ queryKey: ['pteams', search, page], queryFn: () => api.publicGet<T.Paged<T.PublicTeam>>('/api/public/teams', { search, page, pageSize: 24 }), placeholderData: keepPreviousData });

// captain
export const useMyTeams = () => useQuery({ queryKey: ['my-teams'], queryFn: () => api.get<T.CaptainTeam[]>('/api/captain/teams') });
export const useMyFixtures = () => useQuery({ queryKey: ['my-fixtures'], queryFn: () => api.get<T.FeedFixture[]>('/api/captain/fixtures'), refetchInterval: 15000 });
export const useNotifications = (enabled = true) => useQuery({ queryKey: ['notifications'], queryFn: () => api.get<T.Notification[]>('/api/captain/notifications'), refetchInterval: 30000, enabled });

// ops
export const useOpsTournaments = () => useQuery({ queryKey: ['ops-tournaments'], queryFn: () => api.get<T.OpsTournament[]>('/api/ops/tournaments') });
export const useOpsFixtures = (id: string) => useQuery({ queryKey: ['ops-fixtures', id], queryFn: () => api.get<T.OpsFixture[]>(`/api/ops/tournaments/${id}/fixtures`) });
export const useOpsTeams = (p: { status?: string; tournamentId?: string; search?: string; page?: number }) =>
  useQuery({ queryKey: ['ops-teams', p], queryFn: () => api.get<T.Paged<T.TeamSummary>>('/api/ops/teams', { ...p, pageSize: 50 }), placeholderData: keepPreviousData });

// admin (read-only)
export const useDashboard = () => useQuery({ queryKey: ['admin-dashboard'], queryFn: () => api.get<T.Dashboard>('/api/admin/dashboard'), refetchInterval: 60000 });
export const useAdminList = <R,>(path: string, params: Record<string, string | number | boolean | undefined>) =>
  useQuery({ queryKey: ['admin', path, params], queryFn: () => api.get<T.Paged<R>>(path, params), placeholderData: keepPreviousData });
export const useDepartments = () => useQuery({ queryKey: ['admin-depts'], queryFn: () => api.get<T.DepartmentStat[]>('/api/admin/departments') });

/** POST that invalidates the given query-key prefixes on success. */
export function useAction<TBody, TRes = unknown>(fn: (b: TBody) => Promise<TRes>, invalidate: string[][] = []) {
  const qc = useQueryClient();
  return useMutation({ mutationFn: fn, onSuccess: () => { invalidate.forEach((k) => void qc.invalidateQueries({ queryKey: k })); } });
}
