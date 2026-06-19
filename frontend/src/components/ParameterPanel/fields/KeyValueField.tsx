import { useState, useEffect, useCallback, useMemo, useRef } from 'react';
import { TextInput, ActionIcon, Group, Text, Paper, Stack } from '@mantine/core';
import { Plus, Trash, AlertTriangle } from 'lucide-react';
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

  // 检测重复 Key
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
    updateEntries([...entriesRef.current, { key: '', value: '' }]);
  }, [updateEntries]);

  const handleRemoveEntry = useCallback((index: number) => {
    updateEntries(entriesRef.current.filter((_, i) => i !== index));
  }, [updateEntries]);

  return (
    <div>
      <Group justify="space-between" gap="xs" mb={4}>
        <Text size="sm" fw={500}>
          {definition.displayName}
          {definition.required && <span style={{ color: 'var(--mantine-color-error)' }}> *</span>}
        </Text>
        <ActionIcon variant="subtle" color="blue" onClick={handleAddEntry} title="Add entry" size="sm">
          <Plus size={16} />
        </ActionIcon>
      </Group>

      <Stack gap="xs">
        {duplicateKeys.size > 0 && (
          <div style={{ display: 'flex', alignItems: 'center', gap: '6px', padding: '6px 10px', backgroundColor: 'var(--mantine-color-yellow-0)', borderRadius: '4px', border: '1px solid var(--mantine-color-yellow-3)' }}>
            <AlertTriangle size={14} color="var(--mantine-color-yellow-7)" />
            <Text size="xs" c="yellow.9">Duplicate keys detected. Later values will override earlier ones.</Text>
          </div>
        )}
        {entries.map((entry, index) => (
          <Paper key={index} withBorder p="xs" radius="sm" style={duplicateKeys.has(index) ? { borderColor: 'var(--mantine-color-red-4)' } : undefined}>
            <Group gap="xs" align="flex-start">
              <TextInput
                placeholder="Key"
                value={entry.key}
                onChange={(e) => handleEntryChange(index, 'key', e.target.value)}
                size="xs"
                styles={{ root: { flex: 1 }, input: duplicateKeys.has(index) ? { borderColor: 'var(--mantine-color-red-5)' } : undefined }}
              />
              <TextInput
                placeholder="Value"
                value={entry.value}
                onChange={(e) => handleEntryChange(index, 'value', e.target.value)}
                size="xs"
                styles={{ root: { flex: 2 } }}
              />
              <ActionIcon
                variant="subtle"
                color="red"
                onClick={() => handleRemoveEntry(index)}
                title="Remove entry"
                size="sm"
              >
                <Trash size={14} />
              </ActionIcon>
            </Group>
          </Paper>
        ))}

        {entries.length === 0 && (
          <Text size="xs" c="dimmed" ta="center" py="sm">
            No entries. Click + to add.
          </Text>
        )}
      </Stack>

      {definition.description && !error && (
        <Text size="xs" c="dimmed" mt={4}>{definition.description}</Text>
      )}
      {error && (
        <Text size="xs" c="red" mt={4}>{error}</Text>
      )}
    </div>
  );
}
