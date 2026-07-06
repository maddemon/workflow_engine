import { describe, it, expect } from 'vitest';
import {
  generateCron,
  parseCronToPreset,
  describePreset,
  getNextFireTime,
} from './cronUtils';

describe('generateCron', () => {
  it('generates minutes preset', () => {
    expect(generateCron({ type: 'minutes', interval: 5 })).toBe('*/5 * * * *');
    expect(generateCron({ type: 'minutes', interval: 1 })).toBe('*/1 * * * *');
    expect(generateCron({ type: 'minutes', interval: 30 })).toBe('*/30 * * * *');
  });

  it('generates hours preset', () => {
    expect(generateCron({ type: 'hours', interval: 1 })).toBe('0 */1 * * *');
    expect(generateCron({ type: 'hours', interval: 2 })).toBe('0 */2 * * *');
    expect(generateCron({ type: 'hours', interval: 6 })).toBe('0 */6 * * *');
  });

  it('generates daily preset', () => {
    expect(generateCron({ type: 'daily', hour: 9, minute: 0 })).toBe('0 9 * * *');
    expect(generateCron({ type: 'daily', hour: 14, minute: 30 })).toBe('30 14 * * *');
  });

  it('generates weekly preset', () => {
    expect(generateCron({ type: 'weekly', dayOfWeek: 1, hour: 9, minute: 0 })).toBe('0 9 * * 1');
    expect(generateCron({ type: 'weekly', dayOfWeek: 0, hour: 10, minute: 15 })).toBe('15 10 * * 0');
  });

  it('generates monthly preset', () => {
    expect(generateCron({ type: 'monthly', dayOfMonth: 1, hour: 9, minute: 0 })).toBe('0 9 1 * *');
    expect(generateCron({ type: 'monthly', dayOfMonth: 15, hour: 12, minute: 30 })).toBe('30 12 15 * *');
  });

  it('passes through custom expression', () => {
    expect(generateCron({ type: 'custom', expression: '0 9 * * 1-5' })).toBe('0 9 * * 1-5');
    expect(generateCron({ type: 'custom', expression: '  0 9 * * 1-5  ' })).toBe('0 9 * * 1-5');
  });
});

describe('parseCronToPreset', () => {
  it('parses minutes preset', () => {
    expect(parseCronToPreset('*/5 * * * *')).toEqual({ type: 'minutes', interval: 5 });
    expect(parseCronToPreset('*/1 * * * *')).toEqual({ type: 'minutes', interval: 1 });
    expect(parseCronToPreset('*/30 * * * *')).toEqual({ type: 'minutes', interval: 30 });
  });

  it('parses hours preset', () => {
    expect(parseCronToPreset('0 */1 * * *')).toEqual({ type: 'hours', interval: 1 });
    expect(parseCronToPreset('0 */2 * * *')).toEqual({ type: 'hours', interval: 2 });
    expect(parseCronToPreset('0 */6 * * *')).toEqual({ type: 'hours', interval: 6 });
  });

  it('parses daily preset', () => {
    expect(parseCronToPreset('0 9 * * *')).toEqual({ type: 'daily', hour: 9, minute: 0 });
    expect(parseCronToPreset('30 14 * * *')).toEqual({ type: 'daily', hour: 14, minute: 30 });
  });

  it('parses weekly preset', () => {
    expect(parseCronToPreset('0 9 * * 1')).toEqual({ type: 'weekly', dayOfWeek: 1, hour: 9, minute: 0 });
    expect(parseCronToPreset('15 10 * * 0')).toEqual({ type: 'weekly', dayOfWeek: 0, hour: 10, minute: 15 });
  });

  it('parses monthly preset', () => {
    expect(parseCronToPreset('0 9 1 * *')).toEqual({ type: 'monthly', dayOfMonth: 1, hour: 9, minute: 0 });
    expect(parseCronToPreset('30 12 15 * *')).toEqual({ type: 'monthly', dayOfMonth: 15, hour: 12, minute: 30 });
  });

  it('returns custom for unknown patterns', () => {
    expect(parseCronToPreset('0 9 * * 1-5')).toEqual({ type: 'custom', expression: '0 9 * * 1-5' });
    expect(parseCronToPreset('0 0 1,15 * *')).toEqual({ type: 'custom', expression: '0 0 1,15 * *' });
  });

  it('returns null for invalid expressions', () => {
    expect(parseCronToPreset('')).toBeNull();
    expect(parseCronToPreset('* *')).toBeNull();
    expect(parseCronToPreset('* * * * * *')).toBeNull();
  });
});

describe('describePreset', () => {
  it('describes minutes preset', () => {
    expect(describePreset({ type: 'minutes', interval: 1 })).toBe('Every minute');
    expect(describePreset({ type: 'minutes', interval: 5 })).toBe('Every 5 minutes');
  });

  it('describes hours preset', () => {
    expect(describePreset({ type: 'hours', interval: 1 })).toBe('Every hour');
    expect(describePreset({ type: 'hours', interval: 2 })).toBe('Every 2 hours');
  });

  it('describes daily preset', () => {
    expect(describePreset({ type: 'daily', hour: 9, minute: 0 })).toBe('Daily at 09:00');
    expect(describePreset({ type: 'daily', hour: 14, minute: 30 })).toBe('Daily at 14:30');
  });

  it('describes weekly preset', () => {
    expect(describePreset({ type: 'weekly', dayOfWeek: 1, hour: 9, minute: 0 })).toBe('Every Mon at 09:00');
    expect(describePreset({ type: 'weekly', dayOfWeek: 0, hour: 10, minute: 15 })).toBe('Every Sun at 10:15');
  });

  it('describes monthly preset', () => {
    expect(describePreset({ type: 'monthly', dayOfMonth: 1, hour: 9, minute: 0 })).toBe('Monthly on day 1 at 09:00');
    expect(describePreset({ type: 'monthly', dayOfMonth: 15, hour: 12, minute: 30 })).toBe('Monthly on day 15 at 12:30');
  });

  it('describes custom preset', () => {
    expect(describePreset({ type: 'custom', expression: '0 9 * * 1-5' })).toBe('Custom: 0 9 * * 1-5');
  });
});

describe('getNextFireTime', () => {
  it('returns null for invalid cron', () => {
    expect(getNextFireTime('')).toBeNull();
    expect(getNextFireTime('* *')).toBeNull();
  });

  it('calculates next fire for minutes interval', () => {
    const from = new Date('2026-07-06T10:03:00');
    const next = getNextFireTime('*/5 * * * *', from);
    expect(next).not.toBeNull();
    expect(next!.getMinutes()).toBe(5);
  });

  it('calculates next fire for daily schedule', () => {
    const from = new Date('2026-07-06T10:00:00');
    const next = getNextFireTime('0 9 * * *', from);
    expect(next).not.toBeNull();
    expect(next!.getHours()).toBe(9);
    expect(next!.getMinutes()).toBe(0);
    // Should be next day since 9:00 has passed
    expect(next!.getDate()).toBe(7);
  });
});
