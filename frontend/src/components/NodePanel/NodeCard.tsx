import { memo } from 'react';
import { Group, Text } from '@mantine/core';
import type { NodeTypeDescriptor } from '../../types/workflow.ts';
import { NodeIcon } from '../common/NodeIcon.tsx';
import { getNodeCategoryColor } from '../../theme.ts';

interface NodeCardProps {
  descriptor: NodeTypeDescriptor;
  onClick: (typeName: string) => void;
}

function NodeCardComponent({ descriptor, onClick }: NodeCardProps) {
  const onDragStart = (event: React.DragEvent) => {
    event.dataTransfer.setData('application/reactflow', descriptor.typeName);
    event.dataTransfer.effectAllowed = 'move';
  };

  const categoryColor = getNodeCategoryColor(descriptor.category);

  return (
    <div
      className="node-card"
      draggable
      onDragStart={onDragStart}
      onClick={() => onClick(descriptor.typeName)}
      title={`Drag to canvas or click to add ${descriptor.displayName}`}
    >
      <Group gap="xs" wrap="nowrap">
        <NodeIcon icon={descriptor.icon} size={16} color={categoryColor} />
        <Text size="sm" flex={1} truncate>{descriptor.displayName}</Text>
      </Group>
    </div>
  );
}

export const NodeCard = memo(NodeCardComponent);
