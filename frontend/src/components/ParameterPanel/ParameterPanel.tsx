import { useCallback } from 'react';
import { Paper, Stack, TextInput, Text, Badge, Divider, Group, ScrollArea, Switch, Select } from '@mantine/core';
import { useWorkflowStore } from '../../stores/workflowStore.ts';
import { useDisplayRule } from '../../hooks/useDisplayRule.ts';
import { FieldResolver } from './FieldResolver.tsx';
import type { ParameterDefinition } from '../../types/workflow.ts';

/**
 * 将参数按类型分组：基础字段（String/Number/Options/Boolean）和高级字段（Json/Code/Expression 等）。
 */
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
  const validationErrors = useWorkflowStore((s) => s.validationErrors);
  const isActive = useWorkflowStore((s) => s.isActive);
  const setIsActive = useWorkflowStore((s) => s.setIsActive);
  const styleSettings = useWorkflowStore((s) => s.styleSettings);
  const setStyleSettings = useWorkflowStore((s) => s.setStyleSettings);
  const edgeCount = useWorkflowStore((s) => s.edges.length);

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
      <Stack gap="md" p="md" style={{ height: '100%', overflow: 'hidden' }}>
        <Paper withBorder p="sm" radius="md">
          <Stack gap="xs">
            <Text fw={600} size="md">Workflow Settings</Text>
            <Divider />
            <Switch
              label="Active"
              description="Enable this workflow to be triggered"
              checked={isActive}
              onChange={(e) => setIsActive(e.currentTarget.checked)}
              size="sm"
            />
            <Select
              label="Layout Direction"
              description="Node input/output port arrangement"
              value={layoutDirection}
              onChange={handleLayoutChange}
              data={[
                { label: 'Vertical (top to bottom)', value: 'vertical' },
                { label: 'Horizontal (left to right)', value: 'horizontal' },
              ]}
              size="sm"
            />
          </Stack>
        </Paper>
        <Paper withBorder p="sm" radius="md">
          <Group justify="space-between">
            <Text size="sm" c="dimmed">Nodes</Text>
            <Badge variant="light" size="sm">{nodes.length}</Badge>
          </Group>
          <Group justify="space-between" mt={4}>
            <Text size="sm" c="dimmed">Connections</Text>
            <Badge variant="light" size="sm">{edgeCount}</Badge>
          </Group>
        </Paper>
        <Text c="dimmed" size="xs" ta="center" mt="auto" pb="md">
          Select a node on the canvas to edit its parameters.
        </Text>
      </Stack>
    );
  }

  const { descriptor, parameters, name } = selectedNode.data;
  const nodeFieldErrors = (validationErrors[selectedNode.id] ?? {}) as Record<string, string>;
  const hasErrors = Object.keys(nodeFieldErrors).length > 0;

  const { basic, advanced } = groupParameters(descriptor.parameters);

  // 检查是否有可见参数
  const hasVisibleParams = [...basic, ...advanced].some((def) => !def.displayRule || isVisible(def));

  return (
    <Stack gap="md" p="md" style={{ height: '100%', overflow: 'hidden' }}>
      {/* 节点信息头部 */}
      <Paper withBorder p="sm" radius="md">
        <Stack gap="xs">
          <Group justify="space-between" align="center">
            <Text fw={600} size="md">{descriptor.displayName}</Text>
            <Badge variant="light" color="gray" size="sm">{descriptor.category}</Badge>
          </Group>
          <Text size="xs" c="dimmed">{descriptor.typeName}</Text>
          <Divider />
          <TextInput
            label="Node Name"
            value={name}
            onChange={(e) => updateNodeName(selectedNode.id, e.target.value)}
            size="sm"
          />
        </Stack>
      </Paper>

      {/* 校验错误提示 */}
      {hasErrors && (
        <Paper withBorder p="sm" radius="md" bg="var(--mantine-color-red-light)">
          <Text size="xs" c="red" fw={500}>
            Please fix {Object.keys(nodeFieldErrors).length} field error(s) before saving.
          </Text>
        </Paper>
      )}

      {/* 参数编辑区 */}
      <ScrollArea style={{ flex: 1 }} offsetScrollbars>
        <Stack gap="md">
          {/* 基础字段 */}
          {basic.length > 0 && (
            <Stack gap="sm">
              {basic.map((def) => {
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
            </Stack>
          )}

          {/* 分隔线：基础与高级字段之间 */}
          {basic.length > 0 && advanced.length > 0 && <Divider label="Advanced" labelPosition="center" />}

          {/* 高级字段 */}
          {advanced.length > 0 && (
            <Stack gap="sm">
              {advanced.map((def) => {
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
            </Stack>
          )}

          {!hasVisibleParams && (
            <Text size="sm" c="dimmed" ta="center" py="md">
              No configurable parameters.
            </Text>
          )}
        </Stack>
      </ScrollArea>
    </Stack>
  );
}
