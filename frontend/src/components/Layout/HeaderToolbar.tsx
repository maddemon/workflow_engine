import { AppShell, Group, TextInput, Badge, Button, ActionIcon, Tooltip, Text } from '@mantine/core';
import { Undo2, Redo2, Save, FilePlus } from 'lucide-react';
import { useWorkflowStore } from '../../stores/workflowStore.ts';
import { ExecutionButton } from '../ExecutionPanel/ExecutionButton.tsx';

/**
 * 顶部工具栏：工作流名称、Undo/Redo、Save、Execute。
 */
export function HeaderToolbar() {
  const workflowName = useWorkflowStore((s) => s.workflowName);
  const setWorkflowName = useWorkflowStore((s) => s.setWorkflowName);
  const isDirty = useWorkflowStore((s) => s.isDirty);
  const saving = useWorkflowStore((s) => s.saving);
  const saveWorkflow = useWorkflowStore((s) => s.saveWorkflow);
  const newWorkflow = useWorkflowStore((s) => s.newWorkflow);
  const canUndo = useWorkflowStore((s) => s.canUndo);
  const canRedo = useWorkflowStore((s) => s.canRedo);
  const undo = useWorkflowStore((s) => s.undo);
  const redo = useWorkflowStore((s) => s.redo);
  const validationErrors = useWorkflowStore((s) => s.validationErrors);

  // validationErrors 改为字段级后，按 nodeId 聚合显示"错误节点数"
  const errorNodeCount = Object.keys(validationErrors).length;

  return (
    <AppShell.Header>
      <Group justify="space-between" h="100%" px="md" gap="sm" wrap="nowrap">
        <Group gap="sm" wrap="nowrap">
          <Button
            variant="default"
            leftSection={<FilePlus size={16} />}
            onClick={newWorkflow}
            title="New Workflow"
          >
            New
          </Button>
          <Group gap="xs">
            <Tooltip label="Undo (Ctrl+Z)" disabled={!canUndo}>
              <ActionIcon variant="default" onClick={undo} disabled={!canUndo} aria-label="Undo">
                <Undo2 size={16} />
              </ActionIcon>
            </Tooltip>
            <Tooltip label="Redo (Ctrl+Y)" disabled={!canRedo}>
              <ActionIcon variant="default" onClick={redo} disabled={!canRedo} aria-label="Redo">
                <Redo2 size={16} />
              </ActionIcon>
            </Tooltip>
          </Group>
          <TextInput
            variant="unstyled"
            placeholder="Workflow name..."
            value={workflowName}
            onChange={(e) => setWorkflowName(e.target.value)}
            w={260}
            styles={{ input: { fontWeight: 600, fontSize: 14 } }}
          />
          {isDirty && (
            <Text c="orange" fw={700} size="lg" component="span" title="Unsaved changes">
              *
            </Text>
          )}
          {errorNodeCount > 0 && (
            <Badge color="red" variant="filled" title="Validation errors">
              {errorNodeCount} error{errorNodeCount > 1 ? 's' : ''}
            </Badge>
          )}
        </Group>
        <Group gap="sm">
          <Button leftSection={<Save size={16} />} onClick={saveWorkflow} loading={saving}>
            {saving ? 'Saving...' : 'Save'}
          </Button>
          <ExecutionButton />
        </Group>
      </Group>
    </AppShell.Header>
  );
}
