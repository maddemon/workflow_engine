import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook, act } from '@testing-library/react';
import { useWebSocketExecution } from '../useWebSocketExecution';

describe('useWebSocketExecution', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('should initialize with disconnected status', () => {
    const { result } = renderHook(() => useWebSocketExecution());
    expect(result.current.status).toBe('disconnected');
  });

  it('should expose connect, disconnect, subscribe, unsubscribe functions', () => {
    const { result } = renderHook(() => useWebSocketExecution());
    expect(typeof result.current.connect).toBe('function');
    expect(typeof result.current.disconnect).toBe('function');
    expect(typeof result.current.subscribe).toBe('function');
    expect(typeof result.current.unsubscribe).toBe('function');
  });

  it('should start with lastSequence 0', () => {
    const { result } = renderHook(() => useWebSocketExecution());
    expect(result.current.lastSequence).toBe(0);
  });

  it('disconnect should set status to disconnected', () => {
    const { result } = renderHook(() => useWebSocketExecution());
    act(() => {
      result.current.disconnect();
    });
    expect(result.current.status).toBe('disconnected');
  });
});
