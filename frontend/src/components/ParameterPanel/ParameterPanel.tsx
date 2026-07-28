import { useCallback, useState } from 'react';
import { Stack, TextInput, Text, Badge, Group, ScrollArea, Switch, Select, Collapse, UnstyledButton, Divider, NumberInput, Box } from '@mantine/core';
import { ChevronRight, ChevronDown } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { useShallow } from 'zustand/shallow';
import { useWorkflowStore } from '../../stores/workflowStore.ts';
import { useCanvasStore } from '../Canvas/stores/canvasStore.ts';
import { useDisplayRule } from '../../hooks/useDisplayRule.ts';
import { useIsDirty } from '../../hooks/useIsDirty.ts';
import { normalizeLayoutDirection } from '../../utils/workflowLayout.ts';
import { FieldResolver } from './FieldResolver.tsx';
import { TriggerConfig } from './TriggerConfig.tsx';
import { InfoTooltip } from './fields/InfoTooltip.tsx';
import { toRetryPolicyDto, fromRetryPolicyDto, DEFAULT_RETRY_POLICY_UI } from '../../utils/retryPolicy.ts';
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
  const { t } = useTranslation(['parameterPanel', 'common']);
  const selectedNode = useCanvasStore(
    useShallow((s) => {
      if (!s.selectedNodeId) return null;
      const node = s.nodes.find((n) => n.id === s.selectedNodeId);
      if (!node) return null;
      return { id: node.id, data: node.data };
    }),
  );
  const isExecuting = useCanvasStore((s) => s.isExecuting);
  const reviewMode = useCanvasStore((s) => s.reviewMode);
  const updateNodeName = useCanvasStore((s) => s.updateNodeName);
  const updateNodeSettings = useCanvasStore((s) => s.updateNodeSettings);
  const validationErrors = useCanvasStore((s) => s.validationErrors);
  const isActive = useWorkflowStore((s) => s.isActive);
  const setIsActive = useWorkflowStore((s) => s.setIsActive);
  const styleSettings = useCanvasStore((s) => s.styleSettings);
  const setStyleSettings = useCanvasStore((s) => s.setStyleSettings);
  const edgeCount = useCanvasStore((s) => s.edges.length);
  const nodeCount = useCanvasStore((s) => s.nodes.length);
  const workflowName = useWorkflowStore((s) => s.workflowName);
  const setWorkflowName = useWorkflowStore((s) => s.setWorkflowName);
  const isDirty = useIsDirty();
  const workflowId = useWorkflowStore((s) => s.workflowId ?? '');
  const projectId = useWorkflowStore((s) => s.projectId);
  const [settingsOpen, setSettingsOpen] = useState(false);

  const { isVisible } = useDisplayRule(selectedNode?.data.parameters ?? {});

  const layoutDirection = styleSettings.layoutDirection;

  const handleLayoutChange = (value: string | null) => {
    setStyleSettings({ ...styleSettings, layoutDirection: normalizeLayoutDirection(value) });
  };

  // P3 #26：从 store 读取最新状态，使回调引用稳定，避免 selectedNode 每次渲染变化导致的非必要重渲染。
  const handleParameterChange = useCallback(
    (name: string, value: unknown) => {
      const { selectedNodeId, nodes, updateNodeParameters } = useCanvasStore.getState();
      if (!selectedNodeId) return;
      const node = nodes.find((n) => n.id === selectedNodeId);
      if (!node) return;
      updateNodeParameters(selectedNodeId, { ...node.data.parameters, [name]: value });
    },
    [],
  );

  if (!selectedNode) {
    return (
      <Stack gap="sm" p="sm" style={{ height: '100%', overflow: 'hidden' }}>
        {isExecuting && (
          <Text size="xs" c="blue" fw={500} p={4} style={{ background: 'var(--mantine-color-blue-light)', borderRadius: 4, textAlign: 'center' }}>
            {t('executionBanner.inProgress')}
          </Text>
        )}
        {reviewMode && (
          <Text size="xs" c="blue" fw={500} p={4} style={{ background: 'var(--mantine-color-blue-light)', borderRadius: 4, textAlign: 'center' }}>
            {t('executionBanner.reviewMode')}
          </Text>
        )}
        <Text fw={600} size="xs" tt="uppercase" c="dimmed" style={{ letterSpacing: '0.05em' }}>
          {t('workflowSettings.title')}
        </Text>
        <Stack gap="xs">
          <TextInput
            label={t('workflowSettings.name')}
            value={workflowName}
            onChange={(e) => setWorkflowName(e.target.value)}
            placeholder={t('workflowSettings.namePlaceholder')}
            disabled={isExecuting || reviewMode}
            rightSection={isDirty ? <Text c="orange" fw={700} size="xs">*</Text> : undefined}
          />
          <Group
            justify="space-between"
            align="center"
            onClick={() => !isExecuting && !reviewMode && setIsActive(!isActive)}
            style={{ cursor: (isExecuting || reviewMode) ? 'not-allowed' : 'pointer' }}
            p={4}
          >
            <Switch checked={isActive} onChange={(e) => setIsActive(e.currentTarget.checked)} size="sm" disabled={isExecuting || reviewMode} onClick={(e) => e.stopPropagation()} />
            <Group gap={4} style={{ flex: 1 }}>
              <Text size="xs" fw={400}>{t('workflowSettings.active')}</Text>
              <InfoTooltip label={t('workflowSettings.activeTooltip')} />
            </Group>
          </Group>
          <Select
            label={t('workflowSettings.layoutDirection')}
            value={layoutDirection}
            onChange={handleLayoutChange}
            disabled={isExecuting || reviewMode}
            data={[
              { label: t('workflowSettings.vertical'), value: 'vertical' },
              { label: t('workflowSettings.horizontal'), value: 'horizontal' },
            ]}
          />
          <Divider />
          <TriggerConfig workflowId={workflowId} isExecuting={isExecuting} reviewMode={reviewMode} />
        </Stack>
        <Group justify="space-between">
          <Text size="xs" c="dimmed">{t('workflowSettings.nodes')}</Text>
          <Badge variant="light" size="xs">{nodeCount}</Badge>
        </Group>
        <Group justify="space-between">
          <Text size="xs" c="dimmed">{t('workflowSettings.connections')}</Text>
          <Badge variant="light" size="xs">{edgeCount}</Badge>
        </Group>
        <Text c="dimmed" size="xs" ta="center" mt="auto" pb="sm">
          {reviewMode ? t('workflowSettings.selectNodeReviewHint') : t('workflowSettings.selectNodeHint')}
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
        label={t('nodeSettings.name')}
        value={name}
        onChange={(e) => updateNodeName(selectedNode.id, e.target.value)}
        disabled={isExecuting || reviewMode}
      />

      {hasErrors && (
        <Text size="xs" c="red" fw={500} p="xs" style={{ background: 'var(--mantine-color-red-light)', borderRadius: 4 }}>
          {t('nodeSettings.fixErrors', { count: Object.keys(nodeFieldErrors).length })}
        </Text>
      )}

      {/* 参数列表 */}
      <ScrollArea style={{ flex: 1, position: 'relative' }} offsetScrollbars>
        {isExecuting && (
          <Box
            style={{
              position: 'absolute', inset: 0, zIndex: 10,
              background: 'var(--mantine-color-body)',
              opacity: 0.5, pointerEvents: 'none',
              borderRadius: 4,
            }}
          />
        )}
        <Stack gap="sm" style={(isExecuting || reviewMode) ? { pointerEvents: 'none', opacity: isExecuting ? 0.6 : undefined } : undefined}>
          {basic.length > 0 && basic.map((def) => {
            if (def.displayRule && !isVisible(def)) return null;
            return (
              <FieldResolver
                key={def.name}
                definition={def}
                value={parameters[def.name]}
                onChange={(v) => handleParameterChange(def.name, v)}
                error={nodeFieldErrors[def.name]}
                projectId={projectId}
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
                projectId={projectId}
              />
            );
          })}

          {!hasVisibleParams && (
            <Text size="xs" c="dimmed" ta="center" py="md">
              {t('nodeSettings.noParameters')}
            </Text>
          )}

          {/* Settings — 折叠在底部 */}
          <UnstyledButton w="100%" onClick={() => setSettingsOpen(!settingsOpen)} py={4}>
            <Group justify="space-between" wrap="nowrap">
              <Text size="xs" fw={600}>{t('nodeSettings.settingsTitle')}</Text>
              {settingsOpen ? <ChevronDown size={12} color="var(--mantine-color-dimmed)" /> : <ChevronRight size={12} color="var(--mantine-color-dimmed)" />}
            </Group>
          </UnstyledButton>
          <Collapse expanded={settingsOpen}>
            <Stack gap="sm" pb="sm">
              <Select
                label={t('nodeSettings.onError')}
                value={selectedNode.data.errorStrategy}
                onChange={(v) => updateNodeSettings(selectedNode.id, { errorStrategy: v ?? 'Terminate' })}
                data={[
                  { label: t('nodeSettings.stopWorkflow'), value: 'Terminate' },
                  { label: t('nodeSettings.continue'), value: 'Continue' },
                ]}
              />
              <Group
                justify="space-between"
                align="center"
                onClick={() => {
                  if (selectedNode.data.retryPolicy !== null) {
                    updateNodeSettings(selectedNode.id, { retryPolicy: null });
                  } else {
                    updateNodeSettings(selectedNode.id, { retryPolicy: toRetryPolicyDto(DEFAULT_RETRY_POLICY_UI) });
                  }
                }}
                style={{ cursor: 'pointer' }}
                p={4}
              >
                <Switch
                  checked={selectedNode.data.retryPolicy !== null}
                  onChange={(e) => {
                    if (e.currentTarget.checked) {
                      updateNodeSettings(selectedNode.id, { retryPolicy: toRetryPolicyDto(DEFAULT_RETRY_POLICY_UI) });
                    } else {
                      updateNodeSettings(selectedNode.id, { retryPolicy: null });
                    }
                  }}
                  size="sm"
                  onClick={(e) => e.stopPropagation()}
                />
                <Group gap={4} style={{ flex: 1 }}>
                  <Text size="xs" fw={400}>{t('nodeSettings.retryOnFail')}</Text>
                  <InfoTooltip label={t('nodeSettings.retryTooltip')} />
                </Group>
              </Group>

              {selectedNode.data.retryPolicy !== null && (() => {
                const policy = fromRetryPolicyDto(selectedNode.data.retryPolicy!);
                return (
                  <Stack gap="sm" ml="md">
                    <Select
                      label={t('nodeSettings.maxRetries')}
                      value={String(policy.maxRetries)}
                      onChange={(v) => updateNodeSettings(selectedNode.id, { retryPolicy: toRetryPolicyDto({ ...policy, maxRetries: v != null ? Number(v) : 2 }) })}
                      data={[
                        { label: '2', value: '2' },
                        { label: '3', value: '3' },
                        { label: '5', value: '5' },
                        { label: '10', value: '10' },
                      ]}
                    />
                    <Select
                      label={t('nodeSettings.delayBetweenRetries')}
                      value={String(policy.baseDelayMs)}
                      onChange={(v) => updateNodeSettings(selectedNode.id, { retryPolicy: toRetryPolicyDto({ ...policy, baseDelayMs: v != null ? Number(v) : 1000 }) })}
                      data={[
                        { label: '500', value: '500' },
                        { label: '1000', value: '1000' },
                        { label: '2000', value: '2000' },
                        { label: '5000', value: '5000' },
                      ]}
                    />
                  </Stack>
                );
              }              )()}

              <NumberInput
                label={t('nodeSettings.timeout')}
                description={t('nodeSettings.timeoutDescription')}
                value={selectedNode.data.timeout ?? 0}
                min={0}
                allowNegative={false}
                disabled={isExecuting || reviewMode}
                size="sm"
                onChange={(v) => {
                  const n = typeof v === 'number' ? v : Number(v);
                  updateNodeSettings(selectedNode.id, { timeout: n > 0 ? n : null });
                }}
              />
            </Stack>
          </Collapse>
        </Stack>
      </ScrollArea>
    </Stack>
  );
}
