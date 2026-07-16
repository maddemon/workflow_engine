import { Select } from '@mantine/core';
import { useRequest } from 'ahooks';
import { getProjects } from '../../services/api.ts';

interface ProjectFilterProps {
  value: string | null;
  onChange: (value: string | null) => void;
}

export function ProjectFilter({ value, onChange }: ProjectFilterProps) {
  const { data: projects = [] } = useRequest(getProjects);

  const selectData = [
    { value: '__all__', label: 'All Projects' },
    { value: '__none__', label: 'Uncategorized' },
    ...projects.map((p) => ({ value: p.id, label: p.name })),
  ];

  return (
    <Select
      size="xs"
      placeholder="Filter by project"
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
