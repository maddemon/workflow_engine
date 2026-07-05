import { createClient } from '../api/client.js';
import {
  getConfig,
  getProfile,
  setProfile,
  type ConfigOptions,
} from '../config.js';
import { CLIError, ErrorCode, ExitCode } from '../errors.js';
import { isJsonMode, log, writeJson } from '../output.js';
import type { LoginResult, UserDto } from '../types.js';
import { decodeJwt } from 'jose';
import { createInterface } from 'node:readline/promises';

export interface LoginOptions {
  url?: string;
  email?: string;
  password?: string;
  apiKey?: string;
  passwordStdin?: boolean;
  profile?: string;
  configOptions?: ConfigOptions;
}

export interface LogoutOptions {
  profile?: string;
  configOptions?: ConfigOptions;
}

export interface ProfileOptions {
  profile?: string;
  configOptions?: ConfigOptions;
}

function resolveBaseUrl(url: string | undefined): string {
  const value = url?.trim() ?? '';
  if (value.length === 0) {
    throw new CLIError(
      '缺少 --url 参数',
      ErrorCode.ValidationError,
      ExitCode.InvocationError,
    );
  }
  return value;
}

function resolveEmail(email: string | undefined): string {
  const value = email?.trim() ?? '';
  if (value.length === 0) {
    throw new CLIError(
      '缺少 --email 参数',
      ErrorCode.ValidationError,
      ExitCode.InvocationError,
    );
  }
  return value;
}

function resolveApiKey(apiKey: string | undefined): string {
  const value = apiKey?.trim() ?? '';
  if (value.length === 0) {
    throw new CLIError(
      '缺少 --api-key 参数',
      ErrorCode.ValidationError,
      ExitCode.InvocationError,
    );
  }
  return value;
}

async function readPasswordFromStdin(): Promise<string> {
  const chunks: Buffer[] = [];
  return new Promise((resolve, reject) => {
    process.stdin.on('data', (chunk) => {
      chunks.push(Buffer.from(chunk));
    });
    process.stdin.on('end', () => {
      const input = Buffer.concat(chunks).toString('utf-8').replace(/\r?\n$/, '');
      resolve(input);
    });
    process.stdin.on('error', (err) => {
      reject(err);
    });
  });
}

async function promptPassword(): Promise<string> {
  const rl = createInterface({
    input: process.stdin,
    output: process.stderr,
  });
  try {
    const answer = await rl.question('请输入密码: ');
    return answer;
  } finally {
    rl.close();
  }
}

async function resolvePassword(options: LoginOptions): Promise<string> {
  if (options.passwordStdin) {
    return readPasswordFromStdin();
  }
  if (options.password && options.password.length > 0) {
    return options.password;
  }
  if (isJsonMode()) {
    throw new CLIError(
      'JSON 模式下必须使用 --password 或 --password-stdin 提供密码',
      ErrorCode.ValidationError,
      ExitCode.InvocationError,
    );
  }
  return promptPassword();
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

async function jwtLogin(options: LoginOptions): Promise<void> {
  const baseUrl = resolveBaseUrl(options.url);
  const email = resolveEmail(options.email);
  const password = await resolvePassword(options);
  const profileName = options.profile ?? 'default';

  const client = createClient({ baseURL: baseUrl });
  const response = await client.post<LoginResult>('/api/v1/auth/login', {
    email,
    password,
  });
  const result = response.data;

  if (!result.success || !result.token || !result.userId) {
    throw new CLIError(
      result.errorMessage || '登录失败',
      ErrorCode.AuthRequired,
      ExitCode.BusinessFailure,
    );
  }

  const userEmail = result.user?.email ?? email;
  setProfile(
    profileName,
    {
      baseUrl,
      token: result.token,
      userId: result.userId,
      email: userEmail,
    },
    options.configOptions,
  );

  const tokenExpiresAt = extractTokenExpiresAt(result.token);
  const output = {
    success: true,
    userId: result.userId,
    email: userEmail,
    tokenExpiresAt,
    profile: profileName,
    authType: 'jwt' as const,
  };

  if (isJsonMode()) {
    writeJson(output);
    return;
  }

  log(`登录成功：${userEmail} (${profileName})`);
  if (tokenExpiresAt) {
    log(`Token 过期时间：${tokenExpiresAt}`);
  }
}

async function apiKeyLogin(options: LoginOptions): Promise<void> {
  const baseUrl = resolveBaseUrl(options.url);
  const apiKey = resolveApiKey(options.apiKey);
  const profileName = options.profile ?? 'default';

  const client = createClient({ baseURL: baseUrl, apiKey });
  const response = await client.get<UserDto>('/api/v1/auth/me');
  const user = response.data;

  setProfile(
    profileName,
    {
      baseUrl,
      apiKey,
      userId: user.id,
      email: user.email,
    },
    options.configOptions,
  );

  const output = {
    success: true,
    userId: user.id,
    email: user.email,
    authType: 'apiKey' as const,
    profile: profileName,
  };

  if (isJsonMode()) {
    writeJson(output);
    return;
  }

  log(`登录成功：${user.email} (${profileName})，认证方式：API Key`);
}

export async function login(options: LoginOptions): Promise<void> {
  if (options.apiKey) {
    await apiKeyLogin(options);
  } else {
    await jwtLogin(options);
  }
}

export async function logout(options: LogoutOptions): Promise<void> {
  const config = getConfig(options.profile, options.configOptions);

  if (config.token) {
    const client = createClient({
      baseURL: config.baseUrl,
      token: config.token,
    });
    try {
      await client.post('/api/v1/auth/logout');
    } catch {
      // 后端 logout 失败不阻塞本地清除
    }
  }

  const profileName = options.profile ?? config.profile;
  const existing = getProfile(profileName, options.configOptions);

  setProfile(
    profileName,
    {
      baseUrl: existing.baseUrl,
    },
    options.configOptions,
  );

  const output = {
    success: true,
    profile: profileName,
  };

  if (isJsonMode()) {
    writeJson(output);
    return;
  }

  log(`已登出：${profileName}`);
}

function maskCredential(value: string | undefined): string | undefined {
  if (!value || value.length === 0) {
    return undefined;
  }
  const visibleLength = Math.min(4, value.length);
  return `${value.slice(0, visibleLength)}****`;
}

export async function me(options: ProfileOptions): Promise<void> {
  const config = getConfig(options.profile, options.configOptions);
  const client = createClient({
    baseURL: `${config.baseUrl}/api/v1`,
    token: config.token,
    apiKey: config.apiKey,
  });

  const response = await client.get<UserDto>('/auth/me');
  const user = response.data;

  const output = {
    id: user.id,
    email: user.email,
    userName: user.userName,
    displayName: user.displayName,
    isActive: user.isActive,
    createdAt: user.createdAt,
    updatedAt: user.updatedAt,
  };

  if (isJsonMode()) {
    writeJson(output);
    return;
  }

  log(`User ID: ${user.id}`);
  log(`Email: ${user.email}`);
  log(`User Name: ${user.userName}`);
  if (user.displayName) {
    log(`Display Name: ${user.displayName}`);
  }
  log(`Active: ${user.isActive}`);
  log(`Created At: ${user.createdAt}`);
  if (user.updatedAt) {
    log(`Updated At: ${user.updatedAt}`);
  }
}

export async function profile(options: ProfileOptions): Promise<void> {
  const config = getConfig(options.profile, options.configOptions);
  const authType = config.apiKey ? 'apiKey' : config.token ? 'jwt' : 'none';
  const tokenExpiresAt = config.token ? extractTokenExpiresAt(config.token) : undefined;

  const output = {
    profile: config.profile,
    baseUrl: config.baseUrl,
    authType,
    email: config.email,
    userId: config.userId,
    tokenExpiresAt,
    tokenPrefix: authType === 'jwt' ? maskCredential(config.token) : undefined,
    apiKeyPrefix: authType === 'apiKey' ? maskCredential(config.apiKey) : undefined,
  };

  if (isJsonMode()) {
    writeJson(output);
    return;
  }

  log(`Profile: ${config.profile}`);
  log(`Base URL: ${config.baseUrl}`);
  log(`Auth Type: ${authType}`);
  if (config.email) {
    log(`Email: ${config.email}`);
  }
  if (config.userId) {
    log(`User ID: ${config.userId}`);
  }
  if (tokenExpiresAt) {
    log(`Token Expires At: ${tokenExpiresAt}`);
  }
  if (output.tokenPrefix) {
    log(`Token Prefix: ${output.tokenPrefix}`);
  }
  if (output.apiKeyPrefix) {
    log(`API Key Prefix: ${output.apiKeyPrefix}`);
  }
}
