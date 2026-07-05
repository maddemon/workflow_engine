import { createClient, type ApiClientOptions } from '../api/client.js';
import { getConfig, type ConfigOptions } from '../config.js';
import { CLIError, ErrorCode, ExitCode } from '../errors.js';
import { isJsonMode, isVerbose, log, writeJson } from '../output.js';
import type {
  CreateTriggerDto,
  TriggerDto,
  TriggerSettingsDto,
  TriggerType,
  UpdateTriggerDto,
  WorkflowDto,
} from '../types.js';

export interface TriggerListOptions {
  workflow?: string;
  projectId?: string;
  profile?: string;
  configOptions?: ConfigOptions;
}

export interface TriggerGetOptions {
  id: string;
  profile?: string;
  configOptions?: ConfigOptions;
}

export interface TriggerCreateOptions {
  workflow: string;
  type: string;
  name?: string;
  active?: boolean;
  settings?: string;
  profile?: string;
  configOptions?: ConfigOptions;
}

export interface TriggerUpdateOptions {
  id: string;
  name?: string;
  active?: string;
  settings?: string;
  profile?: string;
  configOptions?: ConfigOptions;
}

export interface TriggerDeleteOptions {
  id: string;
  confirm?: boolean;
  profile?: string;
  configOptions?: ConfigOptions;
}

const VALID_TRIGGER_TYPES: TriggerType[] = ['Schedule', 'Webhook', 'Poll'];

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

function parseTriggerType(value: string): TriggerType {
  const trimmed = value.trim();
  if ((VALID_TRIGGER_TYPES as string[]).includes(trimmed)) {
    return trimmed as TriggerType;
  }
  throw new CLIError(
    `--type 必须是 Schedule、Webhook 或 Poll 之一，收到：${value}`,
    ErrorCode.ValidationError,
    ExitCode.InvocationError,
  );
}

function parseSettings(settingsJson: string | undefined): TriggerSettingsDto | undefined {
  if (settingsJson === undefined) {
    return undefined;
  }
  const trimmed = settingsJson.trim();
  if (trimmed.length === 0) {
    return undefined;
  }
  try {
    const parsed: unknown = JSON.parse(trimmed);
    if (!isRecord(parsed)) {
      throw new CLIError(
        '--settings 必须是 JSON 对象',
        ErrorCode.ValidationError,
        ExitCode.InvocationError,
      );
    }
    return parsed as TriggerSettingsDto;
  } catch (err) {
    if (err instanceof CLIError) {
      throw err;
    }
    const message = err instanceof Error ? err.message : String(err);
    throw new CLIError(
      `解析 --settings 失败：${message}`,
      ErrorCode.ValidationError,
      ExitCode.InvocationError,
    );
  }
}

function parseActive(value: string | undefined): boolean | undefined {
  if (value === undefined) {
    return undefined;
  }
  const normalized = value.trim().toLowerCase();
  if (normalized === 'true') return true;
  if (normalized === 'false') return false;
  throw new CLIError(
    `--active 必须是 true 或 false，收到：${value}`,
    ErrorCode.ValidationError,
    ExitCode.InvocationError,
  );
}

async function confirmDelete(resource: string, id: string): Promise<void> {
  if (isJsonMode()) {
    throw new CLIError(
      `JSON 模式下必须使用 --confirm 确认删除 ${resource}`,
      ErrorCode.ValidationError,
      ExitCode.InvocationError,
    );
  }
  const { createInterface } = await import('node:readline/promises');
  const rl = createInterface({
    input: process.stdin,
    output: process.stderr,
  });
  try {
    const answer = await rl.question(`确认删除 ${resource} ${id} 吗？(yes/no): `);
    if (answer.trim().toLowerCase() !== 'yes') {
      throw new CLIError(
        '已取消删除',
        ErrorCode.UserInterrupted,
        ExitCode.UserInterrupted,
      );
    }
  } finally {
    rl.close();
  }
}

async function fetchWorkflowVersion(
  client: ReturnType<typeof createClient>,
  workflowId: string,
): Promise<number> {
  const response = await client.get(`/workflows/${encodeURIComponent(workflowId)}`);
  const workflow = response.data as WorkflowDto;
  return workflow.version;
}

export async function triggerList(options: TriggerListOptions): Promise<void> {
  const client = createApiClient(options.profile, options.configOptions);
  const params: Record<string, unknown> = {};
  if (options.workflow !== undefined && options.workflow.length > 0) {
    params.workflowDefinitionId = options.workflow;
  }
  if (options.projectId !== undefined && options.projectId.length > 0) {
    params.projectId = options.projectId;
  }

  const response = await client.get('/triggers', { params });
  const data: unknown = response.data;
  let triggers: TriggerDto[];
  if (Array.isArray(data)) {
    triggers = data as TriggerDto[];
  } else if (isRecord(data) && Array.isArray(data.items)) {
    triggers = data.items as TriggerDto[];
  } else {
    triggers = [];
  }

  if (isJsonMode()) {
    writeJson(triggers);
    return;
  }

  if (triggers.length === 0) {
    log('暂无触发器。');
    return;
  }

  for (const trigger of triggers) {
    log(
      `${trigger.id}: ${trigger.name} [${trigger.type}, workflow=${trigger.workflowDefinitionId}, v${trigger.workflowVersion}, active=${trigger.isActive}]`,
    );
  }
}

export async function triggerGet(options: TriggerGetOptions): Promise<void> {
  const id = requireString(options.id, '触发器 ID');
  const client = createApiClient(options.profile, options.configOptions);
  const response = await client.get(`/triggers/${encodeURIComponent(id)}`);
  const trigger = response.data as TriggerDto;

  if (isJsonMode()) {
    writeJson(trigger);
    return;
  }

  log(`ID: ${trigger.id}`);
  log(`Name: ${trigger.name}`);
  log(`Type: ${trigger.type}`);
  log(`WorkflowId: ${trigger.workflowDefinitionId}`);
  log(`WorkflowVersion: ${trigger.workflowVersion}`);
  log(`IsActive: ${trigger.isActive}`);
  if (trigger.settings && Object.keys(trigger.settings).length > 0) {
    log(`Settings: ${JSON.stringify(trigger.settings)}`);
  }
  if (trigger.lastTriggeredAt) {
    log(`LastTriggeredAt: ${trigger.lastTriggeredAt}`);
  }
  if (trigger.nextTriggerAt) {
    log(`NextTriggerAt: ${trigger.nextTriggerAt}`);
  }
}

export async function triggerCreate(options: TriggerCreateOptions): Promise<void> {
  const workflowId = requireString(options.workflow, '--workflow');
  const type = parseTriggerType(options.type);
  const name = options.name?.trim() || `${type} Trigger`;
  const settings = parseSettings(options.settings);
  const isActive = options.active ?? true;

  const client = createApiClient(options.profile, options.configOptions);
  const workflowVersion = await fetchWorkflowVersion(client, workflowId);

  const body: CreateTriggerDto = {
    workflowDefinitionId: workflowId,
    workflowVersion,
    type,
    name,
    isActive,
  };
  if (settings !== undefined) {
    body.settings = settings;
  }

  const response = await client.post('/triggers', body);
  const trigger = response.data as TriggerDto;

  if (isJsonMode()) {
    writeJson(trigger);
    return;
  }

  log(`已创建触发器：${trigger.id}`);
  log(`Name: ${trigger.name}`);
  log(`Type: ${trigger.type}`);
  log(`WorkflowVersion: ${trigger.workflowVersion}`);
}

export async function triggerUpdate(options: TriggerUpdateOptions): Promise<void> {
  const id = requireString(options.id, '触发器 ID');
  const client = createApiClient(options.profile, options.configOptions);

  const response = await client.get(`/triggers/${encodeURIComponent(id)}`);
  const existing = response.data as TriggerDto;

  const name = options.name?.trim() || existing.name;
  const isActive = parseActive(options.active) ?? existing.isActive;
  const settings = parseSettings(options.settings) ?? existing.settings;

  const body: UpdateTriggerDto = {
    name,
    isActive,
  };
  if (settings !== undefined) {
    body.settings = settings;
  }

  const updateResponse = await client.put(`/triggers/${encodeURIComponent(id)}`, body);
  const trigger = updateResponse.data as TriggerDto;

  if (isJsonMode()) {
    writeJson(trigger);
    return;
  }

  log(`已更新触发器：${trigger.id}`);
  log(`Name: ${trigger.name}`);
  log(`IsActive: ${trigger.isActive}`);
}

export async function triggerDelete(options: TriggerDeleteOptions): Promise<void> {
  const id = requireString(options.id, '触发器 ID');

  if (!options.confirm) {
    await confirmDelete('触发器', id);
  }

  const client = createApiClient(options.profile, options.configOptions);
  await client.delete(`/triggers/${encodeURIComponent(id)}`);

  const output = { success: true, id };

  if (isJsonMode()) {
    writeJson(output);
    return;
  }

  log(`已删除触发器：${id}`);
}
