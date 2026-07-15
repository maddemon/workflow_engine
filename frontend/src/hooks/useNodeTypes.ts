import { useRequest } from 'ahooks';
import { useWorkflowStore } from '../stores/workflowStore.ts';
import { getNodeTypes } from '../services/api.ts';

export function useNodeTypes() {
  const setNodeTypes = useWorkflowStore((s) => s.setNodeTypes);
  const nodeTypes = useWorkflowStore((s) => s.nodeTypes);

  const { loading } = useRequest(getNodeTypes, {
    onSuccess: (data) => {
      setNodeTypes(data);
    },
    onError: (err) => {
      console.error('Failed to load node types:', err);
    },
  });

  return { nodeTypes, ready: !loading && nodeTypes.length > 0 };
}
