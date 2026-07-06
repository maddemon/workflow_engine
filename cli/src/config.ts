import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { homedir } from 'node:os';
import { CLIError, ErrorCode, ExitCode } from './errors.js';

export const DEFAULT_BASE_URL = 'http://localhost:8001';
export const DEFAULT_PROFILE_NAME = 'default';
export const CONFIG_DIR_NAME = '.flowengine';
export const CONFIG_FILE_NAME = 'config.json';

export interface Profile {
  baseUrl?: string;
  token?: string;
  apiKey?: string;
  userId?: string;
  email?: string;
}

export interface ConfigFile {
  defaultProfile: string;
  profiles: Record<string, Profile>;
}

export interface ResolvedConfig extends Profile {
  baseUrl: string;
  profile: string;
}

export interface ConfigOptions {
  configDir?: string;
}

function getConfigDir(options?: ConfigOptions): string {
  return options?.configDir ?? join(homedir(), CONFIG_DIR_NAME);
}

export function getConfigPath(options?: ConfigOptions): string {
  return join(getConfigDir(options), CONFIG_FILE_NAME);
}

function ensureConfigDir(options?: ConfigOptions): void {
  const dir = getConfigDir(options);
  if (!existsSync(dir)) {
    mkdirSync(dir, { recursive: true, mode: 0o700 });
  }
}

function createEmptyConfig(): ConfigFile {
  return {
    defaultProfile: DEFAULT_PROFILE_NAME,
    profiles: {
      [DEFAULT_PROFILE_NAME]: {},
    },
  };
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function parseConfigFile(content: string): ConfigFile {
  const parsed: unknown = JSON.parse(content);
  if (!isRecord(parsed)) {
    throw new CLIError(
      '配置文件格式无效：根对象必须是 JSON 对象',
      ErrorCode.InvalidConfig,
      ExitCode.InvocationError,
    );
  }

  const defaultProfile =
    typeof parsed.defaultProfile === 'string' ? parsed.defaultProfile : DEFAULT_PROFILE_NAME;
  const rawProfiles = isRecord(parsed.profiles) ? parsed.profiles : {};

  const profiles: Record<string, Profile> = {};
  for (const [name, value] of Object.entries(rawProfiles)) {
    if (isRecord(value)) {
      profiles[name] = parseProfile(value);
    }
  }

  if (!profiles[defaultProfile]) {
    profiles[defaultProfile] = {};
  }

  return { defaultProfile, profiles };
}

function parseProfile(value: Record<string, unknown>): Profile {
  const profile: Profile = {};
  if (typeof value.baseUrl === 'string') profile.baseUrl = value.baseUrl;
  if (typeof value.token === 'string') profile.token = value.token;
  if (typeof value.apiKey === 'string') profile.apiKey = value.apiKey;
  if (typeof value.userId === 'string') profile.userId = value.userId;
  if (typeof value.email === 'string') profile.email = value.email;
  return profile;
}

export function readConfigFile(options?: ConfigOptions): ConfigFile {
  const path = getConfigPath(options);
  if (!existsSync(path)) {
    return createEmptyConfig();
  }

  try {
    const content = readFileSync(path, 'utf-8');
    if (content.trim().length === 0) {
      return createEmptyConfig();
    }
    return parseConfigFile(content);
  } catch (err) {
    const message = err instanceof Error ? err.message : String(err);
    throw new CLIError(
      `读取配置文件失败：${message}`,
      ErrorCode.ConfigReadError,
      ExitCode.InvocationError,
      err,
    );
  }
}

export function writeConfigFile(config: ConfigFile, options?: ConfigOptions): void {
  ensureConfigDir(options);
  try {
    writeFileSync(getConfigPath(options), JSON.stringify(config, undefined, 2), { mode: 0o600 });
  } catch (err) {
    const message = err instanceof Error ? err.message : String(err);
    throw new CLIError(
      `写入配置文件失败：${message}`,
      ErrorCode.ConfigWriteError,
      ExitCode.InvocationError,
      err,
    );
  }
}

export function getProfile(name: string, options?: ConfigOptions): Profile {
  const config = readConfigFile(options);
  return config.profiles[name] ?? {};
}

export function setProfile(name: string, profile: Profile, options?: ConfigOptions): void {
  const config = readConfigFile(options);
  config.profiles[name] = { ...profile };
  writeConfigFile(config, options);
}

export function setDefaultProfile(name: string, options?: ConfigOptions): void {
  const config = readConfigFile(options);
  config.defaultProfile = name;
  if (!config.profiles[name]) {
    config.profiles[name] = {};
  }
  writeConfigFile(config, options);
}

function applyEnvOverrides(resolved: ResolvedConfig): ResolvedConfig {
  const baseUrl = process.env.FLOWENGINE_BASE_URL;
  const token = process.env.FLOWENGINE_TOKEN;

  return {
    ...resolved,
    ...(baseUrl !== undefined && baseUrl.length > 0 ? { baseUrl } : {}),
    ...(token !== undefined && token.length > 0 ? { token } : {}),
  };
}

export function getConfig(
  profileName?: string,
  options?: ConfigOptions,
): ResolvedConfig {
  const config = readConfigFile(options);
  const targetProfile = profileName && profileName.length > 0 ? profileName : config.defaultProfile;
  const defaultProfile = config.profiles[config.defaultProfile] ?? {};
  const currentProfile = config.profiles[targetProfile] ?? {};

  const merged: ResolvedConfig = {
    baseUrl: DEFAULT_BASE_URL,
    profile: targetProfile,
    ...defaultProfile,
    ...currentProfile,
  };

  if (merged.baseUrl === undefined || merged.baseUrl.length === 0) {
    merged.baseUrl = DEFAULT_BASE_URL;
  }

  return applyEnvOverrides(merged);
}
