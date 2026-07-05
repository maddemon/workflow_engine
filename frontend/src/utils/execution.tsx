import { Check, X, Clock, Loader } from 'lucide-react';
import type { ExecutionStatus } from '../types/workflow';

export const statusConfig: Record<ExecutionStatus, { color: string; icon: React.ReactNode; label: string }> = {
  Pending: { color: 'gray', icon: <Clock size={14} />, label: 'Pending' },
  Running: { color: 'blue', icon: <Loader size={14} speed={2} />, label: 'Running' },
  Completed: { color: 'green', icon: <Check size={14} strokeWidth={3} />, label: 'Completed' },
  Failed: { color: 'red', icon: <X size={14} strokeWidth={3} />, label: 'Failed' },
  Cancelled: { color: 'gray', icon: <X size={14} />, label: 'Cancelled' },
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
