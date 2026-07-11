import { useState } from 'react';
import { Text } from '@mantine/core';
import type { NodeExecutionRecordDto } from '../../types/workflow.ts';
import { StepItem } from './StepItem.tsx';
import { isAgentOutput } from './nodeOutputUtils.ts';

interface NodeOutputListProps {
  records: NodeExecutionRecordDto[];
  nodeNames?: Record<string, string>;
  nodeTypeNames?: Record<string, string>;
}

export function NodeOutputList({ records, nodeNames, nodeTypeNames }: NodeOutputListProps) {
  const [expanded, setExpanded] = useState<Record<string, boolean>>({});

  const toggle = (id: string) => {
    setExpanded((prev) => ({ ...prev, [id]: !prev[id] }));
  };

  if (records.length === 0) {
    return (
      <Text size="sm" c="dimmed" ta="center" py="md">
        No node records
      </Text>
    );
  }

  return (
    <div style={{ display: 'flex', flexDirection: 'column' }}>
      {records.map((record, index) => {
        const typeName = nodeTypeNames?.[record.nodeDefinitionId];
        const isAgent = typeName === 'agent' || isAgentOutput(record.output);
        return (
          <StepItem
            key={record.id}
            record={record}
            isLast={index === records.length - 1}
            isExpanded={!!expanded[record.id]}
            onToggle={() => toggle(record.id)}
            nodeName={nodeNames?.[record.nodeDefinitionId]}
            isAgent={isAgent}
          />
        );
      })}
    </div>
  );
}
