import { createContext } from 'react';

/**
 * nodeId → 已连接 handleId 集合的映射。由 WorkflowCanvas 基于 edges 一次性计算
 * （O(E)），通过 Context 下发给每个 CustomNode，避免每个节点在渲染时各自执行
 * O(N×E) 的 edges.filter。
 */
export const ConnectedHandlesContext = createContext<Record<string, Set<string>>>({});
