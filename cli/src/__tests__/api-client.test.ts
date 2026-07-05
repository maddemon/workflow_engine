import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import axios, {
  AxiosError,
  AxiosHeaders,
  type AxiosInstance,
  type InternalAxiosRequestConfig,
} from 'axios';
import { createClient, maskSecrets } from '../api/client.js';
import { CLIError, ErrorCode, ExitCode } from '../errors.js';

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

describe('api/client', () => {
  let mockInstance: {
    interceptors: {
      request: { use: ReturnType<typeof vi.fn> };
      response: { use: ReturnType<typeof vi.fn> };
    };
    get: ReturnType<typeof vi.fn>;
    post: ReturnType<typeof vi.fn>;
  };

  beforeEach(() => {
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
    vi.restoreAllMocks();
  });

  it('createClient - registers request and response interceptors', () => {
    createClient({ baseURL: 'http://localhost:5000/api/v1' });
    expect(mockInstance.interceptors.request.use).toHaveBeenCalledTimes(1);
    expect(mockInstance.interceptors.response.use).toHaveBeenCalledTimes(1);
  });

  it('request interceptor - sets Bearer token when token provided', () => {
    createClient({ baseURL: 'http://localhost:5000/api/v1', token: 'jwt-token' });
    const requestFn = mockInstance.interceptors.request.use.mock
      .calls[0][0] as (config: InternalAxiosRequestConfig) => InternalAxiosRequestConfig;

    const config: InternalAxiosRequestConfig = {
      url: '/workflows',
      method: 'get',
      headers: new AxiosHeaders(),
    };

    const result = requestFn(config);
    expect(result.headers?.Authorization).toBe('Bearer jwt-token');
  });

  it('request interceptor - prefers token over apiKey', () => {
    createClient({
      baseURL: 'http://localhost:5000/api/v1',
      token: 'jwt-token',
      apiKey: 'api-key',
    });
    const requestFn = mockInstance.interceptors.request.use.mock
      .calls[0][0] as (config: InternalAxiosRequestConfig) => InternalAxiosRequestConfig;

    const config: InternalAxiosRequestConfig = {
      url: '/workflows',
      method: 'get',
      headers: new AxiosHeaders(),
    };

    const result = requestFn(config);
    expect(result.headers?.Authorization).toBe('Bearer jwt-token');
  });

  it('request interceptor - sets Bearer apiKey when no token', () => {
    createClient({ baseURL: 'http://localhost:5000/api/v1', apiKey: 'api-key' });
    const requestFn = mockInstance.interceptors.request.use.mock
      .calls[0][0] as (config: InternalAxiosRequestConfig) => InternalAxiosRequestConfig;

    const config: InternalAxiosRequestConfig = {
      url: '/workflows',
      method: 'get',
      headers: new AxiosHeaders(),
    };

    const result = requestFn(config);
    expect(result.headers?.Authorization).toBe('Bearer api-key');
  });

  it('response error interceptor - converts simple error body to CLIError', () => {
    createClient({ baseURL: 'http://localhost:5000/api/v1' });
    const errorFn = mockInstance.interceptors.response.use.mock
      .calls[0][1] as (error: unknown) => never;

    const axiosError = new AxiosError(
      'Request failed with status code 400',
      'ERR_BAD_REQUEST',
      undefined,
      undefined,
      {
        status: 400,
        statusText: 'Bad Request',
        data: { error: 'BadRequest', message: 'Nodes 不能为空。' },
        headers: {},
        config: {} as InternalAxiosRequestConfig,
        request: {},
      },
    );

    expect(() => errorFn(axiosError)).toThrow(CLIError);
    try {
      errorFn(axiosError);
    } catch (err) {
      const cliError = err as CLIError;
      expect(cliError.code).toBe(ErrorCode.ValidationError);
      expect(cliError.exitCode).toBe(ExitCode.BusinessFailure);
      expect(cliError.message).toBe('Nodes 不能为空。');
    }
  });

  it('response error interceptor - converts ProblemDetails to CLIError', () => {
    createClient({ baseURL: 'http://localhost:5000/api/v1' });
    const errorFn = mockInstance.interceptors.response.use.mock
      .calls[0][1] as (error: unknown) => never;

    const axiosError = new AxiosError(
      'Request failed with status code 401',
      'ERR_UNAUTHORIZED',
      undefined,
      undefined,
      {
        status: 401,
        statusText: 'Unauthorized',
        data: { title: 'Unauthorized', detail: 'Token 无效或已过期。' },
        headers: {},
        config: {} as InternalAxiosRequestConfig,
        request: {},
      },
    );

    expect(() => errorFn(axiosError)).toThrow(CLIError);
    try {
      errorFn(axiosError);
    } catch (err) {
      const cliError = err as CLIError;
      expect(cliError.code).toBe(ErrorCode.AuthRequired);
      expect(cliError.message).toBe('Token 无效或已过期。');
    }
  });

  it('response error interceptor - converts network error to CLIError', () => {
    createClient({ baseURL: 'http://localhost:5000/api/v1' });
    const errorFn = mockInstance.interceptors.response.use.mock
      .calls[0][1] as (error: unknown) => never;

    const axiosError = new AxiosError(
      'Network Error',
      'ERR_NETWORK',
      {} as InternalAxiosRequestConfig,
      {},
    );

    expect(() => errorFn(axiosError)).toThrow(CLIError);
    try {
      errorFn(axiosError);
    } catch (err) {
      const cliError = err as CLIError;
      expect(cliError.code).toBe(ErrorCode.NetworkError);
      expect(cliError.exitCode).toBe(ExitCode.InvocationError);
    }
  });

  it('maskSecrets - masks authorization header', () => {
    const masked = maskSecrets({ Authorization: 'Bearer super-secret-token' }) as Record<
      string,
      unknown
    >;
    expect(masked.Authorization).toBe('Bearer ***');
  });

  it('maskSecrets - masks token, password and credential fields', () => {
    const masked = maskSecrets({
      token: 'my-token',
      password: 'my-password',
      fields: {
        apiKey: 'nested-key',
        secret: 'nested-secret',
      },
      public: 'keep-me',
    }) as Record<string, unknown>;

    expect(masked.token).toBe('***');
    expect(masked.password).toBe('***');
    expect((masked.fields as Record<string, unknown>).apiKey).toBe('***');
    expect((masked.fields as Record<string, unknown>).secret).toBe('***');
    expect(masked.public).toBe('keep-me');
  });

  it('maskSecrets - does not mask non-sensitive fields', () => {
    const masked = maskSecrets({
      name: 'workflow-name',
      key: 'plain-key-name',
      fields: {
        label: 'visible',
        secret: 'hidden',
      },
    }) as Record<string, unknown>;

    expect(masked.name).toBe('workflow-name');
    expect(masked.key).toBe('plain-key-name');
    expect((masked.fields as Record<string, unknown>).label).toBe('visible');
    expect((masked.fields as Record<string, unknown>).secret).toBe('***');
  });
});
