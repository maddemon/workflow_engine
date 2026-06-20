import { memo } from 'react';
import { Text } from '@mantine/core';
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
      style={{ '--node-category-color': categoryColor } as React.CSSProperties}
    >
      <div className="node-card-icon">
        <NodeIcon icon={descriptor.icon} size={13} color={categoryColor} />
      </div>
      <Text size="xs" flex={1} truncate ml="xs" fw={500}>{descriptor.displayName}</Text>
    </div>
  );
}

export const NodeCard = memo(NodeCardComponent);
