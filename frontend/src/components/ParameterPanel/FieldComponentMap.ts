import type { FC } from 'react';
import type { ParameterDefinition, PresentationHint, ParameterType } from '../../types/workflow.ts';
import { StringField } from './fields/StringField.tsx';
import { NumberField } from './fields/NumberField.tsx';
import { BooleanField } from './fields/BooleanField.tsx';
import { OptionsField } from './fields/OptionsField.tsx';
import { JsonField } from './fields/JsonField.tsx';
import { CodeField } from './fields/CodeField.tsx';
import { CredentialField } from './fields/CredentialField.tsx';
import { ButtonGroupField } from './fields/ButtonGroupField.tsx';
import { TextAreaField } from './fields/TextAreaField.tsx';
import { SecretField } from './fields/SecretField.tsx';
import { ExpressionField } from './fields/ExpressionField.tsx';
import { ResourceField } from './fields/ResourceField.tsx';
import { ArrayField } from './fields/ArrayField.tsx';
import { FileField } from './fields/FileField.tsx';
import { KeyValueField } from './fields/KeyValueField.tsx';

/**
 * 字段组件统一 props 契约。
 * 各字段组件的 onChange 值类型不同（string/boolean 等），运行时由父组件保证传入正确类型。
 */
export interface FieldProps {
  definition: ParameterDefinition;
  value: unknown;
  onChange: (value: unknown) => void;
  error?: string;
  projectId?: string | null;
}

type FieldComponent = FC<FieldProps>;

/**
 * 字段组件单一注册表（EXT-4）。
 *
 * 原先存在 `hintFieldMap` / `typeFieldMap` 两份映射，新增枚举值需在两处登记，线性增长。
 * 现合并为单一注册表，按枚举值（PresentationHint 与 ParameterType 共用的字符串值空间）注册。
 *
 * 解析优先级（见 {@link resolveFieldComponent}）保持与原来一致：hint 优先，其次 type，最后回退 StringField。
 * 对 'Array'/'Expression'/'Script' 这类两种枚举中同名的值，以 hint 映射为准（hint 优先），
 * 语义与 `hint ?? type` 一致；实际节点（IfNode/FilterNode 的 Script 条件）均显式设置了 hint，
 * 因此渲染行为与重构前完全一致。
 */
export const fieldRegistry: Partial<Record<string, FieldComponent>> = {
  // —— 默认按 type 渲染 ——
  String: StringField as FieldComponent,
  Number: NumberField as FieldComponent,
  Boolean: BooleanField as FieldComponent,
  Options: OptionsField as FieldComponent,
  Json: JsonField as FieldComponent,
  Code: CodeField as FieldComponent,
  Credential: CredentialField as FieldComponent,
  Resource: ResourceField as FieldComponent,
  Array: ArrayField as FieldComponent,
  File: FileField as FieldComponent,
  Expression: ExpressionField as FieldComponent,

  // —— 按 hint 覆盖（优先） ——
  ButtonGroup: ButtonGroupField as FieldComponent,
  Toggle: BooleanField as FieldComponent,
  TextArea: TextAreaField as FieldComponent,
  CodeEditor: CodeField as FieldComponent,
  JsonEditor: JsonField as FieldComponent,
  Secret: SecretField as FieldComponent,
  CredentialSelect: CredentialField as FieldComponent,
  ResourceSelect: ResourceField as FieldComponent,
  FileUpload: FileField as FieldComponent,
  Script: ExpressionField as FieldComponent,
  KeyValueEditor: KeyValueField as FieldComponent,
};

/**
 * 根据渲染提示与参数类型解析字段组件。
 * 优先级：hint 注册 > type 注册 > 默认 StringField。
 */
export function resolveFieldComponent(
  hint: PresentationHint,
  type: ParameterType,
): FieldComponent {
  return fieldRegistry[hint] ?? fieldRegistry[type] ?? (StringField as FieldComponent);
}
