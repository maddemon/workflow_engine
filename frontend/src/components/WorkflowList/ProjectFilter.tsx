import { Select } from '@mantine/core';
import { useRequest } from 'ahooks';
import { useTranslation } from 'react-i18next';
import { getProjects } from '../../services/api.ts';

interface ProjectFilterProps {
  value: string | null;
  onChange: (value: string | null) => void;
}

export function ProjectFilter({ value, onChange }: ProjectFilterProps) {
  const { t } = useTranslation('workflow');
  const { data: projects = [] } = useRequest(getProjects);

  const selectData = [
    { value: '__all__', label: t('projectFilterAll') },
    { value: '__none__', label: t('projectFilterUncategorized') },
    ...projects.map((p) => ({ value: p.id, label: p.name })),
  ];

  return (
    <Select
      size="xs"
      placeholder={t('projectFilterPlaceholder')}
      data={selectData}
      value={value ?? '__all__'}
      onChange={(val) => {
        if (val === '__all__' || val === null) onChange(null);
        else if (val === '__none__') onChange('__none__');
        else onChange(val);
      }}
      clearable
      w={180}
    />
  );
}
