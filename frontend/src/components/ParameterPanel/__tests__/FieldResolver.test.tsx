import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { FieldResolver } from '../FieldResolver.tsx';
import type { ParameterDefinition } from '../../../types/workflow.ts';

// Mock all field components; each renders a unique data-testid so tests can
// verify which component FieldResolver dispatched to.
vi.mock('../fields/StringField.tsx', () => ({
  StringField: () => <div data-testid="field-string" />,
}));
vi.mock('../fields/NumberField.tsx', () => ({
  NumberField: () => <div data-testid="field-number" />,
}));
vi.mock('../fields/BooleanField.tsx', () => ({
  BooleanField: () => <div data-testid="field-boolean" />,
}));
vi.mock('../fields/OptionsField.tsx', () => ({
  OptionsField: () => <div data-testid="field-options" />,
}));
vi.mock('../fields/JsonField.tsx', () => ({
  JsonField: () => <div data-testid="field-json" />,
}));
vi.mock('../fields/CodeField.tsx', () => ({
  CodeField: () => <div data-testid="field-code" />,
}));
vi.mock('../fields/CredentialField.tsx', () => ({
  CredentialField: () => <div data-testid="field-credential" />,
}));
vi.mock('../fields/ButtonGroupField.tsx', () => ({
  ButtonGroupField: () => <div data-testid="field-button-group" />,
}));
vi.mock('../fields/TextAreaField.tsx', () => ({
  TextAreaField: () => <div data-testid="field-textarea" />,
}));
vi.mock('../fields/SecretField.tsx', () => ({
  SecretField: () => <div data-testid="field-secret" />,
}));
vi.mock('../fields/ExpressionField.tsx', () => ({
  ExpressionField: () => <div data-testid="field-expression" />,
}));
vi.mock('../fields/ResourceField.tsx', () => ({
  ResourceField: () => <div data-testid="field-resource" />,
}));
vi.mock('../fields/ArrayField.tsx', () => ({
  ArrayField: () => <div data-testid="field-array" />,
}));
vi.mock('../fields/FileField.tsx', () => ({
  FileField: () => <div data-testid="field-file" />,
}));
vi.mock('../fields/KeyValueField.tsx', () => ({
  KeyValueField: () => <div data-testid="field-keyvalue" />,
}));

function makeDefinition(overrides: Partial<ParameterDefinition> = {}): ParameterDefinition {
  return {
    name: 'param',
    displayName: 'Param',
    type: 'String',
    defaultValue: null,
    required: false,
    validationRules: [],
    displayRule: null,
    credentialType: null,
    options: [],
    ...overrides,
  };
}

function makeOptions(count: number) {
  return Array.from({ length: count }, (_, i) => ({ label: `Opt${i}`, value: `opt${i}` }));
}

function renderResolver(definition: ParameterDefinition) {
  return render(<FieldResolver definition={definition} value={null} onChange={() => {}} />);
}

describe('FieldResolver - hint 分发', () => {
  it('显式 hint=ButtonGroup 时渲染 ButtonGroupField（options <= 5）', () => {
    renderResolver(makeDefinition({ type: 'Options', hint: 'ButtonGroup', options: makeOptions(3) }));
    expect(screen.getByTestId('field-button-group')).toBeTruthy();
  });

  it('显式 hint=ButtonGroup 且 options > 6 时回退到 OptionsField', () => {
    renderResolver(makeDefinition({ type: 'Options', hint: 'ButtonGroup', options: makeOptions(7) }));
    expect(screen.getByTestId('field-options')).toBeTruthy();
  });

  it('显式 hint=Toggle 时渲染 BooleanField', () => {
    renderResolver(makeDefinition({ type: 'Boolean', hint: 'Toggle' }));
    expect(screen.getByTestId('field-boolean')).toBeTruthy();
  });

  it('显式 hint=TextArea 时渲染 TextAreaField', () => {
    renderResolver(makeDefinition({ type: 'String', hint: 'TextArea' }));
    expect(screen.getByTestId('field-textarea')).toBeTruthy();
  });

  it('显式 hint=CodeEditor 时渲染 CodeField', () => {
    renderResolver(makeDefinition({ type: 'Code', hint: 'CodeEditor' }));
    expect(screen.getByTestId('field-code')).toBeTruthy();
  });

  it('显式 hint=JsonEditor 时渲染 JsonField', () => {
    renderResolver(makeDefinition({ type: 'Json', hint: 'JsonEditor' }));
    expect(screen.getByTestId('field-json')).toBeTruthy();
  });

  it('显式 hint=Secret 时渲染 SecretField', () => {
    renderResolver(makeDefinition({ type: 'String', hint: 'Secret' }));
    expect(screen.getByTestId('field-secret')).toBeTruthy();
  });

  it('显式 hint=CredentialSelect 时渲染 CredentialField', () => {
    renderResolver(makeDefinition({ type: 'Credential', hint: 'CredentialSelect' }));
    expect(screen.getByTestId('field-credential')).toBeTruthy();
  });

  it('显式 hint=ResourceSelect 时渲染 ResourceField', () => {
    renderResolver(makeDefinition({ type: 'Resource', hint: 'ResourceSelect' }));
    expect(screen.getByTestId('field-resource')).toBeTruthy();
  });

  it('显式 hint=FileUpload 时渲染 FileField', () => {
    renderResolver(makeDefinition({ type: 'File', hint: 'FileUpload' }));
    expect(screen.getByTestId('field-file')).toBeTruthy();
  });

  it('显式 hint=Expression 时渲染 ExpressionField', () => {
    renderResolver(makeDefinition({ type: 'Expression', hint: 'Expression' }));
    expect(screen.getByTestId('field-expression')).toBeTruthy();
  });

  it('显式 hint=Script 时渲染 ExpressionField（Expression 和 Script 共用）', () => {
    renderResolver(makeDefinition({ type: 'Expression', hint: 'Script' }));
    expect(screen.getByTestId('field-expression')).toBeTruthy();
  });

  it('显式 hint=KeyValueEditor 时渲染 KeyValueField', () => {
    renderResolver(makeDefinition({ type: 'Json', hint: 'KeyValueEditor' }));
    expect(screen.getByTestId('field-keyvalue')).toBeTruthy();
  });

  it('显式 hint=Array 时渲染 ArrayField', () => {
    renderResolver(makeDefinition({ type: 'Array', hint: 'Array' }));
    expect(screen.getByTestId('field-array')).toBeTruthy();
  });
});

describe('FieldResolver - resolveHint 自动推断', () => {
  it('String 类型 + name 含 "password" 时推断为 Secret', () => {
    renderResolver(makeDefinition({ type: 'String', name: 'userPassword' }));
    expect(screen.getByTestId('field-secret')).toBeTruthy();
  });

  it('String 类型 + name 含 "secret" 时推断为 Secret', () => {
    renderResolver(makeDefinition({ type: 'String', name: 'apiSecret' }));
    expect(screen.getByTestId('field-secret')).toBeTruthy();
  });

  it('String 类型 + name 含 "token" 时推断为 Secret', () => {
    renderResolver(makeDefinition({ type: 'String', name: 'authToken' }));
    expect(screen.getByTestId('field-secret')).toBeTruthy();
  });

  it('Options 类型 + 2 个选项时推断为 ButtonGroup', () => {
    renderResolver(makeDefinition({ type: 'Options', options: makeOptions(2) }));
    expect(screen.getByTestId('field-button-group')).toBeTruthy();
  });

  it('Options 类型 + 5 个选项时推断为 ButtonGroup', () => {
    renderResolver(makeDefinition({ type: 'Options', options: makeOptions(5) }));
    expect(screen.getByTestId('field-button-group')).toBeTruthy();
  });

  it('Options 类型 + 6 个选项时推断为 Default（走 renderByType → OptionsField）', () => {
    renderResolver(makeDefinition({ type: 'Options', options: makeOptions(6) }));
    expect(screen.getByTestId('field-options')).toBeTruthy();
  });

  it('Options 类型 + 1 个选项时推断为 Default', () => {
    renderResolver(makeDefinition({ type: 'Options', options: makeOptions(1) }));
    expect(screen.getByTestId('field-options')).toBeTruthy();
  });

  it('Boolean 类型自动推断为 Toggle', () => {
    renderResolver(makeDefinition({ type: 'Boolean' }));
    expect(screen.getByTestId('field-boolean')).toBeTruthy();
  });

  it('Json 类型自动推断为 JsonEditor', () => {
    renderResolver(makeDefinition({ type: 'Json' }));
    expect(screen.getByTestId('field-json')).toBeTruthy();
  });

  it('Code 类型自动推断为 CodeEditor', () => {
    renderResolver(makeDefinition({ type: 'Code' }));
    expect(screen.getByTestId('field-code')).toBeTruthy();
  });

  it('Resource 类型自动推断为 ResourceSelect', () => {
    renderResolver(makeDefinition({ type: 'Resource' }));
    expect(screen.getByTestId('field-resource')).toBeTruthy();
  });

  it('Array 类型自动推断为 Array', () => {
    renderResolver(makeDefinition({ type: 'Array' }));
    expect(screen.getByTestId('field-array')).toBeTruthy();
  });

  it('File 类型自动推断为 FileUpload', () => {
    renderResolver(makeDefinition({ type: 'File' }));
    expect(screen.getByTestId('field-file')).toBeTruthy();
  });

  it('Expression 类型自动推断为 Expression', () => {
    renderResolver(makeDefinition({ type: 'Expression' }));
    expect(screen.getByTestId('field-expression')).toBeTruthy();
  });

  it('Credential 类型自动推断为 CredentialSelect', () => {
    renderResolver(makeDefinition({ type: 'Credential' }));
    expect(screen.getByTestId('field-credential')).toBeTruthy();
  });
});

describe('FieldResolver - type 默认渲染', () => {
  it('hint=Default + type=String 时渲染 StringField', () => {
    renderResolver(makeDefinition({ type: 'String', hint: 'Default' }));
    expect(screen.getByTestId('field-string')).toBeTruthy();
  });

  it('hint=Default + type=Number 时渲染 NumberField', () => {
    renderResolver(makeDefinition({ type: 'Number', hint: 'Default' }));
    expect(screen.getByTestId('field-number')).toBeTruthy();
  });

  it('hint=Default + type=Boolean 时渲染 BooleanField', () => {
    renderResolver(makeDefinition({ type: 'Boolean', hint: 'Default' }));
    expect(screen.getByTestId('field-boolean')).toBeTruthy();
  });

  it('hint=Default + type=Options 时渲染 OptionsField', () => {
    renderResolver(makeDefinition({ type: 'Options', hint: 'Default', options: makeOptions(6) }));
    expect(screen.getByTestId('field-options')).toBeTruthy();
  });

  it('hint=Default + type=Json 时渲染 JsonField', () => {
    renderResolver(makeDefinition({ type: 'Json', hint: 'Default' }));
    expect(screen.getByTestId('field-json')).toBeTruthy();
  });

  it('hint=Default + type=Code 时渲染 CodeField', () => {
    renderResolver(makeDefinition({ type: 'Code', hint: 'Default' }));
    expect(screen.getByTestId('field-code')).toBeTruthy();
  });

  it('hint=Default + type=Credential 时渲染 CredentialField', () => {
    renderResolver(makeDefinition({ type: 'Credential', hint: 'Default' }));
    expect(screen.getByTestId('field-credential')).toBeTruthy();
  });

  it('hint=Default + type=Resource 时渲染 ResourceField', () => {
    renderResolver(makeDefinition({ type: 'Resource', hint: 'Default' }));
    expect(screen.getByTestId('field-resource')).toBeTruthy();
  });

  it('hint=Default + type=Array 时渲染 ArrayField', () => {
    renderResolver(makeDefinition({ type: 'Array', hint: 'Default' }));
    expect(screen.getByTestId('field-array')).toBeTruthy();
  });

  it('hint=Default + type=File 时渲染 FileField', () => {
    renderResolver(makeDefinition({ type: 'File', hint: 'Default' }));
    expect(screen.getByTestId('field-file')).toBeTruthy();
  });

  it('hint=Default + type=Expression 时渲染 ExpressionField', () => {
    renderResolver(makeDefinition({ type: 'Expression', hint: 'Default' }));
    expect(screen.getByTestId('field-expression')).toBeTruthy();
  });
});
