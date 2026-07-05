import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import axios, { type AxiosInstance } from 'axios';
import { mkdtempSync, rmSync } from 'node:fs';
import { join } from 'node:path';
import { tmpdir } from 'node:os';
import { apiKeyCreate, apiKeyList, apiKeyRevoke } from '../commands/api-keys.js';
import { setProfile, type ConfigOptions } from '../config.js';
import { CLIError, ExitCode } from '../errors.js';
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

describe('commands/api-keys', () => {
  let tempDir: string;
  let options: ConfigOptions;
  let mockInstance: {
    interceptors: {
      request: { use: ReturnType<typeof vi.fn> };
      response: { use: ReturnType<typeof vi.fn> };
    };
    get: ReturnType<typeof vi.fn>;
    post: ReturnType<typeof vi.fn>;
    delete: ReturnType<typeof vi.fn>;
  };

  beforeEach(() => {
    tempDir = mkdtempSync(join(tmpdir(), 'flowengine-cli-apikeys-test-'));
    options = { configDir: tempDir };
    setOutputOptions({ json: false, verbose: false });

    mockInstance = {
      interceptors: {
        request: { use: vi.fn() },
        response: { use: vi.fn() },
      },
      get: vi.fn(),
      post: vi.fn(),
      delete: vi.fn(),
    };
    vi.mocked(axios.create).mockReturnValue(mockInstance as unknown as AxiosInstance);

    setProfile(
      'default',
      {
        baseUrl: 'http://localhost:5000',
        token: 'jwt-token',
        userId: 'user-1',
        email: 'a@example.com',
      },
      options,
    );
  });

  afterEach(() => {
    rmSync(tempDir, { recursive: true, force: true });
    vi.restoreAllMocks();
  });

  describe('apiKeyCreate', () => {
    it('creates API key and prints key in human mode', async () => {
      mockInstance.post.mockResolvedValue({
        data: {
          id: 'key-1',
          name: 'CI',
          prefix: 'fe_',
          expiresAt: '2030-01-01T00:00:00Z',
          key: 'fe_secret_value',
        },
      });

      const spy = vi.spyOn(console, 'log').mockImplementation(() => {});
      await apiKeyCreate({ name: 'CI', expiresAt: '2030-01-01T00:00:00Z', configOptions: options });

      expect(mockInstance.post).toHaveBeenCalledWith('/auth/api-keys', {
        name: 'CI',
        expiresAt: '2030-01-01T00:00:00Z',
      });
      const output = spy.mock.calls.map((call) => call[0]).join('\n');
      expect(output).toContain('fe_secret_value');
      expect(output).toContain('key-1');
      spy.mockRestore();
    });

    it('rejects empty name', async () => {
      await expect(apiKeyCreate({ name: '   ', configOptions: options })).rejects.toThrow(CLIError);
    });
  });

  describe('apiKeyList', () => {
    it('lists API keys', async () => {
      mockInstance.get.mockResolvedValue({
        data: [
          {
            id: 'key-1',
            name: 'CI',
            prefix: 'fe_',
            createdAt: '2025-01-01T00:00:00Z',
            expiresAt: '2030-01-01T00:00:00Z',
          },
          {
            id: 'key-2',
            name: 'Deprecated',
            prefix: 'fe_',
            createdAt: '2024-01-01T00:00:00Z',
            revokedAt: '2024-06-01T00:00:00Z',
          },
        ],
      });

      const spy = vi.spyOn(console, 'log').mockImplementation(() => {});
      await apiKeyList({ configOptions: options });

      expect(mockInstance.get).toHaveBeenCalledWith('/auth/api-keys');
      const output = spy.mock.calls.map((call) => call[0]).join('\n');
      expect(output).toContain('key-1');
      expect(output).toContain('有效');
      expect(output).toContain('key-2');
      expect(output).toContain('已吊销');
      spy.mockRestore();
    });
  });

  describe('apiKeyRevoke', () => {
    it('revokes with confirm', async () => {
      mockInstance.delete.mockResolvedValue({ data: {} });

      const spy = vi.spyOn(console, 'log').mockImplementation(() => {});
      await apiKeyRevoke({ id: 'key-1', confirm: true, configOptions: options });

      expect(mockInstance.delete).toHaveBeenCalledWith('/auth/api-keys/key-1');
      const output = spy.mock.calls.map((call) => call[0]).join('\n');
      expect(output).toContain('key-1');
      spy.mockRestore();
    });

    it('requires confirm in human mode', async () => {
      await expect(
        apiKeyRevoke({ id: 'key-1', configOptions: options }),
      ).rejects.toThrow('确认');
    });

    it('rejects empty id', async () => {
      await expect(
        apiKeyRevoke({ id: '   ', confirm: true, configOptions: options }),
      ).rejects.toThrow(CLIError);
    });
  });
});
