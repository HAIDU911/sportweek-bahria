import { Component, type ReactNode } from 'react';
import { Navigate, Route, Routes } from 'react-router-dom';
import { PublicLayout, RequireRole, WorkspaceLayout } from './components/layouts';
import { Btn } from './components/ui';
import { AdminAudit, AdminDashboard, AdminDepartments, AdminExports, AdminParticipants, AdminTeams, AdminTournaments, AdminUsers } from './pages/admin';
import { AuthCallbackPage, ForbiddenPage, LoginPage, NotFoundPage, VerifyEmailPage } from './pages/auth';
import { CaptainFixtures, CaptainNotifications, CaptainOverview, CaptainTeams } from './pages/captain';
import { OpsTeams, OpsTournamentPage, OpsTournaments } from './pages/ops';
import { FixturesPage, HomePage, LeaderboardsPage, SportDetailPage, SportsPage, TeamsDirectoryPage, TournamentDetailPage, TournamentsPage } from './pages/public';

class Boundary extends Component<{ children: ReactNode }, { failed: boolean }> {
  state = { failed: false };
  static getDerivedStateFromError() { return { failed: true }; }
  componentDidCatch(e: unknown) { console.error('UI error', e); }
  render() {
    return this.state.failed ? (
      <div className="container" style={{ padding: '64px 24px' }}><div className="state error" role="alert"><h3>Something broke</h3><p>An unexpected error occurred. Your data is safe.</p><Btn onClick={() => location.assign('/')}>Reload</Btn></div></div>
    ) : this.props.children;
  }
}

export default function App() {
  return (
    <Boundary>
      <Routes>
        <Route path="/login" element={<LoginPage />} /><Route path="/register" element={<LoginPage register />} /><Route path="/auth/callback" element={<AuthCallbackPage />} />
        <Route element={<PublicLayout />}>
          <Route index element={<HomePage />} />
          <Route path="sports" element={<SportsPage />} /><Route path="sports/:id" element={<SportDetailPage />} />
          <Route path="tournaments" element={<TournamentsPage />} /><Route path="tournaments/:id" element={<TournamentDetailPage />} />
          <Route path="fixtures" element={<FixturesPage />} /><Route path="leaderboards" element={<LeaderboardsPage />} /><Route path="teams" element={<TeamsDirectoryPage />} />
          <Route path="verify-email" element={<VerifyEmailPage />} /><Route path="forbidden" element={<ForbiddenPage />} /><Route path="*" element={<NotFoundPage />} />
        </Route>
        <Route path="/captain" element={<RequireRole roles={['Captain']}><WorkspaceLayout kind="captain" /></RequireRole>}>
          <Route index element={<CaptainOverview />} /><Route path="teams" element={<CaptainTeams />} /><Route path="fixtures" element={<CaptainFixtures />} /><Route path="notifications" element={<CaptainNotifications />} />
        </Route>
        <Route path="/ops" element={<RequireRole roles={['Scorer']}><WorkspaceLayout kind="ops" /></RequireRole>}>
          <Route index element={<OpsTournaments />} /><Route path="teams" element={<OpsTeams />} /><Route path="tournaments/:id" element={<OpsTournamentPage />} />
        </Route>
        <Route path="/workspace" element={<RequireRole roles={['Admin']}><WorkspaceLayout kind="admin" /></RequireRole>}>
          <Route index element={<Navigate to="dashboard" replace />} /><Route path="dashboard" element={<AdminDashboard />} />
          <Route path="users" element={<AdminUsers />} /><Route path="participants" element={<AdminParticipants />} /><Route path="teams" element={<AdminTeams />} />
          <Route path="tournaments" element={<AdminTournaments />} /><Route path="departments" element={<AdminDepartments />} /><Route path="audit" element={<AdminAudit />} /><Route path="exports" element={<AdminExports />} />
        </Route>
      </Routes>
    </Boundary>
  );
}
