import { createClient, type ApiClientOptions } from '../api/client.js';
import { getConfig, type ConfigOptions } from '../config.js';
import { CLIError, ErrorCode, ExitCode } from '../errors.js';
import { isJsonMode, isVerbose, log, verbose, writeJson } from '../output.js';
import type {
  ExecuteWorkflowRequestDto,
  ExecuteWorkflowResponseDto,
  ExecutionDto,
  ExecutionSummaryDto,
  NodeExecutionRecordDto,
} from '../types.js';
import { readFileSync } from 'node:fs';

export interface ExecuteOptions {
  workflowId: string;
  wait?: boolean;
  test?: boolean;
  timeout?: number;
  idempotencyKey?: string;
  input?: string;
  pollInterval?: number;
  expect?: string;
  profile?: string;
  configOptions?: ConfigOptions;
}

export interface ExecutionGetOptions {
  id: string;
  profile?: string;
  configOptions?: ConfigOptions;
}

export interface ExecutionListOptions {
  workflowId: string;
  page?: number;
  pageSize?: number;
  profile?: string;
  configOptions?: ConfigOptions;
}

export interface ExecutionCancelOptions {
  id: string;
  profile?: string;
  configOptions?: ConfigOptions;
}

const DEFAULT_TIMEOUT_SECONDS = 60;
const DEFAULT_POLL_INTERVAL_MS = 2000;
const TERMINAL_STATUSES = new Set(['Completed', 'Failed', 'Cancelled']);

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function createApiClient(profile?: string, configOptions?: ConfigOptions) {
  const config = getConfig(profile, configOptions);
  const options: ApiClientOptions = {
    baseURL: `${config.baseUrl}/api/v1`,
    token: config.token,
    apiKey: config.apiKey,
    verbose: isVerbose(),
  };
  return createClient(options);
}

function requireString(value: string | undefined, label: string): string {
  const trimmed = value?.trim() ?? '';
  if (trimmed.length === 0) {
    throw new CLIError(
      `缺少 ${label}`,
      ErrorCode.ValidationError,
      ExitCode.InvocationError,
    );
  }
  return trimmed;
}

function readJsonFile(filePath: string): unknown {
  let content: string;
  try {
    content = readFileSync(filePath, 'utf-8');
  } catch (err) {
    const message = err instanceof Error ? err.message : String(err);
    throw new CLIError(
      `读取文件失败：${message}`,
      ErrorCode.ConfigReadError,
      ExitCode.InvocationError,
      err,
    );
  }

  try {
    return JSON.parse(content);
  } catch (err) {
    const message = err instanceof Error ? err.message : String(err);
    throw new CLIError(
      `解析 JSON 失败：${message}`,
      ErrorCode.InvalidConfig,
      ExitCode.InvocationError,
      err,
    );
  }
}

function parseJsonString(value: string | undefined, label: string): unknown {
  if (value === undefined || value.length === 0) {
    return undefined;
  }
  try {
    return JSON.parse(value);
  } catch (err) {
    const message = err instanceof Error ? err.message : String(err);
    throw new CLIError(
      `解析 ${label} 失败：${message}`,
      ErrorCode.InvalidConfig,
      ExitCode.InvocationError,
      err,
    );
  }
}

function sleep(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

function resolveTimeoutSeconds(value: number | undefined): number {
  if (value === undefined || Number.isNaN(value) || value <= 0) {
    return DEFAULT_TIMEOUT_SECONDS;
  }
  return value;
}

function resolvePollInterval(value: number | undefined): number {
  if (value === undefined || Number.isNaN(value) || value <= 0) {
    return DEFAULT_POLL_INTERVAL_MS;
  }
  return value;
}

function getNodeKey(record: NodeExecutionRecordDto): string {
  if (record.nodeStringId && record.nodeStringId.length > 0) {
    return record.nodeStringId;
  }
  return record.nodeDefinitionId;
}

function buildNodeSummary(execution: ExecutionDto): Record<string, string> {
  const summary: Record<string, string> = {};
  for (const record of execution.nodeRecords ?? []) {
    summary[getNodeKey(record)] = record.status;
  }
  return summary;
}

interface ComparisonFailure {
  path: string;
  expected: unknown;
  actual: unknown;
}

function collectFailures(
  actual: unknown,
  expected: unknown,
  path: string,
  failures: ComparisonFailure[],
): void {
  if (typeof expected !== 'object' || expected === null) {
    if (actual !== expected) {
      failures.push({ path, expected, actual });
    }
    return;
  }

  if (Array.isArray(expected)) {
    if (!Array.isArray(actual)) {
      failures.push({ path, expected, actual });
      return;
    }
    const maxLength = Math.max(expected.length, actual.length);
    for (let i = 0; i < maxLength; i++) {
      collectFailures(actual[i], expected[i], `${path}[${i}]`, failures);
    }
    return;
  }

  if (!isRecord(actual)) {
    failures.push({ path, expected, actual });
    return;
  }

  const expectedRecord = expected as Record<string, unknown>;
  for (const [key, expectedValue] of Object.entries(expectedRecord)) {
    collectFailures(actual[key], expectedValue, path ? `${path}.${key}` : key, failures);
  }
}

function runAssertion(
  actual: Record<string, unknown>,
  expected: unknown,
): { passed: boolean; failures: ComparisonFailure[] } {
  const failures: ComparisonFailure[] = [];
  if (!isRecord(expected)) {
    failures.push({ path: '', expected, actual });
    return { passed: false, failures };
  }
  collectFailures(actual, expected, '', failures);
  return { passed: failures.length === 0, failures };
}

function buildExecutionAssertionTarget(execution: ExecutionDto): Record<string, unknown> {
  const nodes: Record<string, unknown> = {};
  for (const record of execution.nodeRecords ?? []) {
    nodes[getNodeKey(record)] = {
      status: record.status,
      output: record.output,
    };
  }
  return {
    status: execution.status,
    nodes,
  };
}

async function waitForExecution(
  client: ReturnType<typeof createClient>,
  executionId: string,
  timeoutSeconds: number,
  pollIntervalMs: number,
): Promise<ExecutionDto> {
  const start = Date.now();
  const timeoutMs = timeoutSeconds * 1000;

  while (true) {
    const response = await client.get(
      `/executions/${encodeURIComponent(executionId)}`,
    );
    const execution = response.data as ExecutionDto;

    if (TERMINAL_STATUSES.has(execution.status)) {
      return execution;
    }

    const elapsed = Date.now() - start;
    if (elapsed >= timeoutMs) {
      throw new CLIError(
        `执行超时：executionId=${executionId}`,
        ErrorCode.ExecutionTimeout,
        ExitCode.BusinessFailure,
        { executionId },
      );
    }

    const remaining = timeoutMs - elapsed;
    await sleep(Math.min(pollIntervalMs, remaining));
  }
}

function loadExpectFile(filePath: string | undefined): unknown {
  if (filePath === undefined || filePath.length === 0) {
    return undefined;
  }
  return readJsonFile(filePath);
}

export async function execute(options: ExecuteOptions): Promise<void> {
  const workflowId = requireString(options.workflowId, '工作流 ID');
  const inputs = parseJsonString(options.input, '--input');
  const idempotencyKey = options.idempotencyKey?.trim();

  const body: ExecuteWorkflowRequestDto = {};
  if (inputs !== undefined) {
    if (!isRecord(inputs)) {
      throw new CLIError(
        '--input 必须是 JSON 对象',
        ErrorCode.ValidationError,
        ExitCode.InvocationError,
      );
    }
    body.inputs = inputs;
  }
  if (idempotencyKey !== undefined && idempotencyKey.length > 0) {
    body.idempotencyKey = idempotencyKey;
  }

  const client = createApiClient(options.profile, options.configOptions);
  const response = await client.post<ExecuteWorkflowResponseDto>(
    `/workflows/${encodeURIComponent(workflowId)}/execute`,
    body,
  );
  const started = response.data;
  const executionId = started.id;

  verbose(`已开始执行：${executionId}`);

  const shouldWait = options.wait ?? false;
  const isTest = options.test ?? false;

  if (!shouldWait && !isTest) {
    const output = { executionId, status: started.status ?? 'Started' };
    if (isJsonMode()) {
      writeJson(output);
      return;
    }
    log(`已启动执行：${executionId}`);
    return;
  }

  const execution = await waitForExecution(
    client,
    executionId,
    resolveTimeoutSeconds(options.timeout),
    resolvePollInterval(options.pollInterval),
  );

  const nodeSummary = buildNodeSummary(execution);
  const expect = loadExpectFile(options.expect);

  if (isTest && expect !== undefined) {
    const target = buildExecutionAssertionTarget(execution);
    const { passed, failures } = runAssertion(target, expect);

    const result = {
      passed,
      executionId,
      status: execution.status,
      nodeSummary,
      failures,
    };

    if (isJsonMode()) {
      writeJson(result);
    } else {
      log(`执行${passed ? '通过' : '失败'}：${executionId}`);
      log(`Status: ${execution.status}`);
      for (const [nodeId, status] of Object.entries(nodeSummary)) {
        log(`  ${nodeId}: ${status}`);
      }
      if (!passed) {
        for (const failure of failures) {
          log(`  [失败] ${failure.path}`);
        }
      }
    }

    if (!passed) {
      throw new CLIError(
        '执行结果与期望不符',
        ErrorCode.AssertionFailed,
        ExitCode.BusinessFailure,
        result,
      );
    }
    return;
  }

  const output = {
    executionId,
    status: execution.status,
    nodeSummary,
    execution,
  };

  if (isJsonMode()) {
    writeJson(isTest ? { passed: true, ...output } : output);
    return;
  }

  log(`执行完成：${executionId}`);
  log(`Status: ${execution.status}`);
  if (isTest) {
    log('节点摘要：');
  }
  for (const [nodeId, status] of Object.entries(nodeSummary)) {
    log(`  ${nodeId}: ${status}`);
  }
}

export async function executionGet(options: ExecutionGetOptions): Promise<void> {
  const id = requireString(options.id, '执行 ID');
  const client = createApiClient(options.profile, options.configOptions);
  const response = await client.get<ExecutionDto>(
    `/executions/${encodeURIComponent(id)}`,
  );
  const execution = response.data;

  if (isJsonMode()) {
    writeJson(execution);
    return;
  }

  log(`Execution ID: ${execution.id}`);
  log(`Workflow ID: ${execution.workflowDefinitionId}`);
  log(`Status: ${execution.status}`);
  log(`Started At: ${execution.startedAt}`);
  if (execution.completedAt) {
    log(`Completed At: ${execution.completedAt}`);
  }

  const records = execution.nodeRecords ?? [];
  if (records.length === 0) {
    log('暂无节点记录。');
    return;
  }

  log('节点记录：');
  for (const record of records) {
    const nodeKey = getNodeKey(record);
    log(`  ${nodeKey} (${record.nodeDefinitionId})`);
    log(`    Status: ${record.status}`);
    if (record.inputs && Object.keys(record.inputs).length > 0) {
      log(`    Inputs: ${JSON.stringify(record.inputs)}`);
    }
    if (record.output !== undefined) {
      log(`    Output: ${JSON.stringify(record.output)}`);
    }
    if (record.rawParameters && Object.keys(record.rawParameters).length > 0) {
      log(`    RawParameters: ${JSON.stringify(record.rawParameters)}`);
    }
    if (record.resolvedParameters && Object.keys(record.resolvedParameters).length > 0) {
      log(`    ResolvedParameters: ${JSON.stringify(record.resolvedParameters)}`);
    }
  }
}

export async function executionList(options: ExecutionListOptions): Promise<void> {
  const workflowId = requireString(options.workflowId, '工作流 ID');
  const client = createApiClient(options.profile, options.configOptions);
  const response = await client.get<ExecutionSummaryDto[]>(
    `/workflows/${encodeURIComponent(workflowId)}/executions`,
  );
  const data: unknown = response.data;
  let executions: ExecutionSummaryDto[];
  if (Array.isArray(data)) {
    executions = data;
  } else if (isRecord(data) && Array.isArray(data.items)) {
    executions = data.items as ExecutionSummaryDto[];
  } else {
    executions = [];
  }

  // 按时间倒序，客户端分页
  executions.sort(
    (a, b) => new Date(b.startedAt).getTime() - new Date(a.startedAt).getTime(),
  );

  const page = options.page !== undefined && options.page > 0 ? options.page : 1;
  const pageSize =
    options.pageSize !== undefined && options.pageSize > 0 ? options.pageSize : 20;
  const start = (page - 1) * pageSize;
  const paged = executions.slice(start, start + pageSize);

  if (isJsonMode()) {
    writeJson({
      items: paged,
      totalCount: executions.length,
      page,
      pageSize,
      totalPages: Math.ceil(executions.length / pageSize),
    });
    return;
  }

  if (paged.length === 0) {
    log('暂无执行记录。');
    return;
  }

  for (const execution of paged) {
    const completed = execution.completedAt
      ? `, Completed: ${execution.completedAt}`
      : '';
    log(
      `${execution.id}: ${execution.status} (Started: ${execution.startedAt}${completed})`,
    );
  }
}

export async function executionCancel(options: ExecutionCancelOptions): Promise<void> {
  const id = requireString(options.id, '执行 ID');
  const client = createApiClient(options.profile, options.configOptions);
  await client.post(`/executions/${encodeURIComponent(id)}/cancel`);

  const output = { success: true, id };

  if (isJsonMode()) {
    writeJson(output);
    return;
  }

  log(`已取消执行：${id}`);
}
