import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { renderHook, waitFor, act } from '@testing-library/react';
import { useExecution } from '../useExecution.ts';
import { useWorkflowStore } from '../../stores/workflowStore.ts';
import * as api from '../../services/api.ts';
import * as serializer from '../../utils/workflowSerializer.ts';
import type { ExecutionDto, NodeExecutionRecordDto, NodeDefinition, ExecutionSummaryDto } from '../../types/workflow.ts';

vi.mock('../../services/api.ts', () => ({
  executeWorkflow: vi.fn(),
  getActiveExecutions: vi.fn(),
  getExecution: vi.fn(),
  cancelExecution: vi.fn(),
  dryRun: vi.fn(),
}));

vi.mock('../../utils/workflowSerializer.ts', () => ({
  serializeWorkflow: vi.fn(),
}));

vi.mock('../useWebSocketExecution.ts', () => ({
  useWebSocketExecution: vi.fn(() => ({
    connect: vi.fn(),
    disconnect: vi.fn(),
    subscribe: vi.fn(),
    unsubscribe: vi.fn(),
  })),
}));

vi.mock('@mantine/notifications', () => ({
  notifications: { show: vi.fn() },
}));

const mockedExecuteWorkflow = vi.mocked(api.executeWorkflow);
const mockedGetActiveExecutions = vi.mocked(api.getActiveExecutions);
const mockedGetExecution = vi.mocked(api.getExecution);
const mockedCancelExecution = vi.mocked(api.cancelExecution);
const mockedDryRun = vi.mocked(api.dryRun);
const mockedSerializeWorkflow = vi.mocked(serializer.serializeWorkflow);

function makeExecution(overrides: Partial<ExecutionDto> = {}): ExecutionDto {
  return {
    id: 'exec-1',
    workflowDefinitionId: 'wf-1',
    status: 'Running',
    startedAt: '2024-01-01T00:00:00Z',
    completedAt: null,
    nodeRecords: [],
    ...overrides,
  };
}

function makeNodeDefinition(overrides: Partial<NodeDefinition> = {}): NodeDefinition {
  return {
    id: 'n1',
    typeName: 'test',
    name: 'Test',
    parameters: {},
    ports: [],
    positionX: 0,
    positionY: 0,
    isEntry: false,
    disabled: false,
    errorStrategy: 'Terminate',
    retryPolicy: null,
    timeout: null,
    ...overrides,
  };
}

function makeRecord(status: NodeExecutionRecordDto['status'], nodeDefinitionId = 'n1'): NodeExecutionRecordDto {
  return {
    id: `${nodeDefinitionId}-0`,
    nodeDefinitionId,
    runIndex: 0,
    status,
    startedAt: '2024-01-01T00:00:00Z',
    completedAt: null,
    inputs: null,
    output: null,
    rawParameters: null,
    resolvedParameters: null,
  };
}

describe('useExecution', () => {
  beforeEach(() => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    useWorkflowStore.getState().newWorkflow();
    vi.clearAllMocks();
    mockedGetActiveExecutions.mockResolvedValue([]);
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('initialState_returnsIdle', () => {
    const { result } = renderHook(() => useExecution());
    expect(result.current.status).toBe('idle');
    expect(result.current.execution).toBeNull();
    expect(result.current.error).toBeNull();
    expect(result.current.dryRunLoading).toBe(false);
  });

  it('execute_completed_setsStatusCompleted', async () => {
    mockedExecuteWorkflow.mockResolvedValue(makeExecution({ status: 'Completed' }));
    useWorkflowStore.setState({ workflowId: 'wf-1' });

    const { result } = renderHook(() => useExecution());
    await act(async () => {
      await result.current.execute('wf-1');
    });

    expect(result.current.status).toBe('completed');
    expect(useWorkflowStore.getState().isExecuting).toBe(false);
  });

  it('execute_failed_setsStatusFailed', async () => {
    mockedExecuteWorkflow.mockResolvedValue(makeExecution({ status: 'Failed' }));
    useWorkflowStore.setState({ workflowId: 'wf-1' });

    const { result } = renderHook(() => useExecution());
    await act(async () => {
      await result.current.execute('wf-1');
    });

    expect(result.current.status).toBe('failed');
    expect(useWorkflowStore.getState().isExecuting).toBe(false);
  });

  it('execute_running_startsPolling', async () => {
    mockedExecuteWorkflow.mockResolvedValue(makeExecution({ status: 'Running' }));
    mockedGetExecution.mockResolvedValue(makeExecution({ status: 'Completed' }));
    useWorkflowStore.setState({ workflowId: 'wf-1' });

    const { result } = renderHook(() => useExecution());
    await act(async () => {
      await result.current.execute('wf-1');
    });

    expect(result.current.status).toBe('running');

    await act(async () => {
      await vi.advanceTimersByTimeAsync(2000);
    });

    expect(mockedGetExecution).toHaveBeenCalledWith('exec-1');
    expect(result.current.status).toBe('completed');
  });

  it('execute_error_setsStatusFailedWithMessage', async () => {
    mockedExecuteWorkflow.mockRejectedValue(new Error('execution failed'));
    useWorkflowStore.setState({ workflowId: 'wf-1' });

    const { result } = renderHook(() => useExecution());
    await act(async () => {
      await result.current.execute('wf-1');
    });

    expect(result.current.status).toBe('failed');
    expect(result.current.error).toBe('execution failed');
  });

  it('execute_withNodeRecords_appliesRecordsAndStatuses', async () => {
    const record = makeRecord('Completed', 'n1');
    mockedExecuteWorkflow.mockResolvedValue(makeExecution({ status: 'Completed', nodeRecords: [record] }));
    useWorkflowStore.setState({
      workflowId: 'wf-1',
      nodes: [{
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
      }],
    });

    const { result } = renderHook(() => useExecution());
    await act(async () => {
      await result.current.execute('wf-1');
    });

    expect(useWorkflowStore.getState().nodeExecutionRecords['n1']).toEqual(record);
    expect(useWorkflowStore.getState().nodes[0].data.executionStatus).toBe('success');
  });

  it('clearExecution_resetsState', async () => {
    mockedExecuteWorkflow.mockResolvedValue(makeExecution({ status: 'Running' }));
    useWorkflowStore.setState({ workflowId: 'wf-1' });

    const { result } = renderHook(() => useExecution());
    await act(async () => {
      await result.current.execute('wf-1');
    });

    act(() => {
      result.current.clearExecution();
    });

    expect(result.current.status).toBe('idle');
    expect(result.current.execution).toBeNull();
    expect(useWorkflowStore.getState().isExecuting).toBe(false);
  });

  it('cancelExecution_success_setsStatusFailed', async () => {
    mockedExecuteWorkflow.mockResolvedValue(makeExecution({ status: 'Running' }));
    mockedCancelExecution.mockResolvedValue(makeExecution({ status: 'Cancelled' }));
    useWorkflowStore.setState({ workflowId: 'wf-1' });

    const { result } = renderHook(() => useExecution());
    await act(async () => {
      await result.current.execute('wf-1');
    });

    await act(async () => {
      await result.current.cancelExecution();
    });

    expect(result.current.status).toBe('failed');
    expect(useWorkflowStore.getState().isExecuting).toBe(false);
  });

  it('cancelExecution_conflict409_fetchesLatestStatus', async () => {
    mockedExecuteWorkflow.mockResolvedValue(makeExecution({ status: 'Running' }));
    const axiosError = { response: { status: 409 } };
    mockedCancelExecution.mockRejectedValue(axiosError);
    mockedGetExecution.mockResolvedValue(makeExecution({ status: 'Completed' }));
    useWorkflowStore.setState({ workflowId: 'wf-1' });

    const { result } = renderHook(() => useExecution());
    await act(async () => {
      await result.current.execute('wf-1');
    });

    await act(async () => {
      await result.current.cancelExecution();
    });

    expect(mockedGetExecution).toHaveBeenCalledWith('exec-1');
    expect(result.current.status).toBe('completed');
  });

  it('cancelExecution_otherError_doesNotThrow', async () => {
    mockedExecuteWorkflow.mockResolvedValue(makeExecution({ status: 'Running' }));
    mockedCancelExecution.mockRejectedValue(new Error('network'));
    useWorkflowStore.setState({ workflowId: 'wf-1' });

    const { result } = renderHook(() => useExecution());
    await act(async () => {
      await result.current.execute('wf-1');
    });

    await expect(act(async () => {
      await result.current.cancelExecution();
    })).resolves.not.toThrow();
  });

  it('dryRun_success_setsStatusCompleted', async () => {
    mockedSerializeWorkflow.mockReturnValue({
      nodeDefinitions: [makeNodeDefinition()],
      connections: [],
    });
    mockedDryRun.mockResolvedValue(makeExecution({ status: 'DryRunCompleted' }));

    const { result } = renderHook(() => useExecution());
    await act(async () => {
      await result.current.dryRun();
    });

    expect(result.current.status).toBe('completed');
    expect(result.current.dryRunLoading).toBe(false);
  });

  it('dryRun_validationFails_setsError', async () => {
    useWorkflowStore.setState({
      nodes: [{
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
            parameters: [{ name: 'url', displayName: 'URL', type: 'String', required: true, defaultValue: '', validationRules: [], displayRule: null, credentialType: null, options: [] }],
            ports: [],
            defaultIsEntry: false,
          },
          errorStrategy: 'Terminate',
          retryPolicy: null,
          timeout: null,
        },
      }],
    });

    const { result } = renderHook(() => useExecution());
    await act(async () => {
      await result.current.dryRun();
    });

    expect(result.current.error).toContain('修正节点配置');
    expect(result.current.dryRunLoading).toBe(false);
  });

  it('dryRun_noNodes_setsError', async () => {
    mockedSerializeWorkflow.mockReturnValue({ nodeDefinitions: [], connections: [] });

    const { result } = renderHook(() => useExecution());
    await act(async () => {
      await result.current.dryRun();
    });

    expect(result.current.error).toContain('添加节点');
    expect(result.current.dryRunLoading).toBe(false);
  });

  it('dryRun_failure_showsFailedNotification', async () => {
    mockedSerializeWorkflow.mockReturnValue({
      nodeDefinitions: [makeNodeDefinition()],
      connections: [],
    });
    const record = makeRecord('Failed', 'n1');
    record.output = { error: { code: 'ERR', message: 'node error' } };
    mockedDryRun.mockResolvedValue(makeExecution({ status: 'Failed', nodeRecords: [record] }));

    const { result } = renderHook(() => useExecution());
    await act(async () => {
      await result.current.dryRun();
    });

    expect(result.current.status).toBe('failed');
  });

  it('dryRun_error_setsError', async () => {
    mockedSerializeWorkflow.mockReturnValue({
      nodeDefinitions: [makeNodeDefinition()],
      connections: [],
    });
    mockedDryRun.mockRejectedValue(new Error('dry run failed'));

    const { result } = renderHook(() => useExecution());
    await act(async () => {
      await result.current.dryRun();
    });

    expect(result.current.error).toBe('dry run failed');
    expect(result.current.dryRunLoading).toBe(false);
  });

  it('mount_checksRunningExecutionsAndSubscribes', async () => {
    const running = makeExecution({ id: 'exec-2', status: 'Running' });
    const summary: ExecutionSummaryDto = {
      id: 'exec-2',
      workflowDefinitionId: 'wf-1',
      status: 'Running',
      startedAt: '2024-01-01T00:00:00Z',
      completedAt: null,
    };
    mockedGetActiveExecutions.mockResolvedValue([summary]);
    mockedGetExecution.mockResolvedValue(running);
    useWorkflowStore.setState({ workflowId: 'wf-1' });

    renderHook(() => useExecution());

    await waitFor(() => expect(mockedGetActiveExecutions).toHaveBeenCalledWith('wf-1'));
    await waitFor(() => expect(mockedGetExecution).toHaveBeenCalledWith('exec-2'));
  });

  it('mount_noWorkflowId_doesNotCheck', async () => {
    renderHook(() => useExecution());
    vi.advanceTimersByTime(1000);
    expect(mockedGetActiveExecutions).not.toHaveBeenCalled();
  });

  it('polling_reachesTerminalStatus_stopsAndUpdates', async () => {
    mockedExecuteWorkflow.mockResolvedValue(makeExecution({ status: 'Running' }));
    mockedGetExecution.mockResolvedValue(makeExecution({ status: 'Completed' }));
    useWorkflowStore.setState({ workflowId: 'wf-1' });

    const { result } = renderHook(() => useExecution());
    await act(async () => {
      await result.current.execute('wf-1');
    });

    await act(async () => {
      await vi.advanceTimersByTimeAsync(2000);
    });

    expect(result.current.status).toBe('completed');
  });

  it('polling_errorIsIgnored', async () => {
    mockedExecuteWorkflow.mockResolvedValue(makeExecution({ status: 'Running' }));
    mockedGetExecution.mockRejectedValue(new Error('network'));
    useWorkflowStore.setState({ workflowId: 'wf-1' });

    const { result } = renderHook(() => useExecution());
    await act(async () => {
      await result.current.execute('wf-1');
    });

    await act(async () => {
      await vi.advanceTimersByTimeAsync(2000);
    });

    expect(result.current.status).toBe('running');
  });
});
