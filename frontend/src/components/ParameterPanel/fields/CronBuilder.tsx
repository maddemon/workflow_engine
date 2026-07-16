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
import { useTranslation } from 'react-i18next';
import {
  generateCron,
  parseCronToPreset,
  describePreset,
  type SchedulePreset,
} from '../../../utils/cronUtils.ts';

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
  const { t } = useTranslation('parameterPanel');
  const initialPreset = useMemo(() => parseCronToPreset(value), [value]);

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

  const DAY_OPTIONS = [
    { label: t('fields.cronBuilder.sunday'), value: '0' },
    { label: t('fields.cronBuilder.monday'), value: '1' },
    { label: t('fields.cronBuilder.tuesday'), value: '2' },
    { label: t('fields.cronBuilder.wednesday'), value: '3' },
    { label: t('fields.cronBuilder.thursday'), value: '4' },
    { label: t('fields.cronBuilder.friday'), value: '5' },
    { label: t('fields.cronBuilder.saturday'), value: '6' },
  ];

  const TYPE_OPTIONS = [
    { label: t('fields.cronBuilder.everyXMinutes'), value: 'minutes' },
    { label: t('fields.cronBuilder.everyXHours'), value: 'hours' },
    { label: t('fields.cronBuilder.daily'), value: 'daily' },
    { label: t('fields.cronBuilder.weekly'), value: 'weekly' },
    { label: t('fields.cronBuilder.monthly'), value: 'monthly' },
    { label: t('fields.cronBuilder.custom'), value: 'custom' },
  ];

  return (
    <Stack gap="sm">
      <Select
        label={t('fields.cronBuilder.schedule')}
        data={TYPE_OPTIONS}
        value={presetType}
        onChange={(v) => setPresetType((v as SchedulePreset['type']) ?? 'daily')}
        disabled={disabled}
      />

      {presetType === 'minutes' && (
        <NumberInput
          label={t('fields.cronBuilder.everyXMinutes')}
          value={interval}
          onChange={(v) => setInterval(typeof v === 'number' ? v : 5)}
          min={1}
          max={59}
          disabled={disabled}
          description={t('fields.cronBuilder.intervalMinutes')}
        />
      )}

      {presetType === 'hours' && (
        <NumberInput
          label={t('fields.cronBuilder.everyXHours')}
          value={interval}
          onChange={(v) => setInterval(typeof v === 'number' ? v : 1)}
          min={1}
          max={23}
          disabled={disabled}
          description={t('fields.cronBuilder.intervalHours')}
        />
      )}

      {presetType === 'daily' && (
        <Stack gap="xs">
          <Text size="xs" fw={500}>{t('fields.cronBuilder.time')}</Text>
          <TimePicker hour={hour} minute={minute} onChange={handleTimeChange} disabled={disabled} />
        </Stack>
      )}

      {presetType === 'weekly' && (
        <Stack gap="xs">
          <Select
            label={t('fields.cronBuilder.dayOfWeek')}
            data={DAY_OPTIONS}
            value={String(dayOfWeek)}
            onChange={(v) => setDayOfWeek(parseInt(v ?? '1', 10))}
            disabled={disabled}
          />
          <Text size="xs" fw={500}>{t('fields.cronBuilder.time')}</Text>
          <TimePicker hour={hour} minute={minute} onChange={handleTimeChange} disabled={disabled} />
        </Stack>
      )}

      {presetType === 'monthly' && (
        <Stack gap="xs">
          <NumberInput
            label={t('fields.cronBuilder.dayOfMonth')}
            value={dayOfMonth}
            onChange={(v) => setDayOfMonth(typeof v === 'number' ? v : 1)}
            min={1}
            max={31}
            disabled={disabled}
          />
          <Text size="xs" fw={500}>{t('fields.cronBuilder.time')}</Text>
          <TimePicker hour={hour} minute={minute} onChange={handleTimeChange} disabled={disabled} />
        </Stack>
      )}

      {presetType === 'custom' && (
        <Stack gap="xs">
          <Text size="xs" fw={500}>{t('fields.cronBuilder.cronExpression')}</Text>
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
            placeholder={t('fields.cronBuilder.cronExpressionPlaceholder')}
          />
          <Text size="xs" c="dimmed">
            {t('fields.cronBuilder.cronFormat')}
          </Text>
        </Stack>
      )}

      {/* Preview */}
      <Paper p="xs" withBorder bg="var(--mantine-color-gray-0)">
        <Stack gap={4}>
          <Group gap="xs">
            <Text size="xs" fw={600}>{t('fields.cronBuilder.generated')}</Text>
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
