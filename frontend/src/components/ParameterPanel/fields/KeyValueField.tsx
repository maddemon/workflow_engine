import { useState, useEffect, useCallback, useMemo, useRef } from 'react';
import { TextInput, ActionIcon, Group, Text, Stack } from '@mantine/core';
import { Plus, Trash, AlertTriangle } from 'lucide-react';
import { InfoTooltip } from './InfoTooltip.tsx';
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
    if (entry.key.trim() !== '') {
      obj[entry.key] = entry.value;
    }
  }
  return JSON.stringify(obj, null, 2);
}

export function KeyValueField({ definition, value, onChange, error }: KeyValueFieldProps) {
  const [entries, setEntries] = useState<KeyValueEntry[]>(() => parseJsonToEntries(String(value ?? '')));
  const entriesRef = useRef(entries);
  entriesRef.current = entries;

  useEffect(() => {
    setEntries(parseJsonToEntries(String(value ?? '')));
  }, [value]);

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

  const updateEntries = useCallback((updated: KeyValueEntry[]) => {
    setEntries(updated);
    onChange(entriesToJson(updated));
  }, [onChange]);

  const handleEntryChange = useCallback((index: number, field: 'key' | 'value', newValue: string) => {
    const updated = entriesRef.current.map((entry, i) =>
      i === index ? { ...entry, [field]: newValue } : entry,
    );
    updateEntries(updated);
  }, [updateEntries]);

  const handleAddEntry = useCallback(() => {
    setEntries((prev) => {
      const next = [...prev, { key: '', value: '' }];
      onChange(entriesToJson(next));
      return next;
    });
  }, [onChange]);

  const handleRemoveEntry = useCallback((index: number) => {
    setEntries((prev) => {
      const next = prev.filter((_, i) => i !== index);
      onChange(entriesToJson(next));
      return next;
    });
  }, [onChange]);

  return (
    <div>
      <Group justify="space-between" gap="xs" mb={4}>
        <Group gap={4}>
          <Text size="xs" fw={400}>
            {definition.displayName}
            {definition.required && <span style={{ color: 'var(--mantine-color-error)' }}> *</span>}
          </Text>
          {definition.description && <InfoTooltip label={definition.description} />}
        </Group>
        <ActionIcon variant="subtle" color="blue" onClick={handleAddEntry} title="Add entry" size="sm">
          <Plus size={14} />
        </ActionIcon>
      </Group>

      <Stack gap={4}>
        {duplicateKeys.size > 0 && (
          <Group gap="xs" p="xs" style={{ backgroundColor: 'var(--mantine-color-yellow-0)', borderRadius: 4 }}>
            <AlertTriangle size={12} color="var(--mantine-color-yellow-7)" />
            <Text size="xs" c="yellow.9">Duplicate keys detected.</Text>
          </Group>
        )}
        {entries.map((entry, index) => (
          <Group key={index} gap="xs" align="center">
            <TextInput
              placeholder="Key"
              value={entry.key}
              onChange={(e) => handleEntryChange(index, 'key', e.target.value)}
              size="xs"
              style={{ flex: 1 }}
              error={duplicateKeys.has(index)}
            />
            <TextInput
              placeholder="Value"
              value={entry.value}
              onChange={(e) => handleEntryChange(index, 'value', e.target.value)}
              size="xs"
              style={{ flex: 2 }}
            />
            <ActionIcon
              variant="subtle"
              color="red"
              onClick={() => handleRemoveEntry(index)}
              title="Remove entry"
              size="sm"
            >
              <Trash size={12} />
            </ActionIcon>
          </Group>
        ))}

        {entries.length === 0 && (
          <Text size="xs" c="dimmed" ta="center" py="sm">
            No entries. Click + to add.
          </Text>
        )}
      </Stack>

      {error && (
        <Text size="xs" c="red" mt={4}>{error}</Text>
      )}
    </div>
  );
}
