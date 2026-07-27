import { ActionIcon, Group, Menu, Text } from '@mantine/core';
import { Globe, Check } from 'lucide-react';
import { useTranslation } from 'react-i18next';

const LANGUAGE_OPTIONS = [
  { value: 'en', label: 'English' },
  { value: 'zh-CN', label: '中文' },
];

export function LanguageSwitcher() {
  const { i18n, ready } = useTranslation();

  const handleChange = async (val: string) => {
    if (!ready) return;
    try {
      await i18n.changeLanguage(val);
      try { localStorage.setItem('i18nextLng', val); } catch { /* ignore localStorage write failures */ }
    } catch (err) {
      console.error('[LanguageSwitcher] changeLanguage failed', err);
    }
  };

  const currentLng = i18n.resolvedLanguage ?? 'en';

  return (
    <Menu shadow="md" width={140}>
      <Menu.Target>
        <ActionIcon variant="subtle" color="gray" size="sm" aria-label="Switch language">
          <Globe size={16} />
        </ActionIcon>
      </Menu.Target>
      <Menu.Dropdown>
        {LANGUAGE_OPTIONS.map((opt) => (
          <Menu.Item
            key={opt.value}
            onClick={() => handleChange(opt.value)}
            disabled={!ready}
          >
            <Group gap="xs" wrap="nowrap">
              <Text size="sm" fw={opt.value === currentLng ? 600 : 400} style={{ flex: 1 }}>
                {opt.label}
              </Text>
              {opt.value === currentLng && <Check size={14} />}
            </Group>
          </Menu.Item>
        ))}
      </Menu.Dropdown>
    </Menu>
  );
}
