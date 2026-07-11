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
}

type FieldComponent = FC<FieldProps>;

/**
 * hint → 字段组件映射。
 * 未列出的 hint（Default/Select/DateTime）将回退到 typeFieldMap。
 */
export const hintFieldMap: Partial<Record<PresentationHint, FieldComponent>> = {
  ButtonGroup: ButtonGroupField as FieldComponent,
  Toggle: BooleanField as FieldComponent,
  TextArea: TextAreaField as FieldComponent,
  CodeEditor: CodeField as FieldComponent,
  JsonEditor: JsonField as FieldComponent,
  Secret: SecretField as FieldComponent,
  CredentialSelect: CredentialField as FieldComponent,
  ResourceSelect: ResourceField as FieldComponent,
  FileUpload: FileField as FieldComponent,
  Expression: ExpressionField as FieldComponent,
  Script: ExpressionField as FieldComponent,
  KeyValueEditor: KeyValueField as FieldComponent,
  Array: ArrayField as FieldComponent,
};

/**
 * type → 字段组件映射（默认渲染）。
 */
export const typeFieldMap: Partial<Record<ParameterType, FieldComponent>> = {
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
};
