import { useState, useCallback, useMemo, useRef, useEffect } from 'react';
import { TextInput, ActionIcon, Group, Text, Stack } from '@mantine/core';
import { useTranslation } from 'react-i18next';
import { Plus, Trash, AlertTriangle } from 'lucide-react';
import { InfoTooltip } from './InfoTooltip.tsx';
import { useParameterName } from '../useParameterName.ts';
import type { ParameterDefinition } from '../../../types/workflow.ts';

interface KeyValueFieldProps {
  definition: ParameterDefinition;
  value: unknown;
  onChange: (value: string) => void;
  error?: string;
}

interface KeyValueEntry {
  key: string;
  value: string;
}

function parseJsonToEntries(jsonStr: string): KeyValueEntry[] {
  if (!jsonStr || jsonStr.trim() === '') return [];
  try {
    const obj = JSON.parse(jsonStr);
    if (typeof obj !== 'object' || obj === null || Array.isArray(obj)) return [];
    return Object.entries(obj).map(([key, value]) => ({
      key,
      value: typeof value === 'string' ? value : JSON.stringify(value),
    }));
  } catch {
    return [];
  }
}

function entriesToJson(entries: KeyValueEntry[]): string {
  const obj: Record<string, string> = {};
  for (const entry of entries) {
    obj[entry.key] = entry.value;
  }
  return JSON.stringify(obj, null, 2);
}

export function KeyValueField({ definition, value, onChange, error }: KeyValueFieldProps) {
  const { t } = useTranslation('parameterPanel');
  const paramName = useParameterName();
  const label = paramName(definition.name, definition.displayName);
  const valueStr = String(value ?? '');
  const lastEmittedRef = useRef(valueStr);
  const [entries, setEntries] = useState<KeyValueEntry[]>(() => parseJsonToEntries(valueStr));

  // 仅当 value 与上次发射值不同时（外部变更），才从 value 重新解析
  useEffect(() => {
    if (valueStr !== lastEmittedRef.current) {
      const parsed = parseJsonToEntries(valueStr);
      setEntries(parsed);
      lastEmittedRef.current = valueStr;
    }
  }, [valueStr]);

  const duplicateKeys = useMemo(() => {
    const seen = new Map<string, number>();
    const duplicates = new Set<number>();
    entries.forEach((entry, i) => {
      const k = entry.key.trim();
      if (k === '') return;
      if (seen.has(k)) {
        duplicates.add(seen.get(k)!);
        duplicates.add(i);
      } else {
        seen.set(k, i);
      }
    });
    return duplicates;
  }, [entries]);

  const handleAddEntry = useCallback(() => {
    const next = [...entries, { key: '', value: '' }];
    const json = entriesToJson(next);
    lastEmittedRef.current = json;
    onChange(json);
    setEntries(next);
  }, [entries, onChange]);

  const handleRemoveEntry = useCallback((index: number) => {
    const next = entries.filter((_, i) => i !== index);
    const json = entriesToJson(next);
    lastEmittedRef.current = json;
    onChange(json);
    setEntries(next);
  }, [entries, onChange]);

  const handleEntryChange = useCallback((index: number, field: 'key' | 'value', newValue: string) => {
    const updated = entries.map((entry, i) =>
      i === index ? { ...entry, [field]: newValue } : entry,
    );
    const json = entriesToJson(updated);
    lastEmittedRef.current = json;
    setEntries(updated);
    onChange(json);
  }, [entries, onChange]);

  return (
    <div>
      <Group justify="space-between" gap="xs" mb={4}>
        <Group gap={4}>
          <Text size="xs" fw={400}>
            {label}
            {definition.required && <span style={{ color: 'var(--mantine-color-error)' }}> *</span>}
          </Text>
          {definition.description && <InfoTooltip label={definition.description} />}
        </Group>
        <ActionIcon variant="subtle" color="blue" onClick={handleAddEntry} title={t('fields.keyValue.addEntry')} size="sm">
          <Plus size={14} />
        </ActionIcon>
      </Group>

      <Stack gap={4}>
        {duplicateKeys.size > 0 && (
          <Group gap="xs" p="xs" style={{ backgroundColor: 'var(--mantine-color-yellow-0)', borderRadius: 4 }}>
            <AlertTriangle size={12} color="var(--mantine-color-yellow-7)" />
            <Text size="xs" c="yellow.9">{t('fields.keyValue.duplicateKeys')}</Text>
          </Group>
        )}
        {entries.map((entry, index) => (
          <Group key={index} gap="xs" align="center">
            <TextInput
              placeholder={t('fields.keyValue.keyPlaceholder')}
              value={entry.key}
              onChange={(e) => handleEntryChange(index, 'key', e.target.value)}
              size="xs"
              style={{ flex: 1 }}
              error={duplicateKeys.has(index)}
            />
            <TextInput
              placeholder={t('fields.keyValue.valuePlaceholder')}
              value={entry.value}
              onChange={(e) => handleEntryChange(index, 'value', e.target.value)}
              size="xs"
              style={{ flex: 2 }}
            />
            <ActionIcon
              variant="subtle"
              color="red"
              onClick={() => handleRemoveEntry(index)}
              title={t('fields.keyValue.removeEntry')}
              size="sm"
            >
              <Trash size={12} />
            </ActionIcon>
          </Group>
        ))}

        {entries.length === 0 && (
          <Text size="xs" c="dimmed" ta="center" py="sm">
            {t('fields.keyValue.noEntries')}
          </Text>
        )}
      </Stack>

      {error && (
        <Text size="xs" c="red" mt={4}>{error}</Text>
      )}
    </div>
  );
}
