import axios, {
  AxiosError,
  AxiosHeaders,
  type AxiosInstance,
  type InternalAxiosRequestConfig,
  type AxiosResponse,
} from 'axios';
import { CLIError, ErrorCode, ExitCode } from '../errors.js';
import { verbose } from '../output.js';

export interface ApiClientOptions {
  baseURL: string;
  token?: string;
  apiKey?: string;
  timeout?: number;
  verbose?: boolean;
}

interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  instance?: string;
  errors?: Record<string, string[]>;
}

interface SimpleErrorBody {
  error?: string;
  message?: string;
}

const DEFAULT_TIMEOUT = 30_000;
const SENSITIVE_KEYS = new Set([
  'authorization',
  'token',
  'apikey',
  'api-key',
  'api_key',
  'password',
  'secret',
  'privatekey',
  'private-key',
  'private_key',
  'accesstoken',
  'access-token',
  'access_token',
  'refreshtoken',
  'refresh-token',
  'refresh_token',
]);

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function isSensitiveKey(key: string): boolean {
  return SENSITIVE_KEYS.has(key.toLowerCase());
}

function maskString(value: string): string {
  return '***';
}

function maskAuthorization(value: string): string {
  const trimmed = value.trim();
  if (trimmed.toLowerCase().startsWith('bearer ')) {
    return `Bearer ${maskString(trimmed.slice(7))}`;
  }
  return maskString(value);
}

export function maskSecrets(value: unknown): unknown {
  if (value === null || value === undefined) {
    return value;
  }

  if (Array.isArray(value)) {
    return value.map((item) => maskSecrets(item));
  }

  if (!isRecord(value)) {
    return value;
  }

  const masked: Record<string, unknown> = {};
  for (const [key, val] of Object.entries(value)) {
    if (key.toLowerCase() === 'authorization' && typeof val === 'string') {
      masked[key] = maskAuthorization(val);
    } else if (isSensitiveKey(key)) {
      if (typeof val === 'string') {
        masked[key] = maskString(val);
      } else if (isRecord(val)) {
        const nested: Record<string, unknown> = {};
        for (const [nestedKey, nestedVal] of Object.entries(val)) {
          nested[nestedKey] =
            typeof nestedVal === 'string' ? maskString(nestedVal) : maskSecrets(nestedVal);
        }
        masked[key] = nested;
      } else {
        masked[key] = maskSecrets(val);
      }
    } else {
      masked[key] = maskSecrets(val);
    }
  }
  return masked;
}

function parseErrorData(data: unknown): { message: string; details?: unknown } {
  if (typeof data === 'string') {
    return { message: data };
  }

  if (!isRecord(data)) {
    return { message: '请求失败' };
  }

  const simple = data as SimpleErrorBody;
  if (typeof simple.message === 'string' && simple.message.length > 0) {
    return { message: simple.message, details: data };
  }
  if (typeof simple.error === 'string' && simple.error.length > 0) {
    return { message: simple.error, details: data };
  }

  const problem = data as ProblemDetails;
  if (typeof problem.detail === 'string' && problem.detail.length > 0) {
    return { message: problem.detail, details: problem.errors ?? data };
  }
  if (typeof problem.title === 'string' && problem.title.length > 0) {
    return { message: problem.title, details: problem.errors ?? data };
  }

  return { message: '请求失败', details: data };
}

function mapStatusToError(
  status: number,
  message: string,
  details?: unknown,
): CLIError {
  switch (status) {
    case 401:
      return new CLIError(message, ErrorCode.AuthRequired, ExitCode.BusinessFailure, details);
    case 403:
      return new CLIError(message, ErrorCode.Forbidden, ExitCode.BusinessFailure, details);
    case 404:
      return new CLIError(message, ErrorCode.NotFound, ExitCode.BusinessFailure, details);
    case 409:
      return new CLIError(message, ErrorCode.Conflict, ExitCode.BusinessFailure, details);
    case 400:
    case 422:
      return new CLIError(
        message,
        ErrorCode.ValidationError,
        ExitCode.BusinessFailure,
        details,
      );
    default:
      if (status >= 500) {
        return new CLIError(message, ErrorCode.ServerError, ExitCode.BusinessFailure, details);
      }
      return new CLIError(message, ErrorCode.ApiError, ExitCode.BusinessFailure, details);
  }
}

function normalizeError(error: unknown): CLIError {
  if (!axios.isAxiosError(error)) {
    const message = error instanceof Error ? error.message : String(error);
    return new CLIError(
      message,
      ErrorCode.UnexpectedError,
      ExitCode.InvocationError,
      error,
    );
  }

  const axiosError = error as AxiosError;
  if (axiosError.response) {
    const { status, data } = axiosError.response;
    const parsed = parseErrorData(data);
    return mapStatusToError(status, parsed.message, parsed.details);
  }

  if (axiosError.request) {
    return new CLIError(
      axiosError.message || '网络请求失败',
      ErrorCode.NetworkError,
      ExitCode.InvocationError,
      axiosError,
    );
  }

  return new CLIError(
    axiosError.message || '请求配置错误',
    ErrorCode.UnexpectedError,
    ExitCode.InvocationError,
    axiosError,
  );
}

function logRequest(config: InternalAxiosRequestConfig): void {
  const method = config.method?.toUpperCase() ?? 'UNKNOWN';
  verbose(`→ ${method} ${config.baseURL ?? ''}${config.url ?? ''}`);
  verbose(`headers: ${JSON.stringify(maskSecrets(config.headers))}`);
  if (config.data !== undefined) {
    verbose(`body: ${JSON.stringify(maskSecrets(config.data))}`);
  }
}

function logResponse(response: AxiosResponse): void {
  verbose(`← ${response.status} ${response.config.url ?? ''}`);
  verbose(`body: ${JSON.stringify(maskSecrets(response.data))}`);
}

export function createClient(options: ApiClientOptions): AxiosInstance {
  const client = axios.create({
    baseURL: options.baseURL,
    timeout: options.timeout ?? DEFAULT_TIMEOUT,
    headers: { 'Content-Type': 'application/json' },
  });

  client.interceptors.request.use((config) => {
    if (!config.headers) {
      config.headers = new AxiosHeaders();
    }

    if (options.token) {
      config.headers.Authorization = `Bearer ${options.token}`;
    } else if (options.apiKey) {
      config.headers.Authorization = `Bearer ${options.apiKey}`;
    }

    if (options.verbose) {
      logRequest(config);
    }

    return config;
  });

  client.interceptors.response.use(
    (response) => {
      if (options.verbose) {
        logResponse(response);
      }
      return response;
    },
    (error: unknown) => {
      if (options.verbose) {
        if (axios.isAxiosError(error) && error.response) {
          verbose(`← ${error.response.status} ${error.config?.url ?? ''}`);
          verbose(`body: ${JSON.stringify(maskSecrets(error.response.data))}`);
        }
      }
      throw normalizeError(error);
    },
  );

  return client;
}
