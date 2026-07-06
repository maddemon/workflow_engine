import { useState, useEffect, useMemo } from 'react';
import {
  Stack,
  Select,
  NumberInput,
  Group,
  Text,
  Paper,
  Box,
} from '@mantine/core';
import {
  generateCron,
  parseCronToPreset,
  describePreset,
  type SchedulePreset,
} from '../../../utils/cronUtils.ts';

const DAY_OPTIONS = [
  { label: 'Sunday', value: '0' },
  { label: 'Monday', value: '1' },
  { label: 'Tuesday', value: '2' },
  { label: 'Wednesday', value: '3' },
  { label: 'Thursday', value: '4' },
  { label: 'Friday', value: '5' },
  { label: 'Saturday', value: '6' },
];

const TYPE_OPTIONS = [
  { label: 'Every X minutes', value: 'minutes' },
  { label: 'Every X hours', value: 'hours' },
  { label: 'Daily', value: 'daily' },
  { label: 'Weekly', value: 'weekly' },
  { label: 'Monthly', value: 'monthly' },
  { label: 'Custom (Advanced)', value: 'custom' },
];

interface TimePickerProps {
  hour: number;
  minute: number;
  onChange: (hour: number, minute: number) => void;
  disabled?: boolean;
}

function TimePicker({ hour, minute, onChange, disabled }: TimePickerProps) {
  const hours = Array.from({ length: 24 }, (_, i) => ({
    label: String(i).padStart(2, '0'),
    value: String(i),
  }));

  const minutes = Array.from({ length: 60 }, (_, i) => ({
    label: String(i).padStart(2, '0'),
    value: String(i),
  }));

  return (
    <Group gap="xs" wrap="nowrap">
      <Select
        data={hours}
        value={String(hour)}
        onChange={(v) => onChange(parseInt(v ?? '0', 10), minute)}
        disabled={disabled}
        size="xs"
        w={70}
      />
      <Text size="xs">:</Text>
      <Select
        data={minutes}
        value={String(minute)}
        onChange={(v) => onChange(hour, parseInt(v ?? '0', 10))}
        disabled={disabled}
        size="xs"
        w={70}
      />
    </Group>
  );
}

interface CronBuilderProps {
  value: string;
  onChange: (cron: string) => void;
  disabled?: boolean;
}

export function CronBuilder({ value, onChange, disabled }: CronBuilderProps) {
  const initialPreset = useMemo(() => parseCronToPreset(value), []);

  const [presetType, setPresetType] = useState<SchedulePreset['type']>(
    initialPreset?.type ?? 'daily'
  );
  const [interval, setInterval] = useState<number>(
    initialPreset?.type === 'minutes' || initialPreset?.type === 'hours'
      ? initialPreset.interval
      : 5
  );
  const [hour, setHour] = useState<number>(
    initialPreset && 'hour' in initialPreset ? initialPreset.hour : 9
  );
  const [minute, setMinute] = useState<number>(
    initialPreset && 'minute' in initialPreset ? initialPreset.minute : 0
  );
  const [dayOfWeek, setDayOfWeek] = useState<number>(
    initialPreset?.type === 'weekly' ? initialPreset.dayOfWeek : 1
  );
  const [dayOfMonth, setDayOfMonth] = useState<number>(
    initialPreset?.type === 'monthly' ? initialPreset.dayOfMonth : 1
  );
  const [customExpression, setCustomExpression] = useState<string>(
    initialPreset?.type === 'custom' ? initialPreset.expression : '0 9 * * *'
  );

  // Generate cron expression from current state
  const cronExpression = useMemo(() => {
    const preset: SchedulePreset = (() => {
      switch (presetType) {
        case 'minutes':
          return { type: 'minutes', interval };
        case 'hours':
          return { type: 'hours', interval };
        case 'daily':
          return { type: 'daily', hour, minute };
        case 'weekly':
          return { type: 'weekly', dayOfWeek, hour, minute };
        case 'monthly':
          return { type: 'monthly', dayOfMonth, hour, minute };
        case 'custom':
          return { type: 'custom', expression: customExpression };
      }
    })();
    return generateCron(preset);
  }, [presetType, interval, hour, minute, dayOfWeek, dayOfMonth, customExpression]);

  // Notify parent when cron changes
  useEffect(() => {
    onChange(cronExpression);
  }, [cronExpression, onChange]);

  const handleTimeChange = (h: number, m: number) => {
    setHour(h);
    setMinute(m);
  };

  return (
    <Stack gap="sm">
      <Select
        label="Schedule"
        data={TYPE_OPTIONS}
        value={presetType}
        onChange={(v) => setPresetType((v as SchedulePreset['type']) ?? 'daily')}
        disabled={disabled}
      />

      {presetType === 'minutes' && (
        <NumberInput
          label="Every X minutes"
          value={interval}
          onChange={(v) => setInterval(typeof v === 'number' ? v : 5)}
          min={1}
          max={59}
          disabled={disabled}
          description="Interval in minutes (1-59)"
        />
      )}

      {presetType === 'hours' && (
        <NumberInput
          label="Every X hours"
          value={interval}
          onChange={(v) => setInterval(typeof v === 'number' ? v : 1)}
          min={1}
          max={23}
          disabled={disabled}
          description="Interval in hours (1-23)"
        />
      )}

      {presetType === 'daily' && (
        <Stack gap="xs">
          <Text size="xs" fw={500}>Time</Text>
          <TimePicker hour={hour} minute={minute} onChange={handleTimeChange} disabled={disabled} />
        </Stack>
      )}

      {presetType === 'weekly' && (
        <Stack gap="xs">
          <Select
            label="Day of Week"
            data={DAY_OPTIONS}
            value={String(dayOfWeek)}
            onChange={(v) => setDayOfWeek(parseInt(v ?? '1', 10))}
            disabled={disabled}
          />
          <Text size="xs" fw={500}>Time</Text>
          <TimePicker hour={hour} minute={minute} onChange={handleTimeChange} disabled={disabled} />
        </Stack>
      )}

      {presetType === 'monthly' && (
        <Stack gap="xs">
          <NumberInput
            label="Day of Month"
            value={dayOfMonth}
            onChange={(v) => setDayOfMonth(typeof v === 'number' ? v : 1)}
            min={1}
            max={31}
            disabled={disabled}
          />
          <Text size="xs" fw={500}>Time</Text>
          <TimePicker hour={hour} minute={minute} onChange={handleTimeChange} disabled={disabled} />
        </Stack>
      )}

      {presetType === 'custom' && (
        <Stack gap="xs">
          <Text size="xs" fw={500}>Cron Expression</Text>
          <Box
            component="input"
            value={customExpression}
            onChange={(e: React.ChangeEvent<HTMLInputElement>) => setCustomExpression(e.target.value)}
            disabled={disabled}
            style={{
              fontFamily: 'monospace',
              padding: '8px 12px',
              borderRadius: 'var(--mantine-radius-sm)',
              border: '1px solid var(--mantine-color-default-border)',
              fontSize: 'var(--mantine-font-size-sm)',
              width: '100%',
              boxSizing: 'border-box',
            }}
            placeholder="*/5 * * * *"
          />
          <Text size="xs" c="dimmed">
            Format: minute hour day-of-month month day-of-week
          </Text>
        </Stack>
      )}

      {/* Preview */}
      <Paper p="xs" withBorder bg="var(--mantine-color-gray-0)">
        <Stack gap={4}>
          <Group gap="xs">
            <Text size="xs" fw={600}>Generated:</Text>
            <Text size="xs" ff="monospace">{cronExpression}</Text>
          </Group>
          <Text size="xs" c="dimmed">
            {describePreset(
              presetType === 'custom'
                ? { type: 'custom', expression: cronExpression }
                : { type: presetType, ...(presetType === 'minutes' || presetType === 'hours' ? { interval } : { hour, minute, ...(presetType === 'weekly' ? { dayOfWeek } : {}), ...(presetType === 'monthly' ? { dayOfMonth } : {}) }) } as SchedulePreset
            )}
          </Text>
        </Stack>
      </Paper>
    </Stack>
  );
}
