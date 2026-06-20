import { useEffect, useState } from 'react';
import { Select, Group, Text } from '@mantine/core';
import { InfoTooltip } from './InfoTooltip.tsx';
import { getCredentials } from '../../../services/api.ts';
import type { CredentialDto, ParameterDefinition } from '../../../types/workflow.ts';

interface CredentialFieldProps {
  definition: ParameterDefinition;
  value: unknown;
  onChange: (value: string) => void;
  error?: string;
}

export function CredentialField({ definition, value, onChange, error }: CredentialFieldProps) {
  const [credentials, setCredentials] = useState<CredentialDto[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let cancelled = false;
    getCredentials()
      .then((data) => {
        if (!cancelled) {
          const filtered = definition.credentialType
            ? data.filter((c) => c.type === definition.credentialType)
            : data;
          setCredentials(filtered);
        }
      })
      .catch(() => {
        if (!cancelled) setCredentials([]);
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [definition.credentialType]);

  return (
    <div>
      <Group gap={4} mb={4}>
        <Text size="xs" fw={400}>
          {definition.displayName}
          {definition.required && <span style={{ color: 'var(--mantine-color-error)' }}> *</span>}
        </Text>
        {definition.description && <InfoTooltip label={definition.description} />}
      </Group>
      <Select
        error={error}
        value={String(value ?? '')}
        onChange={(v) => onChange(v ?? '')}
        placeholder="-- Select Credential --"
        data={credentials.map((c) => ({ label: `${c.name} (${c.type})`, value: c.id }))}
        searchable
        disabled={loading}
      />
    </div>
  );
}
