import { useState } from 'react';
import { FileInput } from '@mantine/core';
import type { ParameterDefinition } from '../../../types/workflow.ts';

interface FileFieldProps {
  definition: ParameterDefinition;
  value: unknown;
  onChange: (value: string) => void;
  error?: string;
}

/**
 * 文件上传，上传后前端暂存文件名，提交时传给后端。
 * 本次先做 UI 和值捕获，后端文件处理后续扩展。
 */
export function FileField({ definition, value, onChange, error }: FileFieldProps) {
  const [file, setFile] = useState<File | null>(null);

  return (
    <FileInput
      label={definition.displayName}
      description={definition.description ?? undefined}
      error={error}
      required={definition.required}
      value={file}
      onChange={(f) => {
        setFile(f);
        onChange(f?.name ?? '');
      }}
      placeholder={typeof value === 'string' && value ? value : 'Select file'}
      clearable
    />
  );
}
