import { useState, useMemo, useCallback } from 'react';
import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { LayoutProvider } from './components/Layout/LayoutContext.tsx';
import { MainLayout } from './components/Layout/MainLayout.tsx';
import { AuthProvider, useAuth } from './hooks/AuthContext.tsx';
import { RequireRole } from './components/common/RequireRole.tsx';
import { WorkflowListPage } from './components/WorkflowList/WorkflowListPage.tsx';
import { WorkflowEditorPage } from './pages/WorkflowEditorPage.tsx';
import { ExecutionHistoryPage } from './pages/ExecutionHistoryPage.tsx';
import { HelpPage } from './pages/HelpPage.tsx';
import { SettingsPage } from './pages/SettingsPage.tsx';
import { AdminUsersPage } from './pages/AdminUsersPage.tsx';
import { AdminAuditPage } from './pages/AdminAuditPage.tsx';
import { AdminFilesPage } from './pages/AdminFilesPage.tsx';
import { AdminProjectsPage } from './pages/AdminProjectsPage.tsx';
import { LoginPage } from './pages/LoginPage.tsx';
import { LoadingOverlay } from '@mantine/core';
import { usePageTitle } from './hooks/usePageTitle.ts';
import './App.css';

function ProtectedRoute({ children }: { children: React.ReactNode }) {
  const { isAuthenticated, isLoading } = useAuth();

  if (isLoading) {
    return <LoadingOverlay visible />;
  }

  if (!isAuthenticated) {
    return <Navigate to="/login" replace />;
  }

  return <>{children}</>;
}

function AuthLayout({ children }: { children: React.ReactNode }) {
  const { isAuthenticated, isLoading } = useAuth();

  if (isLoading) {
    return <LoadingOverlay visible />;
  }

  if (isAuthenticated) {
    return <Navigate to="/" replace />;
  }

  return <>{children}</>;
}

function AppRoutes() {
  usePageTitle();

  const [navbar, setNavbar] = useState<React.ReactNode>(null);
  const [aside, setAside] = useState<React.ReactNode>(null);

  const handleLayoutChange = useCallback((n: React.ReactNode | null, a: React.ReactNode | null) => {
    setNavbar(n);
    setAside(a);
  }, []);

  const layoutValue = useMemo(() => ({ navbar, aside }), [navbar, aside]);

  return (
    <Routes>
      {/* Auth pages - no header/sidebar */}
      <Route path="/login" element={<AuthLayout><LoginPage /></AuthLayout>} />
      {/* App pages - with header/sidebar */}
      <Route
        path="/"
        element={
          <ProtectedRoute>
            <LayoutProvider value={layoutValue}>
              <MainLayout>
                <WorkflowListPage />
              </MainLayout>
            </LayoutProvider>
          </ProtectedRoute>
        }
      />
      <Route
        path="/workflow/:id"
        element={
          <ProtectedRoute>
            <LayoutProvider value={layoutValue}>
              <MainLayout>
                <WorkflowEditorPage onLayoutChange={handleLayoutChange} />
              </MainLayout>
            </LayoutProvider>
          </ProtectedRoute>
        }
      />
      <Route
        path="/workflow/:id/history"
        element={
          <ProtectedRoute>
            <LayoutProvider value={layoutValue}>
              <MainLayout>
                <ExecutionHistoryPage />
              </MainLayout>
            </LayoutProvider>
          </ProtectedRoute>
        }
      />
      <Route
        path="/help"
        element={
          <ProtectedRoute>
            <LayoutProvider value={layoutValue}>
              <MainLayout>
                <HelpPage />
              </MainLayout>
            </LayoutProvider>
          </ProtectedRoute>
        }
      />
      <Route
        path="/settings"
        element={
          <ProtectedRoute>
            <LayoutProvider value={layoutValue}>
              <MainLayout>
                <SettingsPage />
              </MainLayout>
            </LayoutProvider>
          </ProtectedRoute>
        }
      />
      {/* Admin routes (role-protected) — must precede catch-all */}
      <Route
        path="/admin/*"
        element={
          <ProtectedRoute>
            <RequireRole role="Admin">
              <LayoutProvider value={layoutValue}>
                <MainLayout>
                  <Routes>
                    <Route path="users" element={<AdminUsersPage />} />
                    <Route path="audit" element={<AdminAuditPage />} />
                    <Route path="projects" element={<AdminProjectsPage />} />
                    <Route path="files" element={<AdminFilesPage />} />
                    <Route path="*" element={<Navigate to="/admin/users" replace />} />
                  </Routes>
                </MainLayout>
              </LayoutProvider>
            </RequireRole>
          </ProtectedRoute>
        }
      />
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  );
}

function App() {
  return (
    <BrowserRouter>
      <AuthProvider>
        <AppRoutes />
      </AuthProvider>
    </BrowserRouter>
  );
}

export default App;
