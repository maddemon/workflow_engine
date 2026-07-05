import { createClient, type ApiClientOptions } from '../api/client.js';
import { getConfig, type ConfigOptions } from '../config.js';
import { CLIError, ErrorCode, ExitCode } from '../errors.js';
import { isJsonMode, log, writeJson } from '../output.js';
import type { ApiKeyDto, CreateApiKeyRequest, CreateApiKeyResult } from '../types.js';

export interface ApiKeyCreateOptions {
  name: string;
  expiresAt?: string;
  profile?: string;
  configOptions?: ConfigOptions;
}

export interface ApiKeyListOptions {
  profile?: string;
  configOptions?: ConfigOptions;
}

export interface ApiKeyRevokeOptions {
  id: string;
  confirm?: boolean;
  profile?: string;
  configOptions?: ConfigOptions;
}

function createApiClient(profile?: string, configOptions?: ConfigOptions) {
  const config = getConfig(profile, configOptions);
  const options: ApiClientOptions = {
    baseURL: `${config.baseUrl}/api/v1`,
    token: config.token,
    apiKey: config.apiKey,
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

export async function apiKeyCreate(options: ApiKeyCreateOptions): Promise<void> {
  const name = requireString(options.name, '--name');
  const body: CreateApiKeyRequest = { name };
  if (options.expiresAt !== undefined && options.expiresAt.length > 0) {
    body.expiresAt = options.expiresAt;
  }

  const client = createApiClient(options.profile, options.configOptions);
  const response = await client.post<CreateApiKeyResult>('/auth/api-keys', body);
  const result = response.data;

  if (isJsonMode()) {
    writeJson(result);
    return;
  }

  log('API Key 已创建，请立即保存，之后无法再次查看完整 Key。');
  log(`ID: ${result.id}`);
  log(`Name: ${result.name}`);
  log(`Prefix: ${result.prefix}`);
  if (result.expiresAt) {
    log(`Expires At: ${result.expiresAt}`);
  }
  log(`Key: ${result.key}`);
}

export async function apiKeyList(options: ApiKeyListOptions): Promise<void> {
  const client = createApiClient(options.profile, options.configOptions);
  const response = await client.get<ApiKeyDto[]>('/auth/api-keys');
  const items = response.data;

  if (isJsonMode()) {
    writeJson(items);
    return;
  }

  if (items.length === 0) {
    log('暂无 API Key。');
    return;
  }

  for (const item of items) {
    const status = item.revokedAt ? `已吊销（${item.revokedAt}）` : '有效';
    log(`ID: ${item.id}`);
    log(`  Name: ${item.name}`);
    log(`  Prefix: ${item.prefix}`);
    log(`  Created At: ${item.createdAt}`);
    if (item.expiresAt) {
      log(`  Expires At: ${item.expiresAt}`);
    }
    log(`  Status: ${status}`);
  }
}

export async function apiKeyRevoke(options: ApiKeyRevokeOptions): Promise<void> {
  const id = requireString(options.id, 'API Key ID');

  if (!options.confirm && !isJsonMode()) {
    throw new CLIError(
      '吊销 API Key 不可恢复，请在交互模式下使用 --confirm 确认，或改用 JSON 模式。',
      ErrorCode.ValidationError,
      ExitCode.InvocationError,
    );
  }

  const client = createApiClient(options.profile, options.configOptions);
  await client.delete(`/auth/api-keys/${id}`);

  if (isJsonMode()) {
    writeJson({ success: true, id });
    return;
  }

  log(`API Key 已吊销：${id}`);
}
