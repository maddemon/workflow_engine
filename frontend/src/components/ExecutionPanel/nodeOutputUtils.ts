/**
 * Check if a node output contains agent execution data.
 */
export function isAgentOutput(output: unknown): boolean {
  if (!output || typeof output !== 'object') return false;
  const obj = output as Record<string, unknown>;
  return typeof obj.agentInfo === 'object' && obj.agentInfo !== null;
}

export function extractError(output: unknown): { code?: string; message?: string } | null {
  if (!output || typeof output !== 'object') return null;
  const obj = output as Record<string, unknown>;
  if (obj.error && typeof obj.error === 'object') {
    const err = obj.error as Record<string, unknown>;
    return { code: String(err.code ?? ''), message: String(err.message ?? '') };
  }
  if (obj.output && typeof obj.output === 'object') {
    const out = obj.output as Record<string, unknown>;
    const items = out.items;
    if (Array.isArray(items) && items.length > 0) {
      const first = items[0] as Record<string, unknown>;
      if (first.error && typeof first.error === 'object') {
        const err = first.error as Record<string, unknown>;
        return { code: String(err.code ?? ''), message: String(err.message ?? '') };
      }
      if (first.success === false && first.error) {
        const err = first.error as Record<string, unknown>;
        return { code: String(err.code ?? ''), message: String(err.message ?? '') };
      }
      if (first.success === false && !first.error) {
        return { message: 'Node execution failed.' };
      }
    }
  }
  return null;
}

export function formatDuration(startedAt: string | null, completedAt: string | null): string | null {
  if (!startedAt) return null;
  const start = new Date(startedAt).getTime();
  const end = completedAt ? new Date(completedAt).getTime() : Date.now();
  const ms = end - start;
  // 如果开始和结束时间相同（后端未提供真实开始时间），不显示时长
  if (ms <= 0) return null;
  if (ms < 1000) return `${ms}ms`;
  return `${(ms / 1000).toFixed(1)}s`;
}

export function formatOutputSummary(output: unknown): string {
  if (output === null || output === undefined) return 'No output';
  if (typeof output === 'string') return output.length > 100 ? `${output.slice(0, 100)}...` : output;
  if (typeof output === 'number' || typeof output === 'boolean') return String(output);

  try {
    const obj = output as Record<string, unknown>;

    // Handle execution result format { success, output, error }
    if (typeof obj === 'object' && obj !== null && 'success' in obj) {
      const success = obj.success as boolean;
      const innerOutput = obj.output;
      const error = obj.error;

      if (!success && error) {
        const errorMsg = typeof error === 'object' ? (error as Record<string, unknown>).message : error;
        return `Error: ${String(errorMsg).slice(0, 80)}`;
      }

      if (innerOutput !== undefined && innerOutput !== null) {
        if (typeof innerOutput === 'string') {
          return innerOutput.length > 100 ? `${innerOutput.slice(0, 100)}...` : innerOutput;
        }
        if (typeof innerOutput === 'object') {
          const str = JSON.stringify(innerOutput);
          return str.length > 100 ? `${str.slice(0, 100)}...` : str;
        }
        return String(innerOutput);
      }

      return success ? 'Success' : 'Failed';
    }

    // Handle array
    if (Array.isArray(obj)) {
      return obj.length === 0 ? 'Empty array' : `Array(${obj.length} items)`;
    }

    // Handle object
    if (typeof obj === 'object' && obj !== null) {
      const str = JSON.stringify(obj);
      return str.length > 100 ? `${str.slice(0, 100)}...` : str;
    }

    return String(obj);
  } catch {
    const str = JSON.stringify(output);
    return str.length > 100 ? `${str.slice(0, 100)}...` : str;
  }
}
