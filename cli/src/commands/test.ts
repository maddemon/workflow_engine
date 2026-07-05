import { createClient, type ApiClientOptions } from '../api/client.js';
import { getConfig, type ConfigOptions } from '../config.js';
import { CLIError, ErrorCode, ExitCode } from '../errors.js';
import { isJsonMode, isVerbose, log, writeJson } from '../output.js';
import type {
  DryRunCredentialDto,
  DryRunResultDto,
  DryRunWorkflowRequestDto,
} from '../types.js';
import { readFileSync } from 'node:fs';

export interface TestOptions {
  file: string;
  expect?: string;
  credentials?: string;
  timeout?: number;
  projectId?: string;
  profile?: string;
  configOptions?: ConfigOptions;
}

const DEFAULT_TIMEOUT_SECONDS = 60;

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

function parseCredentials(value: string | undefined): DryRunCredentialDto[] | undefined {
  if (value === undefined || value.length === 0) {
    return undefined;
  }

  const parsed = parseJsonString(value, '--credentials');
  if (!isRecord(parsed)) {
    throw new CLIError(
      '--credentials 必须是 JSON 对象',
      ErrorCode.ValidationError,
      ExitCode.InvocationError,
    );
  }

  const credentials: DryRunCredentialDto[] = [];
  for (const [name, credential] of Object.entries(parsed)) {
    if (!isRecord(credential)) {
      throw new CLIError(
        `--credentials 中的 ${name} 必须是对象`,
        ErrorCode.ValidationError,
        ExitCode.InvocationError,
      );
    }
    const type = typeof credential.type === 'string' ? credential.type : '';
    if (type.length === 0) {
      throw new CLIError(
        `--credentials 中的 ${name} 缺少 type`,
        ErrorCode.ValidationError,
        ExitCode.InvocationError,
      );
    }
    const fields = isRecord(credential.fields)
      ? (credential.fields as Record<string, string>)
      : {};
    credentials.push({ name, type, fields });
  }
  return credentials;
}

function ensureHttpsForCredentials(baseUrl: string): void {
  try {
    const url = new URL(baseUrl);
    if (url.protocol !== 'https:') {
      throw new CLIError(
        'Dry-run 凭据只能发送至 HTTPS 后端，当前使用 HTTP，已拒绝执行以避免凭据泄露',
        ErrorCode.ValidationError,
        ExitCode.InvocationError,
      );
    }
  } catch (err) {
    if (err instanceof CLIError) {
      throw err;
    }
    throw new CLIError(
      `无效的后端地址：${baseUrl}`,
      ErrorCode.ValidationError,
      ExitCode.InvocationError,
      err,
    );
  }
}

function resolveTimeoutSeconds(value: number | undefined): number {
  if (value === undefined || Number.isNaN(value) || value <= 0) {
    return DEFAULT_TIMEOUT_SECONDS;
  }
  return value;
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

function buildAssertionTarget(result: DryRunResultDto): Record<string, unknown> {
  const nodes: Record<string, unknown> = {};
  for (const [nodeId, node] of Object.entries(result.nodes ?? {})) {
    nodes[nodeId] = {
      status: node.status,
      output: node.output,
    };
  }
  return {
    status: result.status,
    nodes,
  };
}

function buildNodeSummary(result: DryRunResultDto): Record<string, string> {
  if (result.nodeSummary && Object.keys(result.nodeSummary).length > 0) {
    return result.nodeSummary;
  }
  const summary: Record<string, string> = {};
  for (const [nodeId, node] of Object.entries(result.nodes ?? {})) {
    summary[nodeId] = node.status;
  }
  return summary;
}

export async function test(options: TestOptions): Promise<void> {
  const filePath = requireString(options.file, '--file');
  const raw = readJsonFile(filePath);
  if (!isRecord(raw)) {
    throw new CLIError(
      '工作流文件必须是 JSON 对象',
      ErrorCode.InvalidConfig,
      ExitCode.InvocationError,
    );
  }

  const nodes = Array.isArray(raw.nodes) ? raw.nodes : [];
  const connections = Array.isArray(raw.connections) ? raw.connections : [];
  const inputs = isRecord(raw.inputs) ? raw.inputs : undefined;

  const credentials = parseCredentials(options.credentials);

  const config = getConfig(options.profile, options.configOptions);
  if (credentials && credentials.length > 0) {
    ensureHttpsForCredentials(config.baseUrl);
  }

  const body: DryRunWorkflowRequestDto = {
    nodes: nodes as DryRunWorkflowRequestDto['nodes'],
    connections: connections as DryRunWorkflowRequestDto['connections'],
  };
  if (inputs !== undefined) {
    body.inputs = inputs;
  }
  if (credentials !== undefined && credentials.length > 0) {
    body.credentials = credentials;
  }

  const client = createApiClient(options.profile, options.configOptions);
  client.defaults.timeout = resolveTimeoutSeconds(options.timeout) * 1000;

  const response = await client.post<DryRunResultDto>('/workflows/dry-run', body);
  const result = response.data;
  const nodeSummary = buildNodeSummary(result);

  const expect = options.expect ? readJsonFile(options.expect) : undefined;

  if (expect !== undefined) {
    const target = buildAssertionTarget(result);
    const { passed, failures } = runAssertion(target, expect);

    const output = {
      passed,
      executionId: result.executionId ?? '',
      status: result.status ?? '',
      nodeSummary,
      failures,
    };

    if (isJsonMode()) {
      writeJson(output);
    } else {
      log(`测试${passed ? '通过' : '失败'}`);
      log(`Status: ${result.status ?? ''}`);
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
        'Dry-run 结果与期望不符',
        ErrorCode.AssertionFailed,
        ExitCode.BusinessFailure,
        output,
      );
    }
    return;
  }

  if (isJsonMode()) {
    writeJson({
      executionId: result.executionId ?? '',
      status: result.status ?? '',
      nodeSummary,
    });
    return;
  }

  log(`Dry-run 完成：${result.executionId ?? ''}`);
  log(`Status: ${result.status ?? ''}`);
  log('节点摘要：');
  for (const [nodeId, status] of Object.entries(nodeSummary)) {
    log(`  ${nodeId}: ${status}`);
  }
}
