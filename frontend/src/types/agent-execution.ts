import type { ExecutionStatus } from './workflow.ts';

export type { ExecutionStatus };

/**
 * Agent execution information from the backend.
 */
export interface AgentExecutionInfo {
  model: string;
  iterationCount: number;
  status: ExecutionStatus;
  startedAt: string | null;
  completedAt: string | null;
  errorMessage?: string | null;
  tokenUsage?: TokenUsage | null;
}

/**
 * Token usage statistics.
 */
export interface TokenUsage {
  promptTokens: number;
  completionTokens: number;
  totalTokens: number;
}

/**
 * A single tool call record within an agent iteration.
 */
export interface ToolCallRecord {
  id: string;
  toolName: string;
  input: unknown;
  output: unknown;
  status: ExecutionStatus;
  duration: number | null;
  error?: string | null;
}

/**
 * A single LLM response chunk (for streaming display).
 */
export interface LLMChunk {
  content: string;
  role: 'assistant' | 'system' | 'user';
  timestamp: string;
}

/**
 * A single iteration within an agent execution.
 * Each iteration contains one LLM call and zero or more tool calls.
 */
export interface AgentIteration {
  index: number;
  llmChunks: LLMChunk[];
  toolCalls: ToolCallRecord[];
  startedAt: string | null;
  completedAt: string | null;
}

/**
 * Sub-record for nested agent calls (child agents called by parent).
 */
export interface SubRecord {
  parentId: string;
  agentName: string;
  records: AgentIteration[];
  status: ExecutionStatus;
}

/**
 * Complete agent execution data for rendering.
 */
export interface AgentExecutionData {
  agentInfo: AgentExecutionInfo;
  iterations: AgentIteration[];
  subRecords: SubRecord[];
  systemPrompt?: string | null;
}
