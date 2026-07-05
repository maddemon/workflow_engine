import { createClient, type ApiClientOptions } from '../api/client.js';
import { getConfig, type ConfigOptions } from '../config.js';
import { CLIError, ErrorCode, ExitCode } from '../errors.js';
import { isJsonMode, isVerbose, log, writeJson } from '../output.js';
import type {
  CredentialDto,
  CreateCredentialDto,
  UpdateCredentialDto,
} from '../types.js';

export interface CredentialListOptions {
  projectId?: string;
  profile?: string;
  configOptions?: ConfigOptions;
}

export interface CredentialGetOptions {
  id: string;
  profile?: string;
  configOptions?: ConfigOptions;
}

export interface CredentialCreateOptions {
  name: string;
  type: string;
  fields: string;
  projectId?: string;
  profile?: string;
  configOptions?: ConfigOptions;
}

export interface CredentialEnsureOptions {
  name: string;
  type: string;
  fields: string;
  projectId?: string;
  profile?: string;
  configOptions?: ConfigOptions;
}

export interface CredentialUpdateOptions {
  id: string;
  name: string;
  fields: string;
  profile?: string;
  configOptions?: ConfigOptions;
}

export interface CredentialDeleteOptions {
  id: string;
  confirm?: boolean;
  profile?: string;
  configOptions?: ConfigOptions;
}

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

function parseFields(fieldsJson: string): Record<string, string> {
  const trimmed = fieldsJson.trim();
  if (trimmed.length === 0) {
    throw new CLIError(
      '--fields 不能为空',
      ErrorCode.ValidationError,
      ExitCode.InvocationError,
    );
  }
  try {
    const parsed: unknown = JSON.parse(trimmed);
    if (!isRecord(parsed)) {
      throw new CLIError(
        '--fields 必须是 JSON 对象',
        ErrorCode.ValidationError,
        ExitCode.InvocationError,
      );
    }
    const result: Record<string, string> = {};
    for (const [key, value] of Object.entries(parsed)) {
      result[key] = typeof value === 'string' ? value : JSON.stringify(value);
    }
    return result;
  } catch (err) {
    if (err instanceof CLIError) {
      throw err;
    }
    const message = err instanceof Error ? err.message : String(err);
    throw new CLIError(
      `解析 --fields 失败：${message}`,
      ErrorCode.ValidationError,
      ExitCode.InvocationError,
    );
  }
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

export async function credentialList(options: CredentialListOptions): Promise<void> {
  const client = createApiClient(options.profile, options.configOptions);
  const response = await client.get('/credentials', {
    params: options.projectId ? { projectId: options.projectId } : undefined,
  });

  const data: unknown = response.data;
  let credentials: CredentialDto[];
  if (Array.isArray(data)) {
    credentials = data as CredentialDto[];
  } else if (isRecord(data) && Array.isArray(data.items)) {
    credentials = data.items as CredentialDto[];
  } else {
    credentials = [];
  }

  if (isJsonMode()) {
    writeJson(credentials);
    return;
  }

  if (credentials.length === 0) {
    log('暂无凭据。');
    return;
  }

  for (const credential of credentials) {
    const projectId = credential.projectId ? `, Project: ${credential.projectId}` : '';
    log(`${credential.id}: ${credential.name} [${credential.type}]${projectId}`);
  }
}

export async function credentialGet(options: CredentialGetOptions): Promise<void> {
  const id = requireString(options.id, '凭据 ID');
  const client = createApiClient(options.profile, options.configOptions);
  const response = await client.get(`/credentials/${encodeURIComponent(id)}`);
  const credential = response.data as CredentialDto;

  if (isJsonMode()) {
    writeJson(credential);
    return;
  }

  log(`ID: ${credential.id}`);
  log(`Name: ${credential.name}`);
  log(`Type: ${credential.type}`);
  if (credential.projectId) {
    log(`ProjectId: ${credential.projectId}`);
  }
  if (credential.fields && Object.keys(credential.fields).length > 0) {
    log('Fields (masked by server):');
    for (const [key, value] of Object.entries(credential.fields)) {
      log(`  ${key}: ${value}`);
    }
  }
  log(`CreatedAt: ${credential.createdAt}`);
  if (credential.updatedAt) {
    log(`UpdatedAt: ${credential.updatedAt}`);
  }
}

export async function credentialCreate(options: CredentialCreateOptions): Promise<void> {
  const name = requireString(options.name, '--name');
  const type = requireString(options.type, '--type');
  const fields = parseFields(options.fields);

  const body: CreateCredentialDto = {
    name,
    type,
    fields,
  };
  if (options.projectId !== undefined && options.projectId.length > 0) {
    body.projectId = options.projectId;
  }

  const client = createApiClient(options.profile, options.configOptions);
  const response = await client.post('/credentials', body);
  const credential = response.data as CredentialDto;

  if (isJsonMode()) {
    writeJson(credential);
    return;
  }

  log(`已创建凭据：${credential.id}`);
  log(`Name: ${credential.name}`);
  log(`Type: ${credential.type}`);
}

export async function credentialEnsure(options: CredentialEnsureOptions): Promise<void> {
  const name = requireString(options.name, '--name');
  const type = requireString(options.type, '--type');
  const fields = parseFields(options.fields);

  const body: CreateCredentialDto = {
    name,
    type,
    fields,
  };
  if (options.projectId !== undefined && options.projectId.length > 0) {
    body.projectId = options.projectId;
  }

  const client = createApiClient(options.profile, options.configOptions);
  const response = await client.post('/credentials/ensure', body);
  const result = response.data as CredentialDto & { created?: boolean };

  const output = {
    ...result,
    created: result.created ?? false,
  };

  if (isJsonMode()) {
    writeJson(output);
    return;
  }

  log(`${output.created ? '已新建' : '已更新'}凭据：${output.id}`);
  log(`Name: ${output.name}`);
  log(`Type: ${output.type}`);
}

export async function credentialUpdate(options: CredentialUpdateOptions): Promise<void> {
  const id = requireString(options.id, '凭据 ID');
  const name = requireString(options.name, '--name');
  const fields = parseFields(options.fields);

  const body: UpdateCredentialDto = { name, fields };

  const client = createApiClient(options.profile, options.configOptions);
  const response = await client.put(`/credentials/${encodeURIComponent(id)}`, body);
  const credential = response.data as CredentialDto;

  if (isJsonMode()) {
    writeJson(credential);
    return;
  }

  log(`已更新凭据：${credential.id}`);
  log(`Name: ${credential.name}`);
  log(`Type: ${credential.type}`);
}

export async function credentialDelete(options: CredentialDeleteOptions): Promise<void> {
  const id = requireString(options.id, '凭据 ID');

  if (!options.confirm) {
    await confirmDelete('凭据', id);
  }

  const client = createApiClient(options.profile, options.configOptions);
  await client.delete(`/credentials/${encodeURIComponent(id)}`);

  const output = { success: true, id };

  if (isJsonMode()) {
    writeJson(output);
    return;
  }

  log(`已删除凭据：${id}`);
}
