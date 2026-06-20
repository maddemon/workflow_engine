import { Group, ActionIcon, Tooltip, Divider, Button } from '@mantine/core';
import { Undo2, Redo2, ZoomIn, ZoomOut, Maximize, Save, Play } from 'lucide-react';
import { useReactFlow } from '@xyflow/react';
import { useWorkflowStore } from '../../stores/workflowStore.ts';
import { useExecution } from '../../hooks/useExecution.ts';

export function CanvasToolbar() {
  const { fitView, zoomIn, zoomOut } = useReactFlow();
  const canUndo = useWorkflowStore((s) => s.canUndo);
  const canRedo = useWorkflowStore((s) => s.canRedo);
  const undo = useWorkflowStore((s) => s.undo);
  const redo = useWorkflowStore((s) => s.redo);
  const saving = useWorkflowStore((s) => s.saving);
  const saveWorkflow = useWorkflowStore((s) => s.saveWorkflow);
  const workflowId = useWorkflowStore((s) => s.workflowId);
  const nodes = useWorkflowStore((s) => s.nodes);
  const { execute, status } = useExecution();

  const isRunning = status === 'loading';
  const canExecute = workflowId && nodes.length > 0 && !isRunning;

  return (
    <div className="canvas-toolbar">
      {/* 左侧：撤销/重做 + 缩放 */}
      <Group gap={2} wrap="nowrap">
        <Tooltip label="Undo" position="bottom" disabled={!canUndo}>
          <ActionIcon variant="subtle" color="gray" size="sm" onClick={undo} disabled={!canUndo} aria-label="Undo">
            <Undo2 size={14} />
          </ActionIcon>
        </Tooltip>
        <Tooltip label="Redo" position="bottom" disabled={!canRedo}>
          <ActionIcon variant="subtle" color="gray" size="sm" onClick={redo} disabled={!canRedo} aria-label="Redo">
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
        <Button leftSection={<Save size={12} />} onClick={saveWorkflow} loading={saving} size="compact-xs" variant="default">
          Save
        </Button>
        <Button
          leftSection={<Play size={12} />}
          color="green"
          size="compact-xs"
          onClick={() => canExecute && execute(workflowId)}
          disabled={!canExecute}
          loading={isRunning}
        >
          {isRunning ? 'Running...' : 'Execute'}
        </Button>
      </Group>
    </div>
  );
}
