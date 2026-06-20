import { useEffect, useRef, useMemo } from 'react';
import { useParams } from 'react-router-dom';
import { ReactFlowProvider } from '@xyflow/react';
import { useNodeTypes } from '../hooks/useNodeTypes.ts';
import { useExecution } from '../hooks/useExecution.ts';
import { useWorkflowStore } from '../stores/workflowStore.ts';
import { WorkflowCanvas } from '../components/Canvas/WorkflowCanvas.tsx';
import { NodePanel } from '../components/NodePanel/NodePanel.tsx';
import { ParameterPanel } from '../components/ParameterPanel/ParameterPanel.tsx';
import { ExecutionPanel } from '../components/ExecutionPanel/ExecutionPanel.tsx';

interface WorkflowEditorPageProps {
  onLayoutChange?: (navbar: React.ReactNode | null, aside: React.ReactNode | null) => void;
}

export function WorkflowEditorPage({ onLayoutChange }: WorkflowEditorPageProps) {
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

  const navbar = useMemo(() => <NodePanel />, []);

  const aside = useMemo(() => {
    if (execution) {
      return <ExecutionPanel execution={execution} onClose={clearExecution} />;
    }
    return <ParameterPanel />;
  }, [execution, clearExecution]);

  const asideKey = execution?.id ?? 'default';
  const prevKeyRef = useRef<string>(asideKey);

  useEffect(() => {
    if (prevKeyRef.current !== asideKey) {
      prevKeyRef.current = asideKey;
      onLayoutChange?.(navbar, aside);
    }
  }, [asideKey, navbar, aside, onLayoutChange]);

  useEffect(() => {
    onLayoutChange?.(navbar, aside);
    return () => onLayoutChange?.(null, null);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  return (
    <ReactFlowProvider>
      <WorkflowCanvas />
    </ReactFlowProvider>
  );
}
