import type { RetryPolicyDto } from '../types/workflow.ts';

/**
 * 前端 UI 态的重试策略：延迟以毫秒计，便于 NumberInput / Select 输入。
 * 与后端 RetryPolicyDto（延迟为 TimeSpan 字符串 "hh:mm:ss"）分离，避免类型混淆。
 */
export interface RetryPolicyUi {
  maxRetries: number;
  baseDelayMs: number;
  maxDelayMs: number;
  useJitter: boolean;
  backoffStrategy: string;
  retryableErrorCodes?: string[] | null;
}

/** 默认重试策略（首次开启重试时使用）。 */
export const DEFAULT_RETRY_POLICY_UI: RetryPolicyUi = {
  maxRetries: 2,
  baseDelayMs: 1000,
  maxDelayMs: 10000,
  useJitter: false,
  backoffStrategy: 'Exponential',
  retryableErrorCodes: null,
};

/**
 * 将毫秒转换为 TimeSpan 字符串 "hh:mm:ss"（System.Text.Json 默认绑定格式）。
 * 不足一秒的部分向下取整到整秒（后端 TimeSpan 精度足够，UI 以秒为粒度）。
 */
export function msToTimeSpan(ms: number): string {
  const totalSeconds = Math.max(0, Math.floor(ms / 1000));
  const hours = Math.floor(totalSeconds / 3600);
  const minutes = Math.floor((totalSeconds % 3600) / 60);
  const seconds = totalSeconds % 60;
  return `${pad2(hours)}:${pad2(minutes)}:${pad2(seconds)}`;
}

/**
 * 将 TimeSpan 字符串解析为毫秒。支持 "hh:mm:ss"、可选小数部分 ".fffffff"，
 * 以及可选的天部分 "d.hh:mm:ss"。无法解析时返回 NaN。
 */
export function timeSpanToMs(timeSpan: string): number {
  const trimmed = (timeSpan ?? '').trim();
  if (!trimmed) return NaN;

  let rest = trimmed;
  let days = 0;
  const dayMatch = rest.match(/^(\d+)\./);
  if (dayMatch) {
    days = Number(dayMatch[1]);
    rest = rest.slice(dayMatch[0].length);
  }

  const parts = rest.split(':');
  if (parts.length !== 3) return NaN;

  const [h, m, s] = parts;
  const hours = Number(h);
  const minutes = Number(m);
  const secondsPart = s.split('.');
  const seconds = Number(secondsPart[0]);
  const fractionMs = secondsPart[1]
    ? Math.round(Number(`0.${secondsPart[1]}`) * 1000)
    : 0;

  if (Number.isNaN(hours) || Number.isNaN(minutes) || Number.isNaN(seconds)) return NaN;

  return (((days * 24 + hours) * 60 + minutes) * 60 + seconds) * 1000 + fractionMs;
}

/** 将前端 UI 态转换为后端 RetryPolicyDto（延迟转为 TimeSpan 字符串）。 */
export function toRetryPolicyDto(ui: RetryPolicyUi): RetryPolicyDto {
  return {
    maxRetries: ui.maxRetries,
    baseDelay: msToTimeSpan(ui.baseDelayMs),
    maxDelay: msToTimeSpan(ui.maxDelayMs),
    useJitter: ui.useJitter,
    backoffStrategy: ui.backoffStrategy,
    retryableErrorCodes: ui.retryableErrorCodes,
  };
}

/** 将后端 RetryPolicyDto 转换为前端 UI 态（TimeSpan 字符串转为毫秒）。 */
export function fromRetryPolicyDto(dto: RetryPolicyDto): RetryPolicyUi {
  return {
    maxRetries: dto.maxRetries,
    baseDelayMs: timeSpanToMs(dto.baseDelay),
    maxDelayMs: timeSpanToMs(dto.maxDelay),
    useJitter: dto.useJitter,
    backoffStrategy: dto.backoffStrategy,
    retryableErrorCodes: dto.retryableErrorCodes,
  };
}

function pad2(n: number): string {
  return String(n).padStart(2, '0');
}
