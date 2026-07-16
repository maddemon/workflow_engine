import { memo, useCallback, useMemo } from 'react';
import { Group, ActionIcon, Tooltip, Divider, Button } from '@mantine/core';
import { notifications } from '@mantine/notifications';
import { Undo2, Redo2, ZoomIn, ZoomOut, Maximize, Save, Play, Square, Layout } from 'lucide-react';
import { useReactFlow } from '@xyflow/react';
import { useTranslation } from 'react-i18next';
import { useWorkflowStore } from '../../stores/workflowStore.ts';
import { validateParameters } from '../../utils/validateParameters.ts';

interface ICanvasToolbarProps {
  onExecute: (workflowId: string) => void;
  onCancel?: () => void;
  onDryRun?: () => void;
  dryRunLoading?: boolean;
}

export const CanvasToolbar = memo(function CanvasToolbar({ onExecute, onCancel, onDryRun, dryRunLoading }: ICanvasToolbarProps) {
  const { t } = useTranslation(['workflow', 'common']);
  const { fitView, zoomIn, zoomOut } = useReactFlow();
  const canUndo = useWorkflowStore((s) => s.canUndo);
  const canRedo = useWorkflowStore((s) => s.canRedo);
  const undo = useWorkflowStore((s) => s.undo);
  const redo = useWorkflowStore((s) => s.redo);
  const saving = useWorkflowStore((s) => s.saving);
  const saveWorkflow = useWorkflowStore((s) => s.saveWorkflow);
  const workflowId = useWorkflowStore((s) => s.workflowId);
  const nodeCount = useWorkflowStore((s) => s.nodes.length);
  const isExecuting = useWorkflowStore((s) => s.isExecuting);
  const autoLayout = useWorkflowStore((s) => s.autoLayout);
  const reviewMode = useWorkflowStore((s) => s.reviewMode);

  const canExecute = workflowId && nodeCount > 0 && !isExecuting;
  const canDryRun = nodeCount > 0 && !isExecuting;

  const allValid = useMemo(() => {
    if (nodeCount === 0) return false;
    const { nodes } = useWorkflowStore.getState();
    for (const node of nodes) {
      const { descriptor, parameters } = node.data;
      const errors = validateParameters(parameters, descriptor.parameters);
      if (Object.keys(errors).length > 0) return false;
    }
    return true;
  }, [nodeCount]);

  const handleExecute = useCallback(() => {
    if (!workflowId) return;
    const store = useWorkflowStore.getState();
    const valid = store.validateAllNodes();
    if (!valid) {
      const errors = store.validationErrors;
      const lines: string[] = [];
      const allNodes = store.nodes;
      for (const [nodeId, fields] of Object.entries(errors)) {
        const node = allNodes.find((n) => n.id === nodeId);
        const nodeName = node?.data.name ?? nodeId;
        for (const [, msg] of Object.entries(fields)) {
          lines.push(`${nodeName}: ${msg}`);
        }
      }
      notifications.show({
        title: t('toolbarConfigurationError'),
        message: lines.join('\n'),
        color: 'red',
        autoClose: 8000,
      });
      return;
    }
    onExecute(workflowId);
  }, [workflowId, onExecute, t]);

  return (
    <div className="canvas-toolbar">
      {/* 左侧：撤销/重做 + 缩放 */}
      <Group gap={2} wrap="nowrap">
        <Tooltip label={t('toolbarUndo')} position="bottom" disabled={!canUndo || isExecuting}>
          <ActionIcon variant="subtle" color="gray" size="sm" onClick={undo} disabled={!canUndo || isExecuting} aria-label={t('toolbarUndo')}>
            <Undo2 size={14} />
          </ActionIcon>
        </Tooltip>
        <Tooltip label={t('toolbarRedo')} position="bottom" disabled={!canRedo || isExecuting}>
          <ActionIcon variant="subtle" color="gray" size="sm" onClick={redo} disabled={!canRedo || isExecuting} aria-label={t('toolbarRedo')}>
            <Redo2 size={14} />
          </ActionIcon>
        </Tooltip>
        <Divider orientation="vertical" mx={2} />
        <Tooltip label={t('toolbarZoomIn')} position="bottom">
          <ActionIcon variant="subtle" color="gray" size="sm" onClick={() => zoomIn()} aria-label={t('toolbarZoomIn')}>
            <ZoomIn size={14} />
          </ActionIcon>
        </Tooltip>
        <Tooltip label={t('toolbarZoomOut')} position="bottom">
          <ActionIcon variant="subtle" color="gray" size="sm" onClick={() => zoomOut()} aria-label={t('toolbarZoomOut')}>
            <ZoomOut size={14} />
          </ActionIcon>
        </Tooltip>
        <Tooltip label={t('toolbarFitView')} position="bottom">
          <ActionIcon variant="subtle" color="gray" size="sm" onClick={() => fitView({ padding: 0.2 })} aria-label={t('toolbarFitView')}>
            <Maximize size={14} />
          </ActionIcon>
        </Tooltip>
        <Tooltip label={t('toolbarAutoLayout')} position="bottom">
          <ActionIcon
            variant="subtle"
            color="gray"
            size="sm"
            aria-label={t('toolbarAutoLayout')}
            disabled={nodeCount === 0 || isExecuting || reviewMode}
            onClick={() => {
              autoLayout();
              requestAnimationFrame(() => fitView({ padding: 0.2 }));
            }}
          >
            <Layout size={14} />
          </ActionIcon>
        </Tooltip>
      </Group>

      {/* 右侧：保存 + 执行 */}
      <Group gap="xs" wrap="nowrap">
        <Button leftSection={<Save size={12} />} onClick={saveWorkflow} loading={saving} disabled={isExecuting} size="compact-xs" variant="filled">
          {t('save', { ns: 'common' })}
        </Button>
        <Button
          leftSection={<Play size={12} />}
          variant="light"
          color="blue"
          size="compact-xs"
          onClick={onDryRun}
          disabled={!canDryRun || isExecuting || dryRunLoading}
          loading={dryRunLoading}
        >
          {t('editorDryRun')}
        </Button>
        {isExecuting ? (
          <Button
            leftSection={<Square size={12} />}
            variant="filled"
            color="red"
            size="compact-xs"
            onClick={onCancel}
          >
            {t('toolbarStop')}
          </Button>
        ) : (
          <Button
            leftSection={<Play size={12} />}
            variant={canExecute && allValid ? "filled" : "default"}
            color="green"
            size="compact-xs"
            onClick={handleExecute}
            disabled={!canExecute}
          >
            {t('toolbarTestRun')}
          </Button>
        )}
      </Group>
    </div>
  );
})
