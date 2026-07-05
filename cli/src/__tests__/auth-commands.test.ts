import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import axios, { type AxiosInstance } from 'axios';
import { mkdtempSync, rmSync } from 'node:fs';
import { join } from 'node:path';
import { tmpdir } from 'node:os';
import { login, logout, me, profile } from '../commands/auth.js';
import { getConfig, getProfile, setProfile, type ConfigOptions } from '../config.js';
import { CLIError, ErrorCode, ExitCode } from '../errors.js';
import { setOutputOptions } from '../output.js';

vi.mock('axios', async (importOriginal) => {
  const actual = await importOriginal<typeof import('axios')>();
  return {
    ...actual,
    default: {
      ...actual.default,
      create: vi.fn(),
    },
  };
});

function encodeBase64Url(value: string): string {
  return Buffer.from(value).toString('base64url');
}

function makeJwt(payload: object): string {
  const header = encodeBase64Url(JSON.stringify({ alg: 'none', typ: 'JWT' }));
  const body = encodeBase64Url(JSON.stringify(payload));
  return `${header}.${body}.signature`;
}

describe('commands/auth', () => {
  let tempDir: string;
  let options: ConfigOptions;
  let mockInstance: {
    interceptors: {
      request: { use: ReturnType<typeof vi.fn> };
      response: { use: ReturnType<typeof vi.fn> };
    };
    get: ReturnType<typeof vi.fn>;
    post: ReturnType<typeof vi.fn>;
  };

  beforeEach(() => {
    tempDir = mkdtempSync(join(tmpdir(), 'flowengine-cli-auth-test-'));
    options = { configDir: tempDir };
    setOutputOptions({ json: false, verbose: false });

    mockInstance = {
      interceptors: {
        request: { use: vi.fn() },
        response: { use: vi.fn() },
      },
      get: vi.fn(),
      post: vi.fn(),
    };
    vi.mocked(axios.create).mockReturnValue(mockInstance as unknown as AxiosInstance);
  });

  afterEach(() => {
    rmSync(tempDir, { recursive: true, force: true });
    vi.restoreAllMocks();
  });

  it('login - JWT success saves profile', async () => {
    const token = makeJwt({ exp: 1893456000 });
    mockInstance.post.mockResolvedValue({
      data: {
        success: true,
        token,
        userId: 'user-1',
        user: { email: 'a@example.com' },
      },
    });

    await login({
      url: 'http://localhost:5000',
      email: 'a@example.com',
      password: 'secret',
      profile: 'dev',
      configOptions: options,
    });

    const saved = getProfile('dev', options);
    expect(saved.baseUrl).toBe('http://localhost:5000');
    expect(saved.token).toBe(token);
    expect(saved.userId).toBe('user-1');
    expect(saved.email).toBe('a@example.com');
    expect(saved.apiKey).toBeUndefined();

    expect(mockInstance.post).toHaveBeenCalledWith('/api/v1/auth/login', {
      email: 'a@example.com',
      password: 'secret',
    });
  });

  it('login - API Key success saves profile', async () => {
    mockInstance.get.mockResolvedValue({
      data: {
        id: 'user-2',
        email: 'b@example.com',
        userName: 'user2',
      },
    });

    await login({
      url: 'http://localhost:5000',
      apiKey: 'api-key-value',
      profile: 'prod',
      configOptions: options,
    });

    const saved = getProfile('prod', options);
    expect(saved.baseUrl).toBe('http://localhost:5000');
    expect(saved.apiKey).toBe('api-key-value');
    expect(saved.userId).toBe('user-2');
    expect(saved.email).toBe('b@example.com');
    expect(saved.token).toBeUndefined();

    expect(mockInstance.get).toHaveBeenCalledWith('/api/v1/auth/me');
  });

  it('login - returns error when backend rejects credentials', async () => {
    mockInstance.post.mockRejectedValue(
      new CLIError(
        '邮箱或密码错误',
        ErrorCode.AuthRequired,
        ExitCode.BusinessFailure,
      ),
    );

    await expect(
      login({
        url: 'http://localhost:5000',
        email: 'a@example.com',
        password: 'wrong',
        configOptions: options,
      }),
    ).rejects.toThrow('邮箱或密码错误');
  });

  it('login - returns error when response indicates failure', async () => {
    mockInstance.post.mockResolvedValue({
      data: {
        success: false,
        errorMessage: '账号已锁定',
      },
    });

    await expect(
      login({
        url: 'http://localhost:5000',
        email: 'a@example.com',
        password: 'secret',
        configOptions: options,
      }),
    ).rejects.toThrow('账号已锁定');
  });

  it('logout - clears token and apiKey but keeps baseUrl', async () => {
    setProfile(
      'dev',
      {
        baseUrl: 'http://dev.example.com',
        token: 'jwt-token',
        apiKey: 'api-key-value',
        userId: 'user-1',
        email: 'a@example.com',
      },
      options,
    );

    mockInstance.post.mockResolvedValue({ data: {} });

    await logout({ profile: 'dev', configOptions: options });

    const saved = getProfile('dev', options);
    expect(saved.baseUrl).toBe('http://dev.example.com');
    expect(saved.token).toBeUndefined();
    expect(saved.apiKey).toBeUndefined();
    expect(saved.userId).toBeUndefined();
    expect(saved.email).toBeUndefined();
  });

  it('logout - skips logout API for API Key mode', async () => {
    setProfile(
      'prod',
      {
        baseUrl: 'http://prod.example.com',
        apiKey: 'api-key-value',
        userId: 'user-2',
        email: 'b@example.com',
      },
      options,
    );

    await logout({ profile: 'prod', configOptions: options });

    expect(mockInstance.post).not.toHaveBeenCalled();
    const saved = getProfile('prod', options);
    expect(saved.baseUrl).toBe('http://prod.example.com');
    expect(saved.apiKey).toBeUndefined();
  });

  it('profile - shows authType and token expiration', async () => {
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

    setOutputOptions({ json: true, verbose: false });
    const spy = vi.spyOn(console, 'log').mockImplementation(() => {});

    await profile({ profile: 'dev', configOptions: options });

    expect(spy).toHaveBeenCalledTimes(1);
    const parsed = JSON.parse(spy.mock.calls[0][0] as string);
    expect(parsed.profile).toBe('dev');
    expect(parsed.baseUrl).toBe('http://dev.example.com');
    expect(parsed.authType).toBe('jwt');
    expect(parsed.userId).toBe('user-1');
    expect(parsed.email).toBe('a@example.com');
    expect(parsed.tokenExpiresAt).toBe('2030-01-01T00:00:00.000Z');
    expect(parsed.tokenPrefix).toContain('****');
    expect(parsed.token).toBeUndefined();
    expect(parsed.apiKey).toBeUndefined();
    spy.mockRestore();
  });

  it('profile - detects apiKey auth type', async () => {
    setProfile(
      'prod',
      {
        baseUrl: 'http://prod.example.com',
        apiKey: 'api-key-value',
        userId: 'user-2',
        email: 'b@example.com',
      },
      options,
    );

    setOutputOptions({ json: true, verbose: false });
    const spy = vi.spyOn(console, 'log').mockImplementation(() => {});

    await profile({ profile: 'prod', configOptions: options });

    const parsed = JSON.parse(spy.mock.calls[0][0] as string);
    expect(parsed.authType).toBe('apiKey');
    expect(parsed.apiKeyPrefix).toContain('****');
    expect(parsed.apiKey).toBeUndefined();
    spy.mockRestore();
  });

  it('me - fetches current user in human mode', async () => {
    setProfile(
      'dev',
      {
        baseUrl: 'http://dev.example.com',
        token: 'jwt-token',
        userId: 'user-1',
        email: 'a@example.com',
      },
      options,
    );

    mockInstance.get.mockResolvedValue({
      data: {
        id: 'user-1',
        email: 'a@example.com',
        userName: 'alice',
        displayName: 'Alice',
        isActive: true,
        createdAt: '2024-01-01T00:00:00Z',
        updatedAt: '2025-01-01T00:00:00Z',
      },
    });

    setOutputOptions({ json: false, verbose: false });
    const spy = vi.spyOn(console, 'log').mockImplementation(() => {});

    await me({ profile: 'dev', configOptions: options });

    expect(mockInstance.get).toHaveBeenCalledWith('/auth/me');
    const output = spy.mock.calls.map((call) => call[0]).join('\n');
    expect(output).toContain('user-1');
    expect(output).toContain('a@example.com');
    expect(output).toContain('Alice');
    spy.mockRestore();
  });

  it('me - outputs JSON in json mode', async () => {
    setProfile(
      'dev',
      {
        baseUrl: 'http://dev.example.com',
        token: 'jwt-token',
        userId: 'user-1',
        email: 'a@example.com',
      },
      options,
    );

    mockInstance.get.mockResolvedValue({
      data: {
        id: 'user-1',
        email: 'a@example.com',
        userName: 'alice',
        isActive: true,
        createdAt: '2024-01-01T00:00:00Z',
      },
    });

    setOutputOptions({ json: true, verbose: false });
    const spy = vi.spyOn(console, 'log').mockImplementation(() => {});

    await me({ profile: 'dev', configOptions: options });

    const parsed = JSON.parse(spy.mock.calls[0][0] as string);
    expect(parsed.id).toBe('user-1');
    expect(parsed.email).toBe('a@example.com');
    expect(parsed.userName).toBe('alice');
    spy.mockRestore();
  });
});
