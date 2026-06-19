import { ReactFlowProvider } from '@xyflow/react';
import { useNodeTypes } from './hooks/useNodeTypes.ts';
import { useExecution } from './hooks/useExecution.ts';
import { WorkflowCanvas } from './components/Canvas/WorkflowCanvas.tsx';
import { NodePanel } from './components/NodePanel/NodePanel.tsx';
import { ParameterPanel } from './components/ParameterPanel/ParameterPanel.tsx';
import { ExecutionPanel } from './components/ExecutionPanel/ExecutionPanel.tsx';
import { AppShellLayout } from './components/Layout/AppShellLayout.tsx';
import './App.css';

function App() {
  useNodeTypes();
  const { execution, clearExecution } = useExecution();

  const aside = execution ? (
    <ExecutionPanel execution={execution} onClose={clearExecution} />
  ) : (
    <ParameterPanel />
  );

  return (
    <AppShellLayout navbar={<NodePanel />} aside={aside}>
      <ReactFlowProvider>
        <WorkflowCanvas />
      </ReactFlowProvider>
    </AppShellLayout>
  );
}

export default App;
