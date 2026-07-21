import { describe, it, expect, vi, beforeEach, type Mock } from 'vitest';
import { messageHandlers, type WebSocketPushMessage, type MessageHandlerContext } from '../messageHandlers.ts';
import { useWorkflowStore } from '../../../stores/workflowStore.ts';
import { useCanvasStore } from '../../../components/Canvas/stores/canvasStore.ts';
import type { ExecutionDto } from '../../../types/workflow.ts';

vi.mock('@mantine/notifications', () => ({
  notifications: { show: vi.fn() },
}));
import { notifications } from '@mantine/notifications';

describe('messageHandlers', () => {
  let ctx: MessageHandlerContext;
  let updateExecutionMeta: Mock<(updater: (prev: ExecutionDto | null) => ExecutionDto | null) => void>;
  let sendIfOpen: Mock<(data: string) => void>;

  beforeEach(() => {
    useWorkflowStore.getState().newWorkflow();
    vi.clearAllMocks();
    updateExecutionMeta = vi.fn();
    sendIfOpen = vi.fn();
    ctx = {
      store: useCanvasStore.getState(),
      notifications,
      sendIfOpen,
      updateExecutionMeta,
    };
  });

  it('executionStarted_setsExecutingFlag', () => {
    const msg: WebSocketPushMessage = {
      type: 'execution_started',
      executionId: 'e1',
      timestamp: new Date().toISOString(),
      sequence: 1,
      payload: { workflowDefinitionId: 'wf-1' },
    };
    messageHandlers.execution_started(msg, ctx);
    expect(useCanvasStore.getState().isExecuting).toBe(true);
  });

  it('nodeStarted_withoutExistingRecord_createsTemporaryRecord', () => {
    const msg: WebSocketPushMessage = {
      type: 'node_started',
      executionId: 'e1',
      timestamp: '2024-01-01T00:00:00Z',
      sequence: 1,
      payload: { nodeDefinitionId: 'n1', runIndex: 0 },
    };
    messageHandlers.node_started(msg, ctx);
    const record = useCanvasStore.getState().nodeExecutionRecords['n1'];
    expect(record).toBeDefined();
    expect(record.status).toBe('Running');
    expect(record.startedAt).toBe('2024-01-01T00:00:00Z');
  });

  it('nodeStarted_withExistingRecord_updatesStartedAt', () => {
    const earlier = '2024-01-01T00:00:00Z';
    useCanvasStore.getState().upsertNodeExecutionRecords([{
      id: 'n1-0',
      nodeDefinitionId: 'n1',
      runIndex: 0,
      status: 'Pending',
      startedAt: earlier,
      completedAt: null,
      inputs: null,
      output: null,
      rawParameters: null,
      resolvedParameters: null,
    }]);

    const later = '2024-01-01T00:00:05Z';
    const msg: WebSocketPushMessage = {
      type: 'node_started',
      executionId: 'e1',
      timestamp: later,
      sequence: 2,
      payload: { nodeDefinitionId: 'n1', runIndex: 0 },
    };
    messageHandlers.node_started(msg, ctx);
    expect(useCanvasStore.getState().nodeExecutionRecords['n1'].startedAt).toBe(later);
  });

  it('nodeExecuted_success_updatesRecordAndNodeStatus', () => {
    useCanvasStore.getState().setNodes([{
      id: 'n1',
      type: 'workflow',
      position: { x: 0, y: 0 },
      data: {
        typeName: 'test',
        name: 'Test',
        parameters: {},
        isEntry: false,
        descriptor: {
          typeName: 'test',
          displayName: 'Test',
          category: 'Test',
          icon: '',
          executionMode: 'Sync',
          parameters: [],
          ports: [],
          defaultIsEntry: false,
        },
        errorStrategy: 'Terminate',
        retryPolicy: null,
        timeout: null,
      },
    }]);

    const msg: WebSocketPushMessage = {
      type: 'node_executed',
      executionId: 'e1',
      timestamp: '2024-01-01T00:00:10Z',
      sequence: 3,
      payload: {
        nodeDefinitionId: 'n1',
        runIndex: 0,
        result: { success: true, itemCount: 1 },
      },
    };
    messageHandlers.node_executed(msg, ctx);
    const record = useCanvasStore.getState().nodeExecutionRecords['n1'];
    expect(record.status).toBe('Completed');
    expect(useCanvasStore.getState().nodes[0].data.executionStatus).toBe('success');
  });

  it('nodeExecuted_failure_updatesRecordAsFailed', () => {
    const msg: WebSocketPushMessage = {
      type: 'node_executed',
      executionId: 'e1',
      timestamp: '2024-01-01T00:00:10Z',
      sequence: 3,
      payload: {
        nodeDefinitionId: 'n1',
        runIndex: 0,
        result: { success: false, itemCount: 0 },
      },
    };
    messageHandlers.node_executed(msg, ctx);
    expect(useCanvasStore.getState().nodeExecutionRecords['n1'].status).toBe('Failed');
  });

  it('nodeError_createsFailedRecord', () => {
    const msg: WebSocketPushMessage = {
      type: 'node_error',
      executionId: 'e1',
      timestamp: '2024-01-01T00:00:10Z',
      sequence: 3,
      payload: {
        nodeDefinitionId: 'n1',
        runIndex: 0,
        error: { code: 'ERR', message: 'node failed' },
      },
    };
    messageHandlers.node_error(msg, ctx);
    const record = useCanvasStore.getState().nodeExecutionRecords['n1'];
    expect(record.status).toBe('Failed');
    expect(record.output).toEqual({ error: { code: 'ERR', message: 'node failed' } });
  });

  it('executionCompleted_setsExecutingFalseAndUpdatesMeta', () => {
    useCanvasStore.setState({ isExecuting: true });
    const execution: ExecutionDto = {
      id: 'e1',
      workflowDefinitionId: 'wf-1',
      status: 'Running',
      startedAt: '2024-01-01T00:00:00Z',
      completedAt: null,
      nodeRecords: [],
    };

    const msg: WebSocketPushMessage = {
      type: 'execution_completed',
      executionId: 'e1',
      timestamp: '2024-01-01T00:00:10Z',
      sequence: 4,
      payload: { finalStatus: 'Completed' },
    };

    updateExecutionMeta.mockImplementation((updater) => {
      const updated = updater(execution);
      expect(updated?.status).toBe('Completed');
      expect(updated?.completedAt).toBe('2024-01-01T00:00:10Z');
    });

    messageHandlers.execution_completed(msg, ctx);
    expect(useCanvasStore.getState().isExecuting).toBe(false);
    expect(notifications.show).toHaveBeenCalled();
  });

  it('executionFailed_setsExecutingFalseAndNotifies', () => {
    useCanvasStore.setState({ isExecuting: true });
    const execution: ExecutionDto = {
      id: 'e1',
      workflowDefinitionId: 'wf-1',
      status: 'Running',
      startedAt: '2024-01-01T00:00:00Z',
      completedAt: null,
      nodeRecords: [],
    };

    const msg: WebSocketPushMessage = {
      type: 'execution_failed',
      executionId: 'e1',
      timestamp: '2024-01-01T00:00:10Z',
      sequence: 4,
      payload: { error: { code: 'ERR', message: 'workflow failed' } },
    };

    updateExecutionMeta.mockImplementation((updater) => {
      const updated = updater(execution);
      expect(updated?.status).toBe('Failed');
      expect(updated?.completedAt).toBe('2024-01-01T00:00:10Z');
    });

    messageHandlers.execution_failed(msg, ctx);
    expect(useCanvasStore.getState().isExecuting).toBe(false);
    expect(notifications.show).toHaveBeenCalledWith(expect.objectContaining({
      title: 'Execution Failed',
      message: 'workflow failed',
      color: 'red',
    }));
  });

  it('executionCancelled_setsExecutingFalseAndNotifies', () => {
    useCanvasStore.setState({ isExecuting: true });
    const execution: ExecutionDto = {
      id: 'e1',
      workflowDefinitionId: 'wf-1',
      status: 'Running',
      startedAt: '2024-01-01T00:00:00Z',
      completedAt: null,
      nodeRecords: [],
    };

    const msg: WebSocketPushMessage = {
      type: 'execution_cancelled',
      executionId: 'e1',
      timestamp: '2024-01-01T00:00:10Z',
      sequence: 4,
    };

    updateExecutionMeta.mockImplementation((updater) => {
      const updated = updater(execution);
      expect(updated?.status).toBe('Cancelled');
    });

    messageHandlers.execution_cancelled(msg, ctx);
    expect(useCanvasStore.getState().isExecuting).toBe(false);
    expect(notifications.show).toHaveBeenCalledWith(expect.objectContaining({
      title: 'Execution Cancelled',
      color: 'yellow',
    }));
  });

  it('ping_repliesWithPong', () => {
    const msg: WebSocketPushMessage = {
      type: 'ping',
      executionId: 'e1',
      timestamp: new Date().toISOString(),
      sequence: 0,
    };
    messageHandlers.ping(msg, ctx);
    expect(sendIfOpen).toHaveBeenCalledWith(JSON.stringify({ type: 'pong' }));
  });

  it('pong_isNoOp', () => {
    const msg: WebSocketPushMessage = {
      type: 'pong',
      executionId: 'e1',
      timestamp: new Date().toISOString(),
      sequence: 0,
    };
    expect(() => messageHandlers.pong(msg, ctx)).not.toThrow();
  });
});
