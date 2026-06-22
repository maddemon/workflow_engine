import { createTheme, type MantineColorsTuple } from '@mantine/core';

const nodeCategoryVarMap: Record<string, string> = {
  HTTP: 'var(--node-category-http)',
  Core: 'var(--node-category-core)',
  Utility: 'var(--node-category-utility)',
  Entry: 'var(--node-category-entry)',
  AI: 'var(--node-category-ai)',
  Agent: 'var(--node-category-agent)',
  LLM: 'var(--node-category-llm)',
  Tool: 'var(--node-category-tool)',
};

export function getNodeCategoryColor(category: string): string {
  return nodeCategoryVarMap[category] ?? 'var(--node-category-unknown)';
}

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

const inputStyles = {
  input: {
    height: 28,
    minHeight: 28,
    fontSize: '0.8125rem',
  },
};

export const theme = createTheme({
  primaryColor: 'brand-blue',
  defaultRadius: 'sm',
  fontFamily:
    "Inter, system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif",
  fontFamilyMonospace:
    "ui-monospace, SFMono-Regular, Consolas, 'Courier New', monospace",
  fontSizes: {
    xs: '0.75rem',
    sm: '0.8125rem',
    md: '0.875rem',
    lg: '1rem',
    xl: '1.125rem',
  },
  spacing: {
    xs: '4px',
    sm: '8px',
    md: '12px',
    lg: '16px',
    xl: '24px',
  },
  colors: {
    'brand-blue': brandBlue,
  },
  components: {
    Button: {
      defaultProps: {
        size: 'compact-sm',
        radius: 'sm',
      },
      styles: {
        root: {
          fontWeight: 500,
        },
      },
    },
    ActionIcon: {
      defaultProps: {
        size: 'compact-sm',
        radius: 'sm',
      },
    },
    TextInput: {
      defaultProps: {
        size: 'xs',
        radius: 'sm',
      },
      styles: {
        ...inputStyles,
        label: { fontWeight: 400 },
      },
    },
    NumberInput: {
      defaultProps: {
        size: 'xs',
        radius: 'sm',
      },
      styles: {
        ...inputStyles,
        label: { fontWeight: 400 },
      },
    },
    PasswordInput: {
      defaultProps: {
        size: 'xs',
        radius: 'sm',
      },
      styles: {
        ...inputStyles,
        label: { fontWeight: 400 },
      },
    },
    Select: {
      defaultProps: {
        size: 'xs',
        radius: 'sm',
      },
      styles: {
        ...inputStyles,
        label: { fontWeight: 400 },
      },
    },
    MultiSelect: {
      defaultProps: {
        size: 'xs',
        radius: 'sm',
      },
      styles: {
        ...inputStyles,
        label: { fontWeight: 400 },
      },
    },
    NativeSelect: {
      defaultProps: {
        size: 'xs',
        radius: 'sm',
      },
      styles: {
        ...inputStyles,
        label: { fontWeight: 400 },
      },
    },
    Textarea: {
      defaultProps: {
        size: 'xs',
        radius: 'sm',
      },
      styles: {
        label: { fontWeight: 400 },
      },
    },
    Switch: {
      defaultProps: {
        size: 'sm',
        radius: 'xl',
      },
    },
    Checkbox: {
      defaultProps: {
        size: 'sm',
        radius: 'sm',
      },
    },
    Radio: {
      defaultProps: {
        size: 'sm',
      },
    },
    Paper: {
      defaultProps: {
        radius: 'sm',
      },
    },
    Badge: {
      defaultProps: {
        radius: 'sm',
        size: 'sm',
      },
      styles: {
        root: {
          fontWeight: 500,
          textTransform: 'none',
          fontSize: '0.6875rem',
          height: 20,
          padding: '0 6px',
        },
      },
    },
    Tabs: {
      defaultProps: {
        radius: 'sm',
      },
    },

    Modal: {
      defaultProps: {
        radius: 'md',
        padding: 'lg',
      },
    },
    Menu: {
      defaultProps: {
        radius: 'sm',
        shadow: 'md',
      },
    },
    Tooltip: {
      defaultProps: {
        radius: 'sm',
        color: 'dark',
        size: 'sm',
      },
    },
  },
  other: {
    shadowCard: '0 1px 3px rgba(0, 0, 0, 0.08)',
    shadowCardHover: '0 4px 12px rgba(0, 0, 0, 0.12)',
    shadowNode: '0 1px 4px rgba(0, 0, 0, 0.06)',
    shadowNodeHover: '0 4px 12px rgba(0, 0, 0, 0.1)',
  },
});

declare module '@mantine/core' {
  interface MantineThemeOther {
    shadowCard: string;
    shadowCardHover: string;
    shadowNode: string;
    shadowNodeHover: string;
  }
}
