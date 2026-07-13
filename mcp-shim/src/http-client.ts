/**
 * Minimal HTTP client for the MCP shim proxy.
 * Uses Node.js 18+ built-in fetch. No external dependencies.
 */

export interface HttpClientOptions {
  baseURL: string;
  apiKey: string;
  timeout?: number;
}

export interface HttpRequest {
  body: string;
  headers?: Record<string, string>;
}

export interface HttpResponse {
  status: number;
  body: string;
  headers: Record<string, string>;
}

const DEFAULT_TIMEOUT = 30_000;

export function createHttpClient(options: HttpClientOptions) {
  const baseURL = options.baseURL.replace(/\/+$/, '');
  const timeout = options.timeout ?? DEFAULT_TIMEOUT;

  return {
    async post(path: string, request: HttpRequest): Promise<HttpResponse> {
      const url = `${baseURL}${path}`;
      const headers: Record<string, string> = {
        'Content-Type': 'application/json',
        Authorization: `Bearer ${options.apiKey}`,
        ...request.headers,
      };

      const controller = new AbortController();
      const timer = setTimeout(() => controller.abort(), timeout);

      try {
        const response = await fetch(url, {
          method: 'POST',
          headers,
          body: request.body,
          signal: controller.signal,
        });

        const responseBody = await response.text();

        const responseHeaders: Record<string, string> = {};
        response.headers.forEach((value, key) => {
          responseHeaders[key] = value;
        });

        return {
          status: response.status,
          body: responseBody,
          headers: responseHeaders,
        };
      } finally {
        clearTimeout(timer);
      }
    },
  };
}

export type HttpClient = ReturnType<typeof createHttpClient>;
