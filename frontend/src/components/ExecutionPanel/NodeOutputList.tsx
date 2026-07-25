import { useState } from 'react';
import { Text } from '@mantine/core';
import { useTranslation } from 'react-i18next';
import type { NodeExecutionRecordDto } from '../../types/workflow.ts';
import { StepItem } from './StepItem.tsx';
import { isAgentOutput } from './nodeOutputUtils.ts';
import styles from './NodeOutputList.module.css';

interface NodeOutputListProps {
  records: NodeExecutionRecordDto[];
  nodeNames?: Record<string, string>;
  nodeTypeNames?: Record<string, string>;
}

export function NodeOutputList({ records, nodeNames, nodeTypeNames }: NodeOutputListProps) {
  const { t } = useTranslation('execution');
  const [expanded, setExpanded] = useState<Record<string, boolean>>({});

  const toggle = (id: string) => {
    setExpanded((prev) => ({ ...prev, [id]: !prev[id] }));
  };

  if (records.length === 0) {
    return (
      <Text size="sm" c="dimmed" ta="center" py="md">
        {t('noNodeRecords')}
      </Text>
    );
  }

  return (
    <div className={styles.list}>
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
