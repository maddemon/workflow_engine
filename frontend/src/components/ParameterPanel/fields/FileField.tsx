import { useState } from 'react';
import { FileInput, Group, Text } from '@mantine/core';
import { InfoTooltip } from './InfoTooltip.tsx';
import type { ParameterDefinition } from '../../../types/workflow.ts';

interface FileFieldProps {
  definition: ParameterDefinition;
  value: unknown;
  onChange: (value: string) => void;
  error?: string;
}

export function FileField({ definition, value, onChange, error }: FileFieldProps) {
  const [file, setFile] = useState<File | null>(null);

  return (
    <div>
      <Group gap={4} mb={4}>
        <Text size="xs" fw={400}>
          {definition.displayName}
          {definition.required && <span style={{ color: 'var(--mantine-color-error)' }}> *</span>}
        </Text>
        {definition.description && <InfoTooltip label={definition.description} />}
      </Group>
      <FileInput
        error={error}
        value={file}
        onChange={(f) => {
          setFile(f);
          onChange(f?.name ?? '');
        }}
        placeholder={typeof value === 'string' && value ? value : 'Select file'}
        clearable
      />
    </div>
  );
}
