import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { WorkflowListPage } from './components/WorkflowList/WorkflowListPage.tsx';
import { WorkflowEditorPage } from './pages/WorkflowEditorPage.tsx';
import './App.css';

function App() {
  return (
    <BrowserRouter>
      <Routes>
        <Route path="/" element={<WorkflowListPage />} />
        <Route path="/workflow/:id" element={<WorkflowEditorPage />} />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </BrowserRouter>
  );
}

export default App;
