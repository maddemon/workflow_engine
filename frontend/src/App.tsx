import { useState, useMemo, useCallback } from 'react';
import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { LayoutProvider } from './components/Layout/LayoutContext.tsx';
import { MainLayout } from './components/Layout/MainLayout.tsx';
import { AuthProvider, useAuth } from './hooks/AuthContext.tsx';
import { WorkflowListPage } from './components/WorkflowList/WorkflowListPage.tsx';
import { WorkflowEditorPage } from './pages/WorkflowEditorPage.tsx';
import { ExecutionHistoryPage } from './pages/ExecutionHistoryPage.tsx';
import { LoginPage } from './pages/LoginPage.tsx';
import { LoadingOverlay } from '@mantine/core';
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
