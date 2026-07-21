import { describe, it, expect, beforeEach } from 'vitest';
import { renderHook, act } from '@testing-library/react';
import { useWorkflowHistory } from '../useWorkflowHistory.ts';
import { useWorkflowStore } from '../../stores/workflowStore.ts';
import { useCanvasStore } from '../../components/Canvas/stores/canvasStore.ts';
import type { NodeTypeDescriptor } from '../../types/workflow.ts';

const descriptor: NodeTypeDescriptor = {
  typeName: 'test',
  displayName: 'Test',
  category: 'Test',
  icon: '',
  executionMode: 'Sync',
  parameters: [],
  ports: [],
  defaultIsEntry: false,
};

function makeNode(id: string) {
  return {
    id,
    type: 'workflow' as const,
    position: { x: 0, y: 0 },
    data: {
      typeName: descriptor.typeName,
      name: `Node ${id}`,
      parameters: {},
      isEntry: false,
      descriptor,
      errorStrategy: 'Terminate' as const,
      retryPolicy: null,
      timeout: null,
    },
  };
}

describe('useWorkflowHistory', () => {
  beforeEach(() => {
    useWorkflowStore.getState().newWorkflow();
  });

  it('initialState_undoRedoFlagsAreFalse', () => {
    const { result } = renderHook(() => useWorkflowHistory());
    expect(result.current.canUndo).toBe(false);
    expect(result.current.canRedo).toBe(false);
  });

  it('pushSnapshot_thenUndo_restoresPreviousState', () => {
    const { result } = renderHook(() => useWorkflowHistory());

    act(() => {
      useCanvasStore.getState().setNodes([makeNode('n1')]);
      result.current.pushSnapshot();
      useCanvasStore.getState().setNodes([makeNode('n1'), makeNode('n2')]);
    });

    expect(useCanvasStore.getState().nodes).toHaveLength(2);
    expect(result.current.canUndo).toBe(true);

    act(() => {
      result.current.undo();
    });

    expect(useCanvasStore.getState().nodes).toHaveLength(1);
    expect(result.current.canRedo).toBe(true);

    act(() => {
      result.current.redo();
    });

    expect(useCanvasStore.getState().nodes).toHaveLength(2);
  });
});
