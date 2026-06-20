import js from '@eslint/js'
import globals from 'globals'
import reactHooks from 'eslint-plugin-react-hooks'
import reactRefresh from 'eslint-plugin-react-refresh'
import tseslint from 'typescript-eslint'
import { defineConfig, globalIgnores } from 'eslint/config'

const COLOR_RE = /(?:#[0-9a-fA-F]{3,8}|rgba?\s*\(|hsla?\s*\()/;

function checkColor(node, context) {
  if (!node) return;
  if (node.type === 'Literal' && typeof node.value === 'string') {
    if (COLOR_RE.test(node.value)) {
      context.report({
        node,
        messageId: 'hardcodedColor',
        data: { value: node.value.length > 30 ? node.value.slice(0, 27) + '...' : node.value },
      });
    }
  } else if (node.type === 'TemplateLiteral') {
    for (const quasi of node.quasis) {
      if (COLOR_RE.test(quasi.value.raw)) {
        context.report({
          node: quasi,
          messageId: 'hardcodedColor',
          data: { value: quasi.value.raw.trim().length > 30 ? quasi.value.raw.trim().slice(0, 27) + '...' : quasi.value.raw.trim() },
        });
      }
    }
  }
}

const noHardcodedColorsRule = {
  meta: {
    type: 'suggestion',
    docs: { description: 'Disallow hardcoded hex/rgba/hsl colors in style props' },
    messages: {
      hardcodedColor: 'Avoid hardcoded color "{{value}}". Use a CSS variable (var(--xxx)) or theme token instead.',
    },
  },
  create(context) {
    return {
      JSXAttribute(node) {
        if (node.name.name !== 'style') return;
        if (!node.value || node.value.type !== 'JSXExpressionContainer') return;

        const expr = node.value.expression;
        if (expr.type === 'ObjectExpression') {
          for (const prop of expr.properties) {
            if (prop.type === 'Property') checkColor(prop.value, context);
          }
        } else if (expr.type === 'TemplateLiteral') {
          checkColor(expr, context);
        }
      },
    };
  },
};

export default defineConfig([
  globalIgnores(['dist']),
  {
    files: ['**/*.{ts,tsx}'],
    extends: [
      js.configs.recommended,
      tseslint.configs.recommended,
      reactHooks.configs.flat.recommended,
      reactRefresh.configs.vite,
    ],
    plugins: {
      local: { rules: { 'no-hardcoded-colors': noHardcodedColorsRule } },
    },
    rules: {
      'local/no-hardcoded-colors': 'warn',
    },
    languageOptions: {
      globals: globals.browser,
    },
  },
])
