import i18n from 'i18next';
import { initReactI18next } from 'react-i18next';
import LanguageDetector from 'i18next-browser-languagedetector';
import Backend from 'i18next-http-backend';

i18n
  .use(Backend)
  .use(LanguageDetector)
  .use(initReactI18next) // 自动注入 React Context，无需手动包 I18nextProvider
  .init({
    fallbackLng: 'en',
    supportedLngs: ['en', 'zh-CN'],
    nonExplicitSupportedLngs: true, // 浏览器 'zh' 自动映射到 'zh-CN'
    ns: [
      'common',
      'login',
      'header',
      'settings',
      'workflow',
      'nodePanel',
      'parameterPanel',
      'execution',
      'admin',
    ],
    defaultNS: 'common',
    interpolation: { escapeValue: false },
    backend: {
      loadPath: '/locales/{{lng}}/{{ns}}.json',
    },
    detection: {
      order: ['localStorage', 'navigator'],
      caches: ['localStorage'],
      lookupLocalStorage: 'i18nextLng',
    },
    react: {
      useSuspense: false, // 首次加载不触发 Suspense，避免白屏
    },
  })
  .catch((err) => {
    console.error('i18n 初始化失败:', err);
  });

export default i18n;
