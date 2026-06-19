import { useState, useEffect } from 'react';
import { Textarea, ActionIcon, Group, Text } from '@mantine/core';
import { Braces } from 'lucide-react';
import type { ParameterDefinition } from '../../../types/workflow.ts';

interface JsonFieldProps {
  definition: ParameterDefinition;
  value: unknown;
  onChange: (value: string) => void;
  error?: string;
}

export function JsonField({ definition, value, onChange, error }: JsonFieldProps) {
  const displayValue = typeof value === 'string' ? value : value === null || value === undefined ? '' : JSON.stringify(value, null, 2);
  const [parseError, setParseError] = useState<string | null>(null);

  // 实时校验 JSON 格式
  useEffect(() => {
    if (displayValue.trim() === '') {
      setParseError(null);
      return;
    }
    try {
      JSON.parse(displayValue);
      setParseError(null);
    } catch {
      setParseError('Invalid JSON');
    }
  }, [displayValue]);

  const handleFormat = () => {
    if (displayValue.trim() === '') return;
    try {
      const parsed = JSON.parse(displayValue);
      onChange(JSON.stringify(parsed, null, 2));
      setParseError(null);
    } catch {
      setParseError('Invalid JSON');
    }
  };

  const combinedError = error ?? parseError ?? undefined;

  return (
    <div>
      <Group justify="space-between" gap="xs" mb={4}>
        <Text size="sm" fw={500}>
          {definition.displayName}
          {definition.required && <span style={{ color: 'var(--mantine-color-error)' }}> *</span>}
        </Text>
        <ActionIcon variant="subtle" onClick={handleFormat} title="Format JSON" size="sm">
          <Braces size={16} />
        </ActionIcon>
      </Group>
      <Textarea
        value={displayValue}
        onChange={(e) => onChange(e.target.value)}
        autosize
        minRows={4}
        maxRows={12}
        spellCheck={false}
        styles={{ input: { fontFamily: 'var(--mantine-font-family-monospace)', fontSize: 12 } }}
        error={combinedError}
      />
      {definition.description && !combinedError && (
        <Text size="xs" c="dimmed" mt={4}>{definition.description}</Text>
      )}
    </div>
  );
}
