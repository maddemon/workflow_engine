import { describe, it, expect } from 'vitest';
import {
  fieldRegistry,
  resolveFieldComponent,
} from '../FieldComponentMap.ts';
import { StringField } from '../fields/StringField.tsx';
import { BooleanField } from '../fields/BooleanField.tsx';
import { ButtonGroupField } from '../fields/ButtonGroupField.tsx';
import { ExpressionField } from '../fields/ExpressionField.tsx';

/**
 * EXT-4：字段组件单一注册表测试。
 * 验证合并后的注册表覆盖原有 hintFieldMap / typeFieldMap 的键，且优先级与原逻辑一致。
 */
describe('field registry (EXT-4)', () => {
  it('合并注册表包含原 type 与 hint 的全部键', () => {
    // 原 typeFieldMap 的键
    for (const key of ['String', 'Number', 'Boolean', 'Options', 'Json', 'Code', 'Credential', 'Resource', 'Array', 'File', 'Expression']) {
      expect(fieldRegistry[key], `缺少 type 键 ${key}`).toBeDefined();
    }
    // 原 hintFieldMap 的键
    for (const key of ['ButtonGroup', 'Toggle', 'TextArea', 'CodeEditor', 'JsonEditor', 'Secret', 'CredentialSelect', 'ResourceSelect', 'FileUpload', 'Script', 'KeyValueEditor']) {
      expect(fieldRegistry[key], `缺少 hint 键 ${key}`).toBeDefined();
    }
  });

  it('hint 优先于 type（ButtonGroup 覆盖 Options）', () => {
    expect(resolveFieldComponent('ButtonGroup', 'Options')).toBe(ButtonGroupField);
  });

  it('hint 未登记时回退到 type 默认渲染', () => {
    // 'Default' 不在注册表，Boolean 默认渲染为 BooleanField
    expect(resolveFieldComponent('Default', 'Boolean')).toBe(BooleanField);
  });

  it('hint 与 type 均无映射时回退到 StringField', () => {
    expect(resolveFieldComponent('DateTime', 'Unknown' as never)).toBe(StringField);
  });

  it('Script 条件（IfNode/FilterNode）渲染为表达式编辑器', () => {
    expect(resolveFieldComponent('Script', 'Script')).toBe(ExpressionField);
  });
});
