import { notifications } from '@mantine/notifications';
import { useWorkflowStore } from '../../stores/workflowStore.ts';
import type { NodeExecutionRecordDto } from '../../types/workflow.ts';

export type WebSocketStatus = 'connecting' | 'connected' | 'disconnected' | 'error';

export interface WebSocketPushMessage {
  type: string;
  executionId: string;
  timestamp: string;
  sequence: number;
  payload?: {
    workflowDefinitionId?: string;
    nodeDefinitionId?: string;
    runIndex?: number;
    result?: {
      success: boolean;
      itemCount: number;
      error?: { code: string; message: string };
    };
    error?: { code: string; message: string };
    finalStatus?: string;
    eventType?: string;
  };
}

export interface MessageHandlerContext {
  store: ReturnType<typeof useWorkflowStore.getState>;
  notifications: typeof notifications;
  sendIfOpen: (data: string) => void;
}

export const messageHandlers: Record<string, (msg: WebSocketPushMessage, ctx: MessageHandlerContext) => void> = {
  execution_started: (msg, ctx) => {
    const { store } = ctx;
    if (msg.payload?.workflowDefinitionId) {
      store.setIsExecuting(true);
    }
  },

  node_started: (msg, ctx) => {
    const { store } = ctx;
    if (msg.payload?.nodeDefinitionId) {
      const nodeDefId = msg.payload.nodeDefinitionId;
      store.updateNodeExecutionStatus(nodeDefId, 'running');

      // 更新节点的 startedAt 时间
      const existingRecord = store.nodeExecutionRecords[nodeDefId];
      if (existingRecord) {
        store.upsertNodeExecutionRecords([{
          ...existingRecord,
          startedAt: msg.timestamp,
        }]);
      } else {
        // 创建临时记录，等 node_executed 事件补充完整信息
        const tempRecord: NodeExecutionRecordDto = {
          id: `${nodeDefId}-${msg.payload.runIndex ?? 0}`,
          nodeDefinitionId: nodeDefId,
          runIndex: msg.payload.runIndex ?? 0,
          status: 'Running',
          startedAt: msg.timestamp,
          completedAt: null,
          inputs: null,
          output: null,
          rawParameters: null,
          resolvedParameters: null,
        };
        store.upsertNodeExecutionRecords([tempRecord]);
      }
    }
  },

  node_executed: (msg, ctx) => {
    const { store } = ctx;
    if (msg.payload?.nodeDefinitionId && msg.payload?.result) {
      const { nodeDefinitionId, result } = msg.payload;
      const status = result.success ? 'success' : 'error';
      store.updateNodeExecutionStatus(nodeDefinitionId, status);

      // 使用已有的 startedAt（来自 node_started 事件），如果不存在则使用 message.timestamp
      const existingRecord = store.nodeExecutionRecords[nodeDefinitionId];
      const startedAt = existingRecord?.startedAt ?? msg.timestamp;

      const record: NodeExecutionRecordDto = {
        id: `${nodeDefinitionId}-${msg.payload.runIndex ?? 0}`,
        nodeDefinitionId,
        runIndex: msg.payload.runIndex ?? 0,
        status: result.success ? 'Completed' : 'Failed',
        startedAt,
        completedAt: msg.timestamp,
        inputs: null,
        output: result,
        rawParameters: null,
        resolvedParameters: null,
      };
      store.upsertNodeExecutionRecords([record]);
    }
  },

  node_error: (msg, ctx) => {
    const { store } = ctx;
    if (msg.payload?.nodeDefinitionId && msg.payload?.error) {
      const { nodeDefinitionId, error } = msg.payload;
      store.updateNodeExecutionStatus(nodeDefinitionId, 'error');

      // 使用已有的 startedAt（来自 node_started 事件），如果不存在则使用 message.timestamp
      const existingRecord = store.nodeExecutionRecords[nodeDefinitionId];
      const startedAt = existingRecord?.startedAt ?? msg.timestamp;

      const record: NodeExecutionRecordDto = {
        id: `${nodeDefinitionId}-${msg.payload.runIndex ?? 0}`,
        nodeDefinitionId,
        runIndex: msg.payload.runIndex ?? 0,
        status: 'Failed',
        startedAt,
        completedAt: msg.timestamp,
        inputs: null,
        output: { error },
        rawParameters: null,
        resolvedParameters: null,
      };
      store.upsertNodeExecutionRecords([record]);
    }
  },

  execution_completed: (msg, ctx) => {
    const { store, notifications: notif } = ctx;
    store.setIsExecuting(false);
    if (msg.payload?.finalStatus === 'Completed') {
      notif.show({
        title: 'Execution Complete',
        message: 'Workflow execution completed successfully.',
        color: 'green',
      });
    }
  },

  execution_failed: (msg, ctx) => {
    const { store, notifications: notif } = ctx;
    store.setIsExecuting(false);
    notif.show({
      title: 'Execution Failed',
      message: msg.payload?.error?.message ?? 'Workflow execution failed.',
      color: 'red',
    });
  },

  execution_cancelled: (_msg, ctx) => {
    const { store, notifications: notif } = ctx;
    store.setIsExecuting(false);
    notif.show({
      title: 'Execution Cancelled',
      message: 'Workflow execution was cancelled.',
      color: 'yellow',
    });
  },

  pong: () => {
    // 心跳响应，无需处理
  },

  ping: (_msg, ctx) => {
    ctx.sendIfOpen(JSON.stringify({ type: 'pong' }));
  },
};
