import { useEffect, useState } from 'react';
import { Select } from '@mantine/core';
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
    <Select
      label={definition.displayName}
      description={definition.description ?? undefined}
      error={error}
      required={definition.required}
      value={String(value ?? '')}
      onChange={(v) => onChange(v ?? '')}
      placeholder="-- Select Credential --"
      data={credentials.map((c) => ({ label: `${c.name} (${c.type})`, value: c.id }))}
      searchable
      disabled={loading}
    />
  );
}
