import { useRequest } from 'ahooks';
import { useCanvasStore } from '../components/Canvas/stores/canvasStore.ts';
import { getNodeTypes } from '../services/api.ts';

export function useNodeTypes() {
  const setNodeTypes = useCanvasStore((s) => s.setNodeTypes);
  const nodeTypes = useCanvasStore((s) => s.nodeTypes);

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
