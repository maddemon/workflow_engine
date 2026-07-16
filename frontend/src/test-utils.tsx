import { type ReactElement } from 'react';
import { render, type RenderOptions } from '@testing-library/react';
import { MantineProvider } from '@mantine/core';
import { I18nextProvider } from 'react-i18next';
import i18n from 'i18next';
import { initReactI18next } from 'react-i18next';

import commonEn from '../public/locales/en/common.json';
import settingsEn from '../public/locales/en/settings.json';

const testI18n = i18n.createInstance();

testI18n
  .use(initReactI18next)
  .init({
    lng: 'en',
    fallbackLng: 'en',
    interpolation: { escapeValue: false },
    resources: {
      en: {
        common: commonEn,
        settings: settingsEn,
      },
    },
    react: {
      useSuspense: false,
    },
  });

/**
 * Render a React element wrapped in MantineProvider and I18nextProvider.
 * Use for components that use Mantine UI primitives or react-i18next.
 */
export function renderWithProvider(ui: ReactElement, options?: RenderOptions) {
  return render(
    <I18nextProvider i18n={testI18n}>
      <MantineProvider>{ui}</MantineProvider>
    </I18nextProvider>,
    options,
  );
}
