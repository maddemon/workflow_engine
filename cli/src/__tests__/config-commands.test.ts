import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { mkdtempSync, rmSync } from 'node:fs';
import { join } from 'node:path';
import { tmpdir } from 'node:os';
import {
  configGet,
  configSet,
  configUseProfile,
  configListProfiles,
} from '../commands/config.js';
import { getConfig, getProfile, setProfile, type ConfigOptions } from '../config.js';
import { CLIError, ErrorCode, ExitCode } from '../errors.js';
import { setOutputOptions } from '../output.js';

function captureStdout(callback: () => Promise<void>): Promise<string> {
  return new Promise((resolve, reject) => {
    const originalLog = console.log;
    const outputs: string[] = [];
    console.log = (message: string) => {
      outputs.push(message);
    };
    callback()
      .then(() => {
        console.log = originalLog;
        resolve(outputs.join('\n'));
      })
      .catch((err) => {
        console.log = originalLog;
        reject(err);
      });
  });
}

function makeJwt(payload: object): string {
  const header = Buffer.from(JSON.stringify({ alg: 'none', typ: 'JWT' })).toString('base64url');
  const body = Buffer.from(JSON.stringify(payload)).toString('base64url');
  return `${header}.${body}.signature`;
}

describe('commands/config', () => {
  let tempDir: string;
  let options: ConfigOptions;

  beforeEach(() => {
    tempDir = mkdtempSync(join(tmpdir(), 'flowengine-cli-config-test-'));
    options = { configDir: tempDir };
    setOutputOptions({ json: false, verbose: false });
  });

  afterEach(() => {
    rmSync(tempDir, { recursive: true, force: true });
    vi.restoreAllMocks();
  });

  it('configGet - returns baseUrl and auth info without exposing token', async () => {
    const token = makeJwt({ exp: 1893456000 });
    setProfile(
      'dev',
      {
        baseUrl: 'http://dev.example.com',
        token,
        userId: 'user-1',
        email: 'a@example.com',
      },
      options,
    );

    const output = await captureStdout(() => configGet({ profile: 'dev', configOptions: options }));

    expect(output).toContain('Profile: dev');
    expect(output).toContain('Base URL: http://dev.example.com');
    expect(output).toContain('Auth Type: jwt');
    expect(output).toContain('User ID: user-1');
    expect(output).toContain('Email: a@example.com');
    expect(output).toContain('Token Expires At:');
    expect(output).not.toContain(token);
  });

  it('configGet - JSON mode outputs only parseable JSON', async () => {
    setProfile(
      'prod',
      {
        baseUrl: 'http://prod.example.com',
        apiKey: 'secret-key',
        userId: 'user-2',
        email: 'b@example.com',
      },
      options,
    );

    setOutputOptions({ json: true, verbose: false });
    const spy = vi.spyOn(console, 'log').mockImplementation(() => {});

    await configGet({ profile: 'prod', configOptions: options });

    expect(spy).toHaveBeenCalledTimes(1);
    const parsed = JSON.parse(spy.mock.calls[0][0] as string);
    expect(parsed.baseUrl).toBe('http://prod.example.com');
    expect(parsed.authType).toBe('apiKey');
    expect(parsed.userId).toBe('user-2');
    expect(parsed.email).toBe('b@example.com');
    expect(parsed.token).toBeUndefined();
    expect(parsed.apiKey).toBeUndefined();
    spy.mockRestore();
  });

  it('configSet - allows setting baseUrl', async () => {
    await configSet({
      profile: 'dev',
      key: 'baseUrl',
      value: 'http://new.example.com',
      configOptions: options,
    });

    const saved = getProfile('dev', options);
    expect(saved.baseUrl).toBe('http://new.example.com');
  });

  it('configSet - allows setting email', async () => {
    await configSet({
      profile: 'dev',
      key: 'email',
      value: 'new@example.com',
      configOptions: options,
    });

    const saved = getProfile('dev', options);
    expect(saved.email).toBe('new@example.com');
  });

  it('configSet - rejects token', async () => {
    await expect(
      configSet({
        profile: 'dev',
        key: 'token',
        value: 'secret-token',
        configOptions: options,
      }),
    ).rejects.toThrow(CLIError);

    try {
      await configSet({
        profile: 'dev',
        key: 'token',
        value: 'secret-token',
        configOptions: options,
      });
    } catch (err) {
      const cliErr = err as CLIError;
      expect(cliErr.code).toBe(ErrorCode.ValidationError);
      expect(cliErr.exitCode).toBe(ExitCode.InvocationError);
    }
  });

  it('configSet - rejects apiKey', async () => {
    await expect(
      configSet({
        profile: 'dev',
        key: 'apiKey',
        value: 'secret-key',
        configOptions: options,
      }),
    ).rejects.toThrow('禁止通过 config set 设置 apiKey');
  });

  it('configSet - rejects userId', async () => {
    await expect(
      configSet({
        profile: 'dev',
        key: 'userId',
        value: 'user-1',
        configOptions: options,
      }),
    ).rejects.toThrow('禁止通过 config set 设置 userId');
  });

  it('configUseProfile - switches default profile', async () => {
    setProfile('prod', { baseUrl: 'http://prod.example.com' }, options);

    await configUseProfile({ name: 'prod', configOptions: options });

    const config = getConfig(undefined, options);
    expect(config.profile).toBe('prod');
    expect(config.baseUrl).toBe('http://prod.example.com');
  });

  it('configUseProfile - rejects empty name', async () => {
    await expect(configUseProfile({ name: '  ', configOptions: options })).rejects.toThrow(CLIError);
  });

  it('configListProfiles - lists profiles with baseUrl and default marker', async () => {
    setProfile('default', { baseUrl: 'http://default.example.com' }, options);
    setProfile('prod', { baseUrl: 'http://prod.example.com' }, options);

    const output = await captureStdout(() => configListProfiles({ configOptions: options }));

    expect(output).toContain('* default: http://default.example.com');
    expect(output).toContain('  prod: http://prod.example.com');
  });

  it('configListProfiles - JSON mode outputs profile list', async () => {
    setProfile('default', { baseUrl: 'http://default.example.com' }, options);
    setProfile('dev', { baseUrl: 'http://dev.example.com' }, options);

    setOutputOptions({ json: true, verbose: false });
    const spy = vi.spyOn(console, 'log').mockImplementation(() => {});

    await configListProfiles({ configOptions: options });

    expect(spy).toHaveBeenCalledTimes(1);
    const parsed = JSON.parse(spy.mock.calls[0][0] as string);
    expect(parsed.profiles).toHaveLength(2);
    expect(parsed.profiles.find((p: { name: string }) => p.name === 'default').isDefault).toBe(true);
    expect(parsed.profiles.find((p: { name: string }) => p.name === 'dev').isDefault).toBe(false);
    spy.mockRestore();
  });
});
