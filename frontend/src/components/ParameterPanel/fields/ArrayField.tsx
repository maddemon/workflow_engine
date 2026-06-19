import { useState } from 'react';
import {
  TextInput, NumberInput, Select, Switch, Stack, Group,
  ActionIcon, Text, Button, UnstyledButton,
} from '@mantine/core';
import { Plus, Trash2, ChevronDown } from 'lucide-react';
import type { ParameterDefinition } from '../../../types/workflow.ts';

interface ArrayFieldProps {
  definition: ParameterDefinition;
  value: unknown;
  onChange: (value: unknown[]) => void;
  error?: string;
}

export function ArrayField({ definition, value, onChange, error }: ArrayFieldProps) {
  const items = Array.isArray(value) ? value : [];
  const itemDef = definition.itemDefinition;
  const fields = itemDef?.fields;
  const isStructured = Array.isArray(fields) && fields.length > 0;

  const handleAdd = () => {
    if (isStructured) {
      const emptyItem: Record<string, string> = {};
      for (const field of fields!) {
        emptyItem[field.name] = '';
      }
      onChange([...items, emptyItem]);
    } else {
      onChange([...items, '']);
    }
  };

  const handleRemove = (index: number) => {
    onChange(items.filter((_, i) => i !== index));
  };

  const handleChange = (index: number, newValue: unknown) => {
    const next = [...items];
    next[index] = newValue;
    onChange(next);
  };

  const handleFieldChange = (index: number, fieldName: string, fieldValue: unknown) => {
    const next = [...items];
    const current = (next[index] && typeof next[index] === 'object')
      ? { ...next[index] as Record<string, unknown> }
      : {};
    current[fieldName] = fieldValue;
    next[index] = current;
    onChange(next);
  };

  return (
    <div>
      <Group justify="space-between" mb={4}>
        <Text size="sm" fw={500}>
          {definition.displayName}
          {definition.required && <span style={{ color: 'var(--mantine-color-error)' }}> *</span>}
        </Text>
        <Button variant="subtle" size="xs" leftSection={<Plus size={14} />} onClick={handleAdd}>
          Add
        </Button>
      </Group>

      <Stack gap={4}>
        {items.length === 0 && (
          <Text size="xs" c="dimmed" ta="center" py="xs">
            No items. Click &quot;Add&quot; to create one.
          </Text>
        )}

        {isStructured
          ? items.map((item, index) => {
              const obj = item as Record<string, unknown>;
              const titleField = findTitleField(fields!);
              const titleValue = titleField ? String(obj[titleField.name] ?? '') : '';
              const headerLabel = titleValue || `${definition.displayName} ${index + 1}`;

              return (
                <StructuredItem
                  key={`item-${index}`}
                  label={headerLabel}
                  defaultExpanded
                  fields={fields!}
                  item={obj}
                  onFieldChange={(fieldName, fieldValue) => handleFieldChange(index, fieldName, fieldValue)}
                  onRemove={() => handleRemove(index)}
                />
              );
            })
          : items.map((item, index) => (
              <Group key={`item-${index}`} gap="xs" align="center" wrap="nowrap">
                <div style={{ flex: 1, minWidth: 0 }}>
                  {renderItem(itemDef, item, (v) => handleChange(index, v))}
                </div>
                <ActionIcon variant="subtle" color="red" onClick={() => handleRemove(index)} title="Remove">
                  <Trash2 size={14} />
                </ActionIcon>
              </Group>
            ))}
      </Stack>

      {definition.description && (
        <Text size="xs" c="dimmed" mt={4}>{definition.description}</Text>
      )}
      {error && (
        <Text size="xs" c="red" mt={4}>{error}</Text>
      )}
    </div>
  );
}

interface StructuredItemProps {
  label: string;
  defaultExpanded?: boolean;
  fields: ParameterDefinition[];
  item: Record<string, unknown>;
  onFieldChange: (fieldName: string, value: unknown) => void;
  onRemove: () => void;
}

function StructuredItem({ label, defaultExpanded, fields, item, onFieldChange, onRemove }: StructuredItemProps) {
  const [expanded, setExpanded] = useState(defaultExpanded ?? false);

  return (
    <div
      style={{
        border: '1px solid var(--mantine-color-default-border)',
        borderRadius: 'var(--mantine-radius-sm)',
      }}
    >
      <Group gap={0} wrap="nowrap" style={{ alignItems: 'center' }}>
        <UnstyledButton
          onClick={() => setExpanded((e) => !e)}
          style={{
            flex: 1,
            display: 'flex',
            alignItems: 'center',
            gap: 6,
            padding: '6px 8px',
            minHeight: 32,
          }}
        >
          <ChevronDown
            size={14}
            style={{
              transition: 'transform 150ms ease',
              transform: expanded ? 'rotate(0deg)' : 'rotate(-90deg)',
              flexShrink: 0,
              color: 'var(--mantine-color-dimmed)',
            }}
          />
          <Text size="xs" fw={500} style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
            {label}
          </Text>
        </UnstyledButton>
        <ActionIcon
          variant="subtle"
          color="red"
          size="sm"
          onClick={(e: React.MouseEvent) => { e.stopPropagation(); onRemove(); }}
          title="Remove"
          style={{ marginRight: 4, flexShrink: 0 }}
        >
          <Trash2 size={13} />
        </ActionIcon>
      </Group>

      {expanded && (
        <Stack gap={6} p="xs" style={{ borderTop: '1px solid var(--mantine-color-default-border)' }}>
          {fields.map((field) => {
            const fieldValue = item[field.name] ?? '';
            return (
              <div key={field.name}>
                <Text size="xs" c="dimmed" mb={2}>{field.displayName}</Text>
                {renderItem(field, fieldValue, (v) => onFieldChange(field.name, v))}
              </div>
            );
          })}
        </Stack>
      )}
    </div>
  );
}

function findTitleField(fields: ParameterDefinition[]): ParameterDefinition | undefined {
  return fields.find((f) => f.name === 'name')
    ?? fields.find((f) => f.name === 'label')
    ?? fields.find((f) => f.name === 'key')
    ?? fields[0];
}

function renderItem(
  itemDef: ParameterDefinition | null | undefined,
  value: unknown,
  onChange: (value: unknown) => void,
) {
  if (!itemDef) {
    return (
      <TextInput
        value={String(value ?? '')}
        onChange={(e) => onChange(e.target.value)}
        size="xs"
      />
    );
  }

  switch (itemDef.type) {
    case 'Number':
      return (
        <NumberInput
          value={typeof value === 'number' ? value : ''}
          onChange={(v) => onChange(typeof v === 'number' ? v : '')}
          size="xs"
        />
      );
    case 'Boolean':
      return (
        <Switch
          checked={!!value}
          onChange={(e) => onChange(e.currentTarget.checked)}
          size="xs"
        />
      );
    case 'Options':
      return (
        <Select
          value={String(value ?? '')}
          onChange={(v) => onChange(v ?? '')}
          data={(itemDef.options ?? []).map((opt) => ({ label: opt.label, value: String(opt.value) }))}
          size="xs"
        />
      );
    case 'String':
    default:
      return (
        <TextInput
          value={String(value ?? '')}
          onChange={(e) => onChange(e.target.value)}
          size="xs"
        />
      );
  }
}
