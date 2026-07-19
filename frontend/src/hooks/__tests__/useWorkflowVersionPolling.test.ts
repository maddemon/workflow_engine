import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { renderHook, act } from '@testing-library/react';
import { useWorkflowVersionPolling } from '../useWorkflowVersionPolling.ts';
import { useWorkflowStore } from '../../stores/workflowStore.ts';
import * as api from '../../services/api.ts';
import type { Workflow } from '../../types/workflow.ts';

vi.mock('../../services/api.ts', () => ({
  getWorkflow: vi.fn(),
}));

const mockedGetWorkflow = vi.mocked(api.getWorkflow);

describe('useWorkflowVersionPolling', () => {
  beforeEach(() => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    useWorkflowStore.getState().newWorkflow();
    vi.clearAllMocks();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('noWorkflowId_doesNotPoll', () => {
    renderHook(() => useWorkflowVersionPolling(null));
    vi.advanceTimersByTime(60000);
    expect(mockedGetWorkflow).not.toHaveBeenCalled();
  });

  it('newWorkflowId_doesNotPoll', () => {
    renderHook(() => useWorkflowVersionPolling('new'));
    vi.advanceTimersByTime(60000);
    expect(mockedGetWorkflow).not.toHaveBeenCalled();
  });

  it('reviewMode_doesNotPoll', () => {
    useWorkflowStore.setState({ reviewMode: true, workflowVersion: 1 });
    renderHook(() => useWorkflowVersionPolling('wf-1'));
    vi.advanceTimersByTime(60000);
    expect(mockedGetWorkflow).not.toHaveBeenCalled();
  });

  it('isExecuting_doesNotPoll', () => {
    useWorkflowStore.setState({ isExecuting: true, workflowVersion: 1 });
    renderHook(() => useWorkflowVersionPolling('wf-1'));
    vi.advanceTimersByTime(60000);
    expect(mockedGetWorkflow).not.toHaveBeenCalled();
  });

  it('pollingDetectsHigherVersion_setsChanged', async () => {
    useWorkflowStore.setState({ workflowVersion: 1 });
    mockedGetWorkflow.mockResolvedValue({ id: 'wf-1', version: 2 } as unknown as Workflow);

    const { result } = renderHook(() => useWorkflowVersionPolling('wf-1'));
    expect(result.current.changed).toBe(false);

    await act(async () => {
      vi.advanceTimersByTime(30000);
      await vi.advanceTimersByTimeAsync(0);
    });

    expect(mockedGetWorkflow).toHaveBeenCalledWith('wf-1');
    expect(result.current.changed).toBe(true);
    expect(result.current.newVersion).toBe(2);
  });

  it('pollingError_isSilentlyIgnored', async () => {
    useWorkflowStore.setState({ workflowVersion: 1 });
    mockedGetWorkflow.mockRejectedValue(new Error('network'));

    renderHook(() => useWorkflowVersionPolling('wf-1'));
    await act(async () => {
      vi.advanceTimersByTime(30000);
      await vi.advanceTimersByTimeAsync(0);
    });

    expect(mockedGetWorkflow).toHaveBeenCalled();
    // no throw, test passes
  });

  it('dismiss_clearsChangedState', async () => {
    useWorkflowStore.setState({ workflowVersion: 1 });
    mockedGetWorkflow.mockResolvedValue({ id: 'wf-1', version: 2 } as unknown as Workflow);

    const { result } = renderHook(() => useWorkflowVersionPolling('wf-1'));
    await act(async () => {
      vi.advanceTimersByTime(30000);
      await vi.advanceTimersByTimeAsync(0);
    });

    expect(result.current.changed).toBe(true);

    act(() => {
      result.current.dismiss();
    });

    expect(result.current.changed).toBe(false);
    expect(result.current.newVersion).toBeNull();
  });
});
