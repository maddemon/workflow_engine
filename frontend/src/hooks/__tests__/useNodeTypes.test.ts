import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { useNodeTypes } from '../useNodeTypes.ts';
import { useWorkflowStore } from '../../stores/workflowStore.ts';
import { useCanvasStore } from '../../components/Canvas/stores/canvasStore.ts';
import type { NodeTypeDescriptor } from '../../types/workflow.ts';

vi.mock('../../services/api.ts', () => ({
  getNodeTypes: vi.fn(),
}));

import { getNodeTypes } from '../../services/api.ts';
const mockedGetNodeTypes = vi.mocked(getNodeTypes);

const descriptor: NodeTypeDescriptor = {
  typeName: 'httpRequest',
  displayName: 'HTTP Request',
  category: 'Http',
  icon: 'globe',
  executionMode: 'Sync',
  parameters: [],
  ports: [],
  defaultIsEntry: true,
};

function resetStore() {
  useWorkflowStore.getState().newWorkflow();
  useCanvasStore.setState({ nodeTypes: [] });
}

describe('useNodeTypes', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    resetStore();
  });

  it('loads node types and sets them in store', async () => {
    mockedGetNodeTypes.mockResolvedValue([descriptor]);

    const { result } = renderHook(() => useNodeTypes());

    await waitFor(() => {
      expect(result.current.ready).toBe(true);
    });

    expect(result.current.nodeTypes).toEqual([descriptor]);
    expect(useCanvasStore.getState().nodeTypes).toEqual([descriptor]);
  });

  it('ready is false while loading and when store already has types', async () => {
    useCanvasStore.setState({ nodeTypes: [descriptor] });
    mockedGetNodeTypes.mockResolvedValue([descriptor]);

    const { result } = renderHook(() => useNodeTypes());

    await waitFor(() => {
      expect(mockedGetNodeTypes).toHaveBeenCalled();
    });

    expect(result.current.ready).toBe(true);
    expect(result.current.nodeTypes).toEqual([descriptor]);
  });

  it('ready is false when node types list is empty', async () => {
    mockedGetNodeTypes.mockResolvedValue([]);

    const { result } = renderHook(() => useNodeTypes());

    await waitFor(() => {
      expect(mockedGetNodeTypes).toHaveBeenCalled();
    });

    expect(result.current.ready).toBe(false);
  });
});
