import { useEffect } from 'react';
import { useParams } from 'react-router-dom';
import { ReactFlowProvider } from '@xyflow/react';
import { useNodeTypes } from '../hooks/useNodeTypes.ts';
import { useExecution } from '../hooks/useExecution.ts';
import { useWorkflowStore } from '../stores/workflowStore.ts';
import { WorkflowCanvas } from '../components/Canvas/WorkflowCanvas.tsx';
import { NodePanel } from '../components/NodePanel/NodePanel.tsx';
import { ParameterPanel } from '../components/ParameterPanel/ParameterPanel.tsx';
import { ExecutionPanel } from '../components/ExecutionPanel/ExecutionPanel.tsx';
import { AppShellLayout } from '../components/Layout/AppShellLayout.tsx';

export function WorkflowEditorPage() {
  const { id } = useParams<{ id: string }>();
  useNodeTypes();
  const { execution, clearExecution } = useExecution();
  const loadWorkflow = useWorkflowStore((s) => s.loadWorkflow);
  const newWorkflow = useWorkflowStore((s) => s.newWorkflow);

  useEffect(() => {
    if (id && id !== 'new') {
      loadWorkflow(id);
    } else {
      newWorkflow();
    }
  }, [id, loadWorkflow, newWorkflow]);

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
