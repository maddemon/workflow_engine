import { Paper, Text, Badge, Group, ActionIcon, Menu } from '@mantine/core';
import { MoreVertical, Trash2, ExternalLink } from 'lucide-react';
import type { WorkflowSummary } from '../../types/workflow.ts';

interface WorkflowCardProps {
  workflow: WorkflowSummary;
  onClick: (id: string) => void;
  onDelete: (id: string) => void;
}

export function WorkflowCard({ workflow, onClick, onDelete }: WorkflowCardProps) {
  return (
    <Paper
      withBorder
      p="md"
      radius="lg"
      className="workflow-card"
      style={{ cursor: 'pointer' }}
      onClick={() => onClick(workflow.id)}
    >
      <Group justify="space-between" align="flex-start">
        <div style={{ flex: 1, minWidth: 0 }}>
          <Text fw={600} size="sm" truncate>
            {workflow.name || 'Untitled Workflow'}
          </Text>
          <Group gap="xs" mt={4}>
            <Badge variant="light" size="xs" color="gray">
              v{workflow.version}
            </Badge>
            <Badge
              variant="light"
              size="xs"
              color={workflow.isActive ? 'green' : 'gray'}
            >
              {workflow.isActive ? 'Active' : 'Inactive'}
            </Badge>
          </Group>
        </div>
        <Menu shadow="md" width={160}>
          <Menu.Target>
            <ActionIcon
              variant="subtle"
              color="gray"
              size="sm"
              onClick={(e) => e.stopPropagation()}
            >
              <MoreVertical size={14} />
            </ActionIcon>
          </Menu.Target>
          <Menu.Dropdown>
            <Menu.Item
              leftSection={<ExternalLink size={14} />}
              onClick={(e) => { e.stopPropagation(); onClick(workflow.id); }}
            >
              Open
            </Menu.Item>
            <Menu.Item
              leftSection={<Trash2 size={14} />}
              color="red"
              onClick={(e) => { e.stopPropagation(); onDelete(workflow.id); }}
            >
              Delete
            </Menu.Item>
          </Menu.Dropdown>
        </Menu>
      </Group>
    </Paper>
  );
}
