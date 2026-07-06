/**
 * Cron expression builder utilities.
 * Converts user-friendly schedule presets to standard 5-field cron expressions.
 */

export type ScheduleType = 'minutes' | 'hours' | 'daily' | 'weekly' | 'monthly' | 'custom';

export interface MinutesPreset {
  type: 'minutes';
  interval: number; // 1-59
}

export interface HoursPreset {
  type: 'hours';
  interval: number; // 1-23
}

export interface DailyPreset {
  type: 'daily';
  hour: number; // 0-23
  minute: number; // 0-59
}

export interface WeeklyPreset {
  type: 'weekly';
  dayOfWeek: number; // 0=Sun, 1=Mon, ..., 6=Sat
  hour: number;
  minute: number;
}

export interface MonthlyPreset {
  type: 'monthly';
  dayOfMonth: number; // 1-31
  hour: number;
  minute: number;
}

export interface CustomPreset {
  type: 'custom';
  expression: string;
}

export type SchedulePreset =
  | MinutesPreset
  | HoursPreset
  | DailyPreset
  | WeeklyPreset
  | MonthlyPreset
  | CustomPreset;

const DAY_NAMES = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];

/**
 * Generate a 5-field cron expression from a schedule preset.
 */
export function generateCron(preset: SchedulePreset): string {
  switch (preset.type) {
    case 'minutes':
      return `*/${preset.interval} * * * *`;

    case 'hours':
      return `0 */${preset.interval} * * *`;

    case 'daily':
      return `${preset.minute} ${preset.hour} * * *`;

    case 'weekly':
      return `${preset.minute} ${preset.hour} * * ${preset.dayOfWeek}`;

    case 'monthly':
      return `${preset.minute} ${preset.hour} ${preset.dayOfMonth} * *`;

    case 'custom':
      return preset.expression.trim();
  }
}

/**
 * Parse a cron expression to a schedule preset.
 * Returns null if the expression doesn't match any known pattern.
 */
export function parseCronToPreset(cron: string): SchedulePreset | null {
  const parts = cron.trim().split(/\s+/);
  if (parts.length !== 5) return null;

  const [min, hour, dom, , dow] = parts;

  // Every X minutes: */X * * * *
  if (min.startsWith('*/') && hour === '*' && dom === '*') {
    const interval = parseInt(min.slice(2), 10);
    if (interval >= 1 && interval <= 59) {
      return { type: 'minutes', interval };
    }
  }

  // Every X hours: 0 */X * * *
  if (min === '0' && hour.startsWith('*/') && dom === '*') {
    const interval = parseInt(hour.slice(2), 10);
    if (interval >= 1 && interval <= 23) {
      return { type: 'hours', interval };
    }
  }

  // Daily: MM HH * * *
  if (dom === '*' && dow === '*') {
    const m = parseInt(min, 10);
    const h = parseInt(hour, 10);
    if (!isNaN(m) && !isNaN(h) && m >= 0 && m <= 59 && h >= 0 && h <= 23) {
      return { type: 'daily', hour: h, minute: m };
    }
  }

  // Weekly: MM HH * * D (single digit only, not ranges like 1-5)
  if (dom === '*' && dow !== '*' && /^\d+$/.test(dow)) {
    const m = parseInt(min, 10);
    const h = parseInt(hour, 10);
    const d = parseInt(dow, 10);
    if (!isNaN(m) && !isNaN(h) && !isNaN(d) && m >= 0 && m <= 59 && h >= 0 && h <= 23 && d >= 0 && d <= 6) {
      return { type: 'weekly', dayOfWeek: d, hour: h, minute: m };
    }
  }

  // Monthly: MM HH DD * * (single digit only, not ranges like 1,15)
  if (dom !== '*' && dow === '*' && /^\d+$/.test(dom)) {
    const m = parseInt(min, 10);
    const h = parseInt(hour, 10);
    const d = parseInt(dom, 10);
    if (!isNaN(m) && !isNaN(h) && !isNaN(d) && m >= 0 && m <= 59 && h >= 0 && h <= 23 && d >= 1 && d <= 31) {
      return { type: 'monthly', dayOfMonth: d, hour: h, minute: m };
    }
  }

  // Custom
  return { type: 'custom', expression: cron };
}

/**
 * Get a human-readable description of a schedule preset.
 */
export function describePreset(preset: SchedulePreset): string {
  switch (preset.type) {
    case 'minutes':
      return preset.interval === 1 ? 'Every minute' : `Every ${preset.interval} minutes`;

    case 'hours':
      return preset.interval === 1 ? 'Every hour' : `Every ${preset.interval} hours`;

    case 'daily':
      return `Daily at ${String(preset.hour).padStart(2, '0')}:${String(preset.minute).padStart(2, '0')}`;

    case 'weekly':
      return `Every ${DAY_NAMES[preset.dayOfWeek]} at ${String(preset.hour).padStart(2, '0')}:${String(preset.minute).padStart(2, '0')}`;

    case 'monthly':
      return `Monthly on day ${preset.dayOfMonth} at ${String(preset.hour).padStart(2, '0')}:${String(preset.minute).padStart(2, '0')}`;

    case 'custom':
      return `Custom: ${preset.expression}`;
  }
}

/**
 * Get the next fire time from a cron expression (simplified calculation).
 * Note: This is a simplified version for preview purposes only.
 * For accurate scheduling, use Quartz.NET on the backend.
 */
export function getNextFireTime(cron: string, from?: Date): Date | null {
  const parts = cron.trim().split(/\s+/);
  if (parts.length !== 5) return null;

  const [minExpr, hourExpr, , , dowExpr] = parts;
  const now = from ?? new Date();
  const next = new Date(now);

  // Start from next minute
  next.setSeconds(0);
  next.setMilliseconds(0);
  next.setMinutes(next.getMinutes() + 1);

  // Handle simple cases
  if (minExpr.startsWith('*/') && hourExpr === '*') {
    const interval = parseInt(minExpr.slice(2), 10);
    // Round up to next interval
    const currentMinute = next.getMinutes();
    const remainder = currentMinute % interval;
    if (remainder !== 0) {
      next.setMinutes(currentMinute + (interval - remainder));
    }
    return next;
  }

  if (minExpr === '0' && hourExpr.startsWith('*/')) {
    const interval = parseInt(hourExpr.slice(2), 10);
    next.setMinutes(0);
    const currentHour = next.getHours();
    const remainder = currentHour % interval;
    if (remainder !== 0 || next.getTime() <= now.getTime()) {
      next.setHours(currentHour + (interval - remainder));
    }
    return next;
  }

  // For daily/weekly/monthly, set time first
  const minute = parseInt(minExpr, 10);
  const hour = parseInt(hourExpr, 10);
  if (!isNaN(minute) && !isNaN(hour)) {
    next.setMinutes(minute);
    next.setHours(hour);

    if (next.getTime() <= now.getTime()) {
      next.setDate(next.getDate() + 1);
    }

    // Adjust for day of week if needed
    if (dowExpr !== '*') {
      const targetDay = parseInt(dowExpr, 10);
      while (next.getDay() !== targetDay) {
        next.setDate(next.getDate() + 1);
      }
    }

    return next;
  }

  return null;
}
