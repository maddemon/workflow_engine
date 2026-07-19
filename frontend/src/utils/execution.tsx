import { Check, X, Clock, Loader } from 'lucide-react';
import type { ExecutionStatus } from '../types/workflow';

export const statusConfig: Record<ExecutionStatus, { color: string; icon: React.ReactNode; labelKey: string }> = {
  Pending: { color: 'gray', icon: <Clock size={14} />, labelKey: 'status.pending' },
  Running: { color: 'blue', icon: <Loader size={14} speed={2} />, labelKey: 'status.running' },
  Completed: { color: 'green', icon: <Check size={14} strokeWidth={3} />, labelKey: 'status.completed' },
  DryRunCompleted: { color: 'green', icon: <Check size={14} strokeWidth={3} />, labelKey: 'status.completed' },
  Failed: { color: 'red', icon: <X size={14} strokeWidth={3} />, labelKey: 'status.failed' },
  Cancelled: { color: 'gray', icon: <X size={14} />, labelKey: 'status.cancelled' },
};

export function formatDuration(startedAt: string | null, completedAt: string | null): string | null {
  if (!startedAt) return null;
  const start = new Date(startedAt).getTime();
  const end = completedAt ? new Date(completedAt).getTime() : Date.now();
  const ms = end - start;
  if (ms < 0) return null;
  if (ms < 1000) return `${ms}ms`;
  if (ms < 60000) return `${(ms / 1000).toFixed(1)}s`;
  const minutes = Math.floor(ms / 60000);
  const seconds = Math.floor((ms % 60000) / 1000);
  return `${minutes}m ${seconds}s`;
}
