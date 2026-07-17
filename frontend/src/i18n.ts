import i18n from "i18next"
import Backend from "i18next-http-backend"
import { initReactI18next } from "react-i18next"

// 时区 → 语言映射
function getLanguageFromTimezone(): string {
  try {
    const tz = Intl.DateTimeFormat().resolvedOptions().timeZone;
    if (tz.startsWith("Asia/Shanghai") || tz.startsWith("Asia/Chongqing") || tz.startsWith("Asia/Harbin") || tz.startsWith("Asia/Urumqi")) {
      return "zh-CN";
    }
  } catch {
    // Intl API 不可用时忽略
  }
  return "en";
}

// 语言检测：localStorage → 时区推测
function detectLanguage(): string {
  try {
    const stored = localStorage.getItem("i18nextLng");
    if (stored && ["en", "zh-CN"].includes(stored)) {
      return stored;
    }
  } catch {
    // 测试环境或 SSR 无 localStorage
  }
  return getLanguageFromTimezone();
}

// 初始化返回的 Promise：init() 会等所有 ns 命名空间的 JSON 加载完成后才 resolve。
// 导出后由 main.tsx 在首屏渲染前 await，避免首屏访问尚未加载的命名空间（见警告
// "namespace xxx was not yet loaded"）。catch 返回已 resolve 的 Promise，
// 即使初始化失败也保证应用照常渲染（回退到 fallbackLng）。
export const initPromise = i18n
  .use(Backend)
  .use(initReactI18next)
  .init({
    lng: detectLanguage(),
    fallbackLng: "en",
    supportedLngs: ["en", "zh-CN"],
    load: "currentOnly",
    ns: [
      "common",
      "login",
      "header",
      "settings",
      "workflow",
      "nodePanel",
      "parameterPanel",
      "execution",
      "admin",
      "help",
      "credentialPanel",
    ],
    defaultNS: "common",
    interpolation: { escapeValue: false },
    backend: {
      loadPath: "/locales/{{lng}}/{{ns}}.json",
    },
    debug: import.meta.env.DEV,
    react: {
      useSuspense: false,
    },
  })
  .catch((err) => {
    console.error("i18n 初始化失败:", err);
  });

export default i18n;
