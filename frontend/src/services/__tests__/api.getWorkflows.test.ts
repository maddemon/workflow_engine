import { describe, it, expect, vi, beforeEach } from 'vitest';

// 真实加载 api.ts，但将其依赖的 axios 替换为可控实例，以便断言请求 URL。
vi.mock('axios', () => {
  const get = vi.fn();
  const instance = {
    get,
    post: vi.fn(),
    put: vi.fn(),
    delete: vi.fn(),
    interceptors: {
      request: { use: vi.fn() },
      response: { use: vi.fn() },
    },
    defaults: { headers: { set: vi.fn() } },
  };
  return {
    default: { create: vi.fn(() => instance) },
    create: vi.fn(() => instance),
    __get: get,
  };
});

import { getWorkflows } from '../../services/api.ts';

const mockedGet = vi.mocked(
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  (await import('axios') as any).__get,
);

describe('getWorkflows', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockedGet.mockResolvedValue({ data: { items: [], totalCount: 0 } });
  });

  it('requests a larger window via pageSize query param (default 200)', async () => {
    await getWorkflows();

    expect(mockedGet).toHaveBeenCalledTimes(1);
    expect(mockedGet.mock.calls[0][0]).toBe('/workflows?pageSize=200');
  });

  it('honours an explicit pageSize argument', async () => {
    await getWorkflows(50);

    expect(mockedGet.mock.calls[0][0]).toBe('/workflows?pageSize=50');
  });

  it('returns only the items array', async () => {
    mockedGet.mockResolvedValue({
      data: { items: [{ id: 'w1', name: 'Alpha' }], totalCount: 1 },
    });

    const items = await getWorkflows();

    expect(items).toEqual([{ id: 'w1', name: 'Alpha' }]);
  });
});
