import { describe, expect, it, beforeEach, afterEach } from 'vitest';
import { mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { tmpdir } from 'node:os';
import {
  DEFAULT_BASE_URL,
  getConfig,
  getConfigPath,
  getProfile,
  readConfigFile,
  setDefaultProfile,
  setProfile,
  type ConfigOptions,
} from '../config.ts';
import { CLIError, ErrorCode } from '../errors.ts';

describe('config', () => {
  let tempDir: string;
  let options: ConfigOptions;

  beforeEach(() => {
    tempDir = mkdtempSync(join(tmpdir(), 'flowengine-cli-test-'));
    options = { configDir: tempDir };
  });

  afterEach(() => {
    rmSync(tempDir, { recursive: true, force: true });
  });

  it('readConfigFile - returns default config when file missing', () => {
    const config = readConfigFile(options);
    expect(config.defaultProfile).toBe('default');
    expect(config.profiles.default).toEqual({});
  });

  it('readConfigFile - returns default config when file is empty', () => {
    writeFileSync(getConfigPath(options), '');
    const config = readConfigFile(options);
    expect(config.defaultProfile).toBe('default');
  });

  it('getConfig - returns default base URL and profile when nothing configured', () => {
    const config = getConfig(undefined, options);
    expect(config.baseUrl).toBe(DEFAULT_BASE_URL);
    expect(config.profile).toBe('default');
  });

  it('setProfile and getProfile - persist and read profile values', () => {
    setProfile(
      'prod',
      {
        baseUrl: 'http://prod.example.com',
        token: 'prod-token',
        apiKey: 'prod-key',
        userId: 'user-1',
        email: 'a@example.com',
      },
      options,
    );

    const profile = getProfile('prod', options);
    expect(profile.baseUrl).toBe('http://prod.example.com');
    expect(profile.token).toBe('prod-token');
    expect(profile.apiKey).toBe('prod-key');
    expect(profile.userId).toBe('user-1');
    expect(profile.email).toBe('a@example.com');
  });

  it('getConfig - merges default profile then current profile', () => {
    setProfile('default', { baseUrl: 'http://default.example.com', token: 'default-token' }, options);
    setProfile('prod', { baseUrl: 'http://prod.example.com' }, options);

    const config = getConfig('prod', options);
    expect(config.baseUrl).toBe('http://prod.example.com');
    expect(config.token).toBe('default-token');
  });

  it('getConfig - environment variables override profile values', () => {
    setProfile('default', { baseUrl: 'http://default.example.com', token: 'default-token' }, options);

    process.env.FLOWENGINE_BASE_URL = 'http://env.example.com';
    process.env.FLOWENGINE_TOKEN = 'env-token';

    const config = getConfig(undefined, options);
    expect(config.baseUrl).toBe('http://env.example.com');
    expect(config.token).toBe('env-token');

    delete process.env.FLOWENGINE_BASE_URL;
    delete process.env.FLOWENGINE_TOKEN;
  });

  it('setDefaultProfile - switches the default profile', () => {
    setProfile('prod', { baseUrl: 'http://prod.example.com' }, options);
    setDefaultProfile('prod', options);

    const config = getConfig(undefined, options);
    expect(config.profile).toBe('prod');
    expect(config.baseUrl).toBe('http://prod.example.com');
  });

  it('readConfigFile - throws ConfigReadError for invalid JSON', () => {
    writeFileSync(getConfigPath(options), 'not-json');
    expect(() => readConfigFile(options)).toThrow(CLIError);
    expect(() => readConfigFile(options)).toThrow('读取配置文件失败');
    try {
      readConfigFile(options);
    } catch (err) {
      expect((err as CLIError).code).toBe(ErrorCode.ConfigReadError);
    }
  });

  it('profile does not persist password field', () => {
    const raw = JSON.stringify({
      defaultProfile: 'default',
      profiles: {
        default: {
          baseUrl: 'http://example.com',
          password: 'secret123',
        },
      },
    });
    writeFileSync(getConfigPath(options), raw);

    const profile = getProfile('default', options);
    expect(profile.password).toBeUndefined();
    expect(profile.baseUrl).toBe('http://example.com');
  });
});
