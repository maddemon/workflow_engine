import { useCallback, useState } from 'react';
import { Stack, TextInput, Text, Badge, Group, ScrollArea, Switch, Select, Collapse, UnstyledButton } from '@mantine/core';
import { ChevronRight, ChevronDown } from 'lucide-react';
import { useWorkflowStore } from '../../stores/workflowStore.ts';
import { useDisplayRule } from '../../hooks/useDisplayRule.ts';
import { FieldResolver } from './FieldResolver.tsx';
import { InfoTooltip } from './fields/InfoTooltip.tsx';
import type { ParameterDefinition } from '../../types/workflow.ts';

function groupParameters(
  parameters: ParameterDefinition[],
): { basic: ParameterDefinition[]; advanced: ParameterDefinition[] } {
  const basic: ParameterDefinition[] = [];
  const advanced: ParameterDefinition[] = [];
  const advancedTypes = new Set(['Json', 'Code', 'Expression', 'Array', 'File', 'Credential', 'Resource']);
  const advancedHints = new Set(['CodeEditor', 'JsonEditor', 'KeyValueEditor', 'Expression', 'Array', 'FileUpload', 'CredentialSelect', 'ResourceSelect']);

  for (const p of parameters) {
    if (advancedTypes.has(p.type) || (p.hint && advancedHints.has(p.hint))) {
      advanced.push(p);
    } else {
      basic.push(p);
    }
  }
  return { basic, advanced };
}

export function ParameterPanel() {
  const selectedNodeId = useWorkflowStore((s) => s.selectedNodeId);
  const nodes = useWorkflowStore((s) => s.nodes);
  const updateNodeParameters = useWorkflowStore((s) => s.updateNodeParameters);
  const updateNodeName = useWorkflowStore((s) => s.updateNodeName);
  const updateNodeSettings = useWorkflowStore((s) => s.updateNodeSettings);
  const validationErrors = useWorkflowStore((s) => s.validationErrors);
  const isActive = useWorkflowStore((s) => s.isActive);
  const setIsActive = useWorkflowStore((s) => s.setIsActive);
  const styleSettings = useWorkflowStore((s) => s.styleSettings);
  const setStyleSettings = useWorkflowStore((s) => s.setStyleSettings);
  const edgeCount = useWorkflowStore((s) => s.edges.length);
  const workflowName = useWorkflowStore((s) => s.workflowName);
  const setWorkflowName = useWorkflowStore((s) => s.setWorkflowName);
  const isDirty = useWorkflowStore((s) => s.isDirty);
  const [settingsOpen, setSettingsOpen] = useState(false);

  const selectedNode = nodes.find((n) => n.id === selectedNodeId);
  const { isVisible } = useDisplayRule(selectedNode?.data.parameters ?? {});

  const layoutDirection = styleSettings.layoutDirection;

  const handleLayoutChange = (value: string | null) => {
    setStyleSettings({ ...styleSettings, layoutDirection: (value as 'vertical' | 'horizontal') ?? 'vertical' });
  };

  const handleParameterChange = useCallback(
    (name: string, value: unknown) => {
      if (!selectedNodeId) return;
      const node = nodes.find((n) => n.id === selectedNodeId);
      if (!node) return;
      updateNodeParameters(selectedNodeId, { ...node.data.parameters, [name]: value });
    },
    [selectedNodeId, nodes, updateNodeParameters],
  );

  if (!selectedNode) {
    return (
      <Stack gap="sm" p="sm" style={{ height: '100%', overflow: 'hidden' }}>
        <Text fw={600} size="xs" tt="uppercase" c="dimmed" style={{ letterSpacing: '0.05em' }}>
          Workflow Settings
        </Text>
        <Stack gap="xs">
          <TextInput
            label="Workflow Name"
            value={workflowName}
            onChange={(e) => setWorkflowName(e.target.value)}
            placeholder="Enter workflow name..."
            rightSection={isDirty ? <Text c="orange" fw={700} size="xs">*</Text> : undefined}
          />
          <Group
            justify="space-between"
            align="center"
            onClick={() => setIsActive(!isActive)}
            style={{ cursor: 'pointer' }}
            p={4}
          >
            <Switch checked={isActive} onChange={(e) => setIsActive(e.currentTarget.checked)} size="sm" onClick={(e) => e.stopPropagation()} />
            <Group gap={4} style={{ flex: 1 }}>
              <Text size="xs" fw={400}>Active</Text>
              <InfoTooltip label="Enable this workflow to be triggered" />
            </Group>
          </Group>
          <Select
            label="Layout Direction"
            value={layoutDirection}
            onChange={handleLayoutChange}
            data={[
              { label: 'Vertical (top to bottom)', value: 'vertical' },
              { label: 'Horizontal (left to right)', value: 'horizontal' },
            ]}
          />
        </Stack>
        <Group justify="space-between">
          <Text size="xs" c="dimmed">Nodes</Text>
          <Badge variant="light" size="xs">{nodes.length}</Badge>
        </Group>
        <Group justify="space-between">
          <Text size="xs" c="dimmed">Connections</Text>
          <Badge variant="light" size="xs">{edgeCount}</Badge>
        </Group>
        <Text c="dimmed" size="xs" ta="center" mt="auto" pb="sm">
          Select a node on the canvas to edit its parameters.
        </Text>
      </Stack>
    );
  }

  const { descriptor, parameters, name } = selectedNode.data;
  const nodeFieldErrors = (validationErrors[selectedNode.id] ?? {}) as Record<string, string>;
  const hasErrors = Object.keys(nodeFieldErrors).length > 0;

  const { basic, advanced } = groupParameters(descriptor.parameters);

  const hasVisibleParams = [...basic, ...advanced].some((def) => !def.displayRule || isVisible(def));

  return (
    <Stack gap="xs" p="sm" style={{ height: '100%', overflow: 'hidden' }}>
      {/* 节点头部 */}
      <Group justify="space-between" align="center">
        <Text fw={600} size="sm">{descriptor.displayName}</Text>
        <Badge variant="light" color="gray" size="xs">{descriptor.category}</Badge>
      </Group>
      <Text size="xs" c="dimmed" ff="monospace">{descriptor.typeName}</Text>
      <TextInput
        label="Node Name"
        value={name}
        onChange={(e) => updateNodeName(selectedNode.id, e.target.value)}
      />

      {hasErrors && (
        <Text size="xs" c="red" fw={500} p="xs" style={{ background: 'var(--mantine-color-red-light)', borderRadius: 4 }}>
          Fix {Object.keys(nodeFieldErrors).length} error(s) before saving.
        </Text>
      )}

      {/* 参数列表 */}
      <ScrollArea style={{ flex: 1 }} offsetScrollbars>
        <Stack gap="sm">
          {basic.length > 0 && basic.map((def) => {
            if (def.displayRule && !isVisible(def)) return null;
            return (
              <FieldResolver
                key={def.name}
                definition={def}
                value={parameters[def.name]}
                onChange={(v) => handleParameterChange(def.name, v)}
                error={nodeFieldErrors[def.name]}
              />
            );
          })}

          {advanced.length > 0 && advanced.map((def) => {
            if (def.displayRule && !isVisible(def)) return null;
            return (
              <FieldResolver
                key={def.name}
                definition={def}
                value={parameters[def.name]}
                onChange={(v) => handleParameterChange(def.name, v)}
                error={nodeFieldErrors[def.name]}
              />
            );
          })}

          {!hasVisibleParams && (
            <Text size="xs" c="dimmed" ta="center" py="md">
              No configurable parameters.
            </Text>
          )}

          {/* Settings — 折叠在底部 */}
          <UnstyledButton w="100%" onClick={() => setSettingsOpen(!settingsOpen)} py={4}>
            <Group justify="space-between" wrap="nowrap">
              <Text size="xs" fw={600}>Settings</Text>
              {settingsOpen ? <ChevronDown size={12} color="var(--mantine-color-dimmed)" /> : <ChevronRight size={12} color="var(--mantine-color-dimmed)" />}
            </Group>
          </UnstyledButton>
          <Collapse expanded={settingsOpen}>
            <Stack gap="sm" pb="sm">
              <Select
                label="On Error"
                value={selectedNode.data.errorStrategy}
                onChange={(v) => updateNodeSettings(selectedNode.id, { errorStrategy: v ?? 'Terminate' })}
                data={[
                  { label: 'Stop Workflow', value: 'Terminate' },
                  { label: 'Continue (regular output)', value: 'Continue' },
                ]}
              />
              <Group
                justify="space-between"
                align="center"
                onClick={() => {
                  if (selectedNode.data.retryPolicy !== null) {
                    updateNodeSettings(selectedNode.id, { retryPolicy: null });
                  } else {
                    updateNodeSettings(selectedNode.id, { retryPolicy: JSON.stringify({ maxRetries: 2, baseDelayMs: 1000 }) });
                  }
                }}
                style={{ cursor: 'pointer' }}
                p={4}
              >
                <Switch
                  checked={selectedNode.data.retryPolicy !== null}
                  onChange={(e) => {
                    if (e.currentTarget.checked) {
                      updateNodeSettings(selectedNode.id, { retryPolicy: JSON.stringify({ maxRetries: 2, baseDelayMs: 1000 }) });
                    } else {
                      updateNodeSettings(selectedNode.id, { retryPolicy: null });
                    }
                  }}
                  size="sm"
                  onClick={(e) => e.stopPropagation()}
                />
                <Group gap={4} style={{ flex: 1 }}>
                  <Text size="xs" fw={400}>Retry on Fail</Text>
                  <InfoTooltip label="Retry this node when it fails" />
                </Group>
              </Group>

              {selectedNode.data.retryPolicy !== null && (() => {
                const policy = (() => {
                  try { return JSON.parse(selectedNode.data.retryPolicy!) as { maxRetries: number; baseDelayMs: number }; }
                  catch { return { maxRetries: 2, baseDelayMs: 1000 }; }
                })();
                return (
                  <Stack gap="sm" ml="md">
                    <Select
                      label="Max Retries"
                      value={String(policy.maxRetries)}
                      onChange={(v) => updateNodeSettings(selectedNode.id, { retryPolicy: JSON.stringify({ ...policy, maxRetries: Number(v) ?? 2 }) })}
                      data={[
                        { label: '2', value: '2' },
                        { label: '3', value: '3' },
                        { label: '5', value: '5' },
                        { label: '10', value: '10' },
                      ]}
                    />
                    <Select
                      label="Delay Between Retries (ms)"
                      value={String(policy.baseDelayMs)}
                      onChange={(v) => updateNodeSettings(selectedNode.id, { retryPolicy: JSON.stringify({ ...policy, baseDelayMs: Number(v) ?? 1000 }) })}
                      data={[
                        { label: '500', value: '500' },
                        { label: '1000', value: '1000' },
                        { label: '2000', value: '2000' },
                        { label: '5000', value: '5000' },
                      ]}
                    />
                  </Stack>
                );
              })()}
            </Stack>
          </Collapse>
        </Stack>
      </ScrollArea>
    </Stack>
  );
}
