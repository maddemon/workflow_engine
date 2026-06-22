import { memo, useCallback, useMemo } from 'react';
import { Group, ActionIcon, Tooltip, Divider, Button } from '@mantine/core';
import { notifications } from '@mantine/notifications';
import { Undo2, Redo2, ZoomIn, ZoomOut, Maximize, Save, Play } from 'lucide-react';
import { useReactFlow } from '@xyflow/react';
import { useWorkflowStore } from '../../stores/workflowStore.ts';
import { validateParameters } from '../../utils/validateParameters.ts';

interface ICanvasToolbarProps {
  onExecute: (workflowId: string) => void;
}

export const CanvasToolbar = memo(function CanvasToolbar({ onExecute }: ICanvasToolbarProps) {
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

  const canExecute = workflowId && nodeCount > 0 && !isExecuting;

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
        title: 'Configuration Error',
        message: lines.join('\n'),
        color: 'red',
        autoClose: 8000,
      });
      return;
    }
    onExecute(workflowId);
  }, [workflowId, onExecute]);

  return (
    <div className="canvas-toolbar">
      {/* 左侧：撤销/重做 + 缩放 */}
      <Group gap={2} wrap="nowrap">
        <Tooltip label="Undo" position="bottom" disabled={!canUndo || isExecuting}>
          <ActionIcon variant="subtle" color="gray" size="sm" onClick={undo} disabled={!canUndo || isExecuting} aria-label="Undo">
            <Undo2 size={14} />
          </ActionIcon>
        </Tooltip>
        <Tooltip label="Redo" position="bottom" disabled={!canRedo || isExecuting}>
          <ActionIcon variant="subtle" color="gray" size="sm" onClick={redo} disabled={!canRedo || isExecuting} aria-label="Redo">
            <Redo2 size={14} />
          </ActionIcon>
        </Tooltip>
        <Divider orientation="vertical" mx={2} />
        <Tooltip label="Zoom In" position="bottom">
          <ActionIcon variant="subtle" color="gray" size="sm" onClick={() => zoomIn()} aria-label="Zoom In">
            <ZoomIn size={14} />
          </ActionIcon>
        </Tooltip>
        <Tooltip label="Zoom Out" position="bottom">
          <ActionIcon variant="subtle" color="gray" size="sm" onClick={() => zoomOut()} aria-label="Zoom Out">
            <ZoomOut size={14} />
          </ActionIcon>
        </Tooltip>
        <Tooltip label="Fit View" position="bottom">
          <ActionIcon variant="subtle" color="gray" size="sm" onClick={() => fitView({ padding: 0.2 })} aria-label="Fit View">
            <Maximize size={14} />
          </ActionIcon>
        </Tooltip>
      </Group>

      {/* 右侧：保存 + 执行 */}
      <Group gap="xs" wrap="nowrap">
        <Button leftSection={<Save size={12} />} onClick={saveWorkflow} loading={saving} disabled={isExecuting} size="compact-xs" variant="filled">
          Save
        </Button>
        <Button
          leftSection={<Play size={12} />}
          variant={canExecute && allValid ? "filled" : "default"}
          color="green"
          size="compact-xs"
          onClick={handleExecute}
          disabled={!canExecute}
          loading={isExecuting}
        >
          {isExecuting ? 'Running...' : 'Test Run'}
        </Button>
      </Group>
    </div>
  );
})
