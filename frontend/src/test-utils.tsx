import { type ReactElement } from 'react';
import { render, type RenderOptions } from '@testing-library/react';
import { MantineProvider } from '@mantine/core';
import { I18nextProvider } from 'react-i18next';
import i18n from 'i18next';
import { initReactI18next } from 'react-i18next';

import commonEn from '../public/locales/en/common.json';
import settingsEn from '../public/locales/en/settings.json';
import workflowEn from '../public/locales/en/workflow.json';
import executionEn from '../public/locales/en/execution.json';
import adminEn from '../public/locales/en/admin.json';
import loginEn from '../public/locales/en/login.json';
import headerEn from '../public/locales/en/header.json';
import nodePanelEn from '../public/locales/en/nodePanel.json';
import parameterPanelEn from '../public/locales/en/parameterPanel.json';
import helpEn from '../public/locales/en/help.json';
import credentialPanelEn from '../public/locales/en/credentialPanel.json';

import commonZh from '../public/locales/zh-CN/common.json';
import settingsZh from '../public/locales/zh-CN/settings.json';
import workflowZh from '../public/locales/zh-CN/workflow.json';
import executionZh from '../public/locales/zh-CN/execution.json';
import adminZh from '../public/locales/zh-CN/admin.json';
import loginZh from '../public/locales/zh-CN/login.json';
import headerZh from '../public/locales/zh-CN/header.json';
import nodePanelZh from '../public/locales/zh-CN/nodePanel.json';
import parameterPanelZh from '../public/locales/zh-CN/parameterPanel.json';
import helpZh from '../public/locales/zh-CN/help.json';
import credentialPanelZh from '../public/locales/zh-CN/credentialPanel.json';

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
        workflow: workflowEn,
        execution: executionEn,
        admin: adminEn,
        login: loginEn,
        header: headerEn,
        nodePanel: nodePanelEn,
        parameterPanel: parameterPanelEn,
        help: helpEn,
        credentialPanel: credentialPanelEn,
      },
      'zh-CN': {
        common: commonZh,
        settings: settingsZh,
        workflow: workflowZh,
        execution: executionZh,
        admin: adminZh,
        login: loginZh,
        header: headerZh,
        nodePanel: nodePanelZh,
        parameterPanel: parameterPanelZh,
        help: helpZh,
        credentialPanel: credentialPanelZh,
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
