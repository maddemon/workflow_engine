import { useEffect } from 'react';
import { useLocation } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useWorkflowStore } from '../stores/workflowStore.ts';

const APP_NAME = 'Flow Engine';

/**
 * 根据当前路由设置文档标题（标签页标题），切换页面时自动更新。
 * 编辑器页面会附带工作流名称；未匹配到具体页面时回退到产品名。
 */
export function usePageTitle(): void {
  const location = useLocation();
  const { t } = useTranslation();
  const workflowName = useWorkflowStore((s) => s.workflowName);

  useEffect(() => {
    const { pathname } = location;

    let pageTitle = '';
    if (pathname === '/login') {
      pageTitle = t('login:title');
    } else if (pathname === '/') {
      pageTitle = t('workflow:list.title');
    } else if (pathname.startsWith('/workflow/') && pathname.endsWith('/history')) {
      pageTitle = t('execution:history.title');
    } else if (pathname.startsWith('/workflow/')) {
      pageTitle = workflowName.trim() ? workflowName : t('workflow:editor.title');
    } else if (pathname.startsWith('/admin/')) {
      const section = pathname.split('/')[2];
      switch (section) {
        case 'users':
          pageTitle = t('admin:users');
          break;
        case 'audit':
          pageTitle = t('admin:audit');
          break;
        case 'projects':
          pageTitle = t('admin:projects');
          break;
        case 'files':
          pageTitle = t('admin:files');
          break;
        default:
          pageTitle = t('header:admin');
          break;
      }
    } else if (pathname === '/help') {
      pageTitle = t('help:title');
    } else if (pathname === '/settings') {
      pageTitle = t('settings:title');
    }

    document.title = pageTitle ? `${pageTitle} · ${APP_NAME}` : APP_NAME;
  }, [location.pathname, workflowName, t]);
}
