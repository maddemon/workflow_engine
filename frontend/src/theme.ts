import { createTheme, type MantineColorsTuple } from '@mantine/core';

/**
 * 节点分类色，用于节点卡片左侧色条与图标着色。
 * key 为 NodeTypeDescriptor.category，缺失时回退到 gray。
 */
export const nodeCategoryColors: Record<string, string> = {
  HTTP: '#3b82f6',
  Core: '#8b5cf6',
  Utility: '#64748b',
  Entry: '#8b5cf6',
  Unknown: '#94a3b8',
};

/**
 * 获取节点分类色，未匹配时回退到灰色。
 */
export function getNodeCategoryColor(category: string): string {
  return nodeCategoryColors[category] ?? nodeCategoryColors.Unknown;
}

// 自定义主色：蓝色（与原 --color-primary 一致）
const brandBlue: MantineColorsTuple = [
  '#e0f2fe',
  '#bae6fd',
  '#7dd3fc',
  '#38bdf8',
  '#0ea5e9',
  '#0284c7',
  '#0369a1',
  '#075985',
  '#0c4a6e',
  '#082f49',
];

export const theme = createTheme({
  primaryColor: 'brand-blue',
  defaultRadius: 'md',
  fontFamily:
    "system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif",
  fontFamilyMonospace:
    "ui-monospace, Consolas, 'Courier New', monospace",
  colors: {
    'brand-blue': brandBlue,
  },
  components: {
    Button: {
      defaultProps: {
        size: 'sm',
      },
    },
    TextInput: {
      defaultProps: {
        size: 'sm',
      },
    },
    Select: {
      defaultProps: {
        size: 'sm',
      },
    },
    Textarea: {
      defaultProps: {
        size: 'sm',
      },
    },
    Switch: {
      defaultProps: {
        size: 'sm',
      },
    },
    Paper: {
      defaultProps: {
        radius: 'md',
      },
    },
  },
});
