import { describe, it, expect } from 'vitest';
import {
  msToTimeSpan,
  timeSpanToMs,
  toRetryPolicyDto,
  fromRetryPolicyDto,
  DEFAULT_RETRY_POLICY_UI,
} from '../retryPolicy';

describe('retryPolicy conversion (#7)', () => {
  describe('msToTimeSpan', () => {
    it('formats whole seconds as hh:mm:ss', () => {
      expect(msToTimeSpan(1000)).toBe('00:00:01');
      expect(msToTimeSpan(5000)).toBe('00:00:05');
      expect(msToTimeSpan(10000)).toBe('00:00:10');
    });

    it('clamps negative and sub-second values to 00:00:00', () => {
      expect(msToTimeSpan(0)).toBe('00:00:00');
      expect(msToTimeSpan(-50)).toBe('00:00:00');
      expect(msToTimeSpan(500)).toBe('00:00:00');
    });

    it('formats hours and minutes', () => {
      expect(msToTimeSpan(2 * 3600 * 1000 + 3 * 60 * 1000 + 5 * 1000)).toBe('02:03:05');
    });
  });

  describe('timeSpanToMs', () => {
    it('parses hh:mm:ss', () => {
      expect(timeSpanToMs('00:00:01')).toBe(1000);
      expect(timeSpanToMs('00:00:10')).toBe(10000);
      expect(timeSpanToMs('02:03:05')).toBe(2 * 3600 * 1000 + 3 * 60 * 1000 + 5 * 1000);
    });

    it('parses fractional seconds', () => {
      expect(timeSpanToMs('00:00:01.500')).toBe(1500);
    });

    it('parses day prefix', () => {
      expect(timeSpanToMs('1.00:00:00')).toBe(24 * 3600 * 1000);
    });

    it('returns NaN for unparseable input', () => {
      expect(Number.isNaN(timeSpanToMs(''))).toBe(true);
      expect(Number.isNaN(timeSpanToMs('abc'))).toBe(true);
      expect(Number.isNaN(timeSpanToMs('00:00'))).toBe(true);
    });
  });

  describe('round-trip', () => {
    it('msToTimeSpan(timeSpanToMs(x)) equals for whole seconds', () => {
      for (const ms of [1000, 5000, 10000, 3600000]) {
        expect(timeSpanToMs(msToTimeSpan(ms))).toBe(ms);
      }
    });

    it('serialize → deserialize of RetryPolicyDto equals (normal)', () => {
      const ui = { ...DEFAULT_RETRY_POLICY_UI };
      const roundTripped = fromRetryPolicyDto(toRetryPolicyDto(ui));
      expect(roundTripped).toEqual(ui);
    });

    it('serialize → deserialize preserves custom values (boundary)', () => {
      const ui = {
        maxRetries: 5,
        baseDelayMs: 2000,
        maxDelayMs: 2 * 3600 * 1000 + 30 * 60 * 1000, // 2h30m
        useJitter: true,
        backoffStrategy: 'Linear',
        retryableErrorCodes: ['Timeout', 'RateLimit'],
      };
      const dto = toRetryPolicyDto(ui);
      // baseDelay / maxDelay 必须是后端可绑定的 TimeSpan 字符串格式。
      expect(dto.baseDelay).toMatch(/^\d{2}:\d{2}:\d{2}$/);
      expect(dto.maxDelay).toMatch(/^\d{2}:\d{2}:\d{2}$/);
      const roundTripped = fromRetryPolicyDto(dto);
      expect(roundTripped).toEqual(ui);
    });

    it('round-trips null retryableErrorCodes', () => {
      const ui = { ...DEFAULT_RETRY_POLICY_UI, retryableErrorCodes: null };
      expect(fromRetryPolicyDto(toRetryPolicyDto(ui)).retryableErrorCodes).toBeNull();
    });
  });
});
