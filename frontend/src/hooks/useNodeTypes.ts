import { useEffect } from 'react';
import { useWorkflowStore } from '../stores/workflowStore.ts';
import { getNodeTypes } from '../services/api.ts';

export function useNodeTypes() {
  const setNodeTypes = useWorkflowStore((s) => s.setNodeTypes);
  const nodeTypes = useWorkflowStore((s) => s.nodeTypes);

  useEffect(() => {
    let cancelled = false;
    getNodeTypes()
      .then((data) => {
        if (!cancelled) setNodeTypes(data);
      })
      .catch((err) => {
        console.error('Failed to load node types:', err);
      });
    return () => { cancelled = true; };
  }, [setNodeTypes]);

  return { nodeTypes };
}
