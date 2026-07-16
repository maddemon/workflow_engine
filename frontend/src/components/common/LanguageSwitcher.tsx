import { Select } from '@mantine/core';
import { useEffect } from 'react';
import { useTranslation } from 'react-i18next';

export function LanguageSwitcher() {
  const { i18n } = useTranslation();

  useEffect(() => {
    // 当前项目未使用 @mantine/dates，此处预留 locale 同步逻辑。
    // 未来引入 Mantine DatePicker 等日期组件时，可在此同步 dayjs
    // 与 DatesProvider 的 locale，例如：
    // const langMap: Record<string, string> = { en: 'en', 'zh-CN': 'zh-cn' };
    // dayjs.locale(langMap[i18n.resolvedLanguage ?? 'en']);
  }, [i18n.resolvedLanguage]);

  const handleChange = (val: string | null) => {
    if (!val) return;
    i18n.changeLanguage(val);
  };

  return (
    <Select
      size="xs"
      w={110}
      value={i18n.resolvedLanguage}
      onChange={handleChange}
      data={[
        { value: 'en', label: 'English' },
        { value: 'zh-CN', label: '中文' },
      ]}
    />
  );
}
