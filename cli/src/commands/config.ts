import {
  getConfig,
  getProfile,
  readConfigFile,
  setDefaultProfile,
  setProfile,
  type ConfigOptions,
} from '../config.js';
import { CLIError, ErrorCode, ExitCode } from '../errors.js';
import { isJsonMode, log, writeJson } from '../output.js';
import { decodeJwt } from 'jose';

export interface ConfigGetOptions {
  profile?: string;
  configOptions?: ConfigOptions;
}

export interface ConfigSetOptions {
  profile?: string;
  key: string;
  value: string;
  configOptions?: ConfigOptions;
}

export interface ConfigUseProfileOptions {
  name: string;
  configOptions?: ConfigOptions;
}

export interface ConfigListProfilesOptions {
  configOptions?: ConfigOptions;
}

const FORBIDDEN_KEYS = new Set([
  'token',
  'apikey',
  'api-key',
  'api_key',
  'userid',
  'user-id',
  'user_id',
]);

function normalizeKey(key: string): string {
  return key.trim().toLowerCase();
}

function extractTokenExpiresAt(token: string): string | undefined {
  try {
    const decoded = decodeJwt(token);
    if (typeof decoded.exp === 'number') {
      return new Date(decoded.exp * 1000).toISOString();
    }
  } catch {
    // 忽略无法解码的 token
  }
  return undefined;
}

export async function configGet(options: ConfigGetOptions): Promise<void> {
  const config = getConfig(options.profile, options.configOptions);
  const authType = config.apiKey ? 'apiKey' : config.token ? 'jwt' : 'none';
  const tokenExpiresAt = config.token ? extractTokenExpiresAt(config.token) : undefined;

  const output = {
    baseUrl: config.baseUrl,
    userId: config.userId,
    email: config.email,
    authType,
    tokenExpiresAt,
    profile: config.profile,
  };

  if (isJsonMode()) {
    writeJson(output);
    return;
  }

  log(`Profile: ${config.profile}`);
  log(`Base URL: ${config.baseUrl}`);
  log(`Auth Type: ${authType}`);
  if (config.userId) {
    log(`User ID: ${config.userId}`);
  }
  if (config.email) {
    log(`Email: ${config.email}`);
  }
  if (tokenExpiresAt) {
    log(`Token Expires At: ${tokenExpiresAt}`);
  }
}

export async function configSet(options: ConfigSetOptions): Promise<void> {
  const key = normalizeKey(options.key);

  if (FORBIDDEN_KEYS.has(key)) {
    throw new CLIError(
      `禁止通过 config set 设置 ${options.key}，请使用 login 命令`,
      ErrorCode.ValidationError,
      ExitCode.InvocationError,
    );
  }

  if (key !== 'baseurl' && key !== 'email') {
    throw new CLIError(
      `不允许设置 ${options.key}，仅支持 baseUrl 和 email`,
      ErrorCode.ValidationError,
      ExitCode.InvocationError,
    );
  }

  const profileName = options.profile ?? 'default';
  const profile = getProfile(profileName, options.configOptions);

  if (key === 'baseurl') {
    profile.baseUrl = options.value;
  } else if (key === 'email') {
    profile.email = options.value;
  }

  setProfile(profileName, profile, options.configOptions);

  const output = {
    success: true,
    profile: profileName,
    key: options.key,
    value: options.value,
  };

  if (isJsonMode()) {
    writeJson(output);
    return;
  }

  log(`已设置 ${options.key}=${options.value} (${profileName})`);
}

export async function configUseProfile(options: ConfigUseProfileOptions): Promise<void> {
  const name = options.name.trim();
  if (name.length === 0) {
    throw new CLIError(
      '请提供 profile 名称',
      ErrorCode.ValidationError,
      ExitCode.InvocationError,
    );
  }

  setDefaultProfile(name, options.configOptions);

  const output = {
    success: true,
    defaultProfile: name,
  };

  if (isJsonMode()) {
    writeJson(output);
    return;
  }

  log(`默认 profile 已切换为：${name}`);
}

export async function configListProfiles(options: ConfigListProfilesOptions): Promise<void> {
  const config = readConfigFile(options.configOptions);
  const profiles = Object.entries(config.profiles).map(([name, profile]) => ({
    name,
    baseUrl: profile.baseUrl,
    isDefault: name === config.defaultProfile,
  }));

  if (isJsonMode()) {
    writeJson({ profiles });
    return;
  }

  if (profiles.length === 0) {
    log('没有已保存的 profile。');
    return;
  }

  for (const p of profiles) {
    const marker = p.isDefault ? '*' : ' ';
    log(`${marker} ${p.name}: ${p.baseUrl || '(未设置)'}`);
  }
}
