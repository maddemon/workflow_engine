import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { renderHook, act } from '@testing-library/react';
import { useWorkflowVersionPolling } from '../useWorkflowVersionPolling.ts';
import { useWorkflowStore } from '../../stores/workflowStore.ts';
import { useCanvasStore } from '../../components/Canvas/stores/canvasStore.ts';
import * as api from '../../services/api.ts';
import type { WorkflowVersion } from '../../types/workflow.ts';

vi.mock('../../services/api.ts', () => ({
  getWorkflowVersion: vi.fn(),
  getWorkflow: vi.fn(),
}));

const mockedGetWorkflowVersion = vi.mocked(api.getWorkflowVersion);
const mockedGetWorkflow = vi.mocked(api.getWorkflow);

/** 构造仅 `version` 不同的最小 WorkflowVersion 对象，供 mock getWorkflowVersion 使用。 */
function makeWorkflowVersion(version: number): WorkflowVersion {
  return {
    id: 'wf-1',
    version,
    updatedAt: null,
  };
}

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
    expect(mockedGetWorkflowVersion).not.toHaveBeenCalled();
    expect(mockedGetWorkflow).not.toHaveBeenCalled();
  });

  it('newWorkflowId_doesNotPoll', () => {
    renderHook(() => useWorkflowVersionPolling('new'));
    vi.advanceTimersByTime(60000);
    expect(mockedGetWorkflowVersion).not.toHaveBeenCalled();
    expect(mockedGetWorkflow).not.toHaveBeenCalled();
  });

  it('reviewMode_doesNotPoll', () => {
    useCanvasStore.setState({ reviewMode: true });
    useWorkflowStore.setState({ workflowVersion: 1 });
    renderHook(() => useWorkflowVersionPolling('wf-1'));
    vi.advanceTimersByTime(60000);
    expect(mockedGetWorkflowVersion).not.toHaveBeenCalled();
    expect(mockedGetWorkflow).not.toHaveBeenCalled();
  });

  it('isExecuting_doesNotPoll', () => {
    useCanvasStore.setState({ isExecuting: true });
    useWorkflowStore.setState({ workflowVersion: 1 });
    renderHook(() => useWorkflowVersionPolling('wf-1'));
    vi.advanceTimersByTime(60000);
    expect(mockedGetWorkflowVersion).not.toHaveBeenCalled();
    expect(mockedGetWorkflow).not.toHaveBeenCalled();
  });

  it('pollingCallsVersionEndpoint_notGetWorkflow', async () => {
    useWorkflowStore.setState({ workflowVersion: 1 });
    mockedGetWorkflowVersion.mockResolvedValue(makeWorkflowVersion(2));

    const { result } = renderHook(() => useWorkflowVersionPolling('wf-1'));
    expect(result.current.changed).toBe(false);

    await act(async () => {
      vi.advanceTimersByTime(30000);
      await vi.advanceTimersByTimeAsync(0);
    });

    // 轮询应调用轻量 version 接口而非全量 getWorkflow
    expect(mockedGetWorkflowVersion).toHaveBeenCalledWith('wf-1');
    expect(mockedGetWorkflow).not.toHaveBeenCalled();
    expect(result.current.changed).toBe(true);
    expect(result.current.newVersion).toBe(2);
  });

  it('pollingDetectsHigherVersion_setsChanged', async () => {
    useWorkflowStore.setState({ workflowVersion: 1 });
    mockedGetWorkflowVersion.mockResolvedValue(makeWorkflowVersion(2));

    const { result } = renderHook(() => useWorkflowVersionPolling('wf-1'));
    expect(result.current.changed).toBe(false);

    await act(async () => {
      vi.advanceTimersByTime(30000);
      await vi.advanceTimersByTimeAsync(0);
    });

    expect(mockedGetWorkflowVersion).toHaveBeenCalledWith('wf-1');
    expect(result.current.changed).toBe(true);
    expect(result.current.newVersion).toBe(2);
  });

  it('pollingError_isSilentlyIgnored', async () => {
    useWorkflowStore.setState({ workflowVersion: 1 });
    mockedGetWorkflowVersion.mockRejectedValue(new Error('network'));

    renderHook(() => useWorkflowVersionPolling('wf-1'));
    await act(async () => {
      vi.advanceTimersByTime(30000);
      await vi.advanceTimersByTimeAsync(0);
    });

    expect(mockedGetWorkflowVersion).toHaveBeenCalled();
    // no throw, test passes
  });

  it('dismiss_clearsChangedState', async () => {
    useWorkflowStore.setState({ workflowVersion: 1 });
    mockedGetWorkflowVersion.mockResolvedValue(makeWorkflowVersion(2));

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

  it('dismiss - advances baseline version so the dismissed version is not re-flagged on next poll', async () => {
    // 后端持续返回较高版本（5），模拟他人已保存的新版本。
    vi.spyOn(api, 'getWorkflowVersion').mockResolvedValue(makeWorkflowVersion(5));

    const { result } = renderHook(() => useWorkflowVersionPolling('wf-1'));

    // 第一次轮询：检测到新版本 5，应置 changed=true。
    await act(async () => {
      await vi.advanceTimersByTimeAsync(30000);
    });
    expect(result.current.changed).toBe(true);
    expect(result.current.newVersion).toBe(5);

    // 用户在提示上点击「忽略」。
    act(() => {
      result.current.dismiss();
    });
    expect(result.current.changed).toBe(false);
    expect(result.current.newVersion).toBeNull();

    // 下一次轮询仍返回同一高版本（5）。
    await act(async () => {
      await vi.advanceTimersByTimeAsync(30000);
    });

    // 修复前：基线未推进，5 > 旧基线 仍成立，会再次 changed=true（复现每周期重复提示）。
    // 修复后：基线已推进到 5，5 > 5 不成立，不应再次提示。
    expect(result.current.changed).toBe(false);
  });
});
