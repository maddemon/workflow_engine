import { useState, useMemo, useCallback } from 'react';
import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { LayoutProvider } from './components/Layout/LayoutContext.tsx';
import { MainLayout } from './components/Layout/MainLayout.tsx';
import { WorkflowListPage } from './components/WorkflowList/WorkflowListPage.tsx';
import { WorkflowEditorPage } from './pages/WorkflowEditorPage.tsx';
import './App.css';

function App() {
  const [navbar, setNavbar] = useState<React.ReactNode>(null);
  const [aside, setAside] = useState<React.ReactNode>(null);

  const handleLayoutChange = useCallback((n: React.ReactNode | null, a: React.ReactNode | null) => {
    setNavbar(n);
    setAside(a);
  }, []);

  const layoutValue = useMemo(() => ({ navbar, aside }), [navbar, aside]);

  return (
    <BrowserRouter>
      <LayoutProvider value={layoutValue}>
        <MainLayout>
          <Routes>
            <Route path="/" element={<WorkflowListPage />} />
            <Route
              path="/workflow/:id"
              element={<WorkflowEditorPage onLayoutChange={handleLayoutChange} />}
            />
            <Route path="*" element={<Navigate to="/" replace />} />
          </Routes>
        </MainLayout>
      </LayoutProvider>
    </BrowserRouter>
  );
}

export default App;
