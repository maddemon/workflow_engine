import type { ParameterDefinition, PresentationHint } from '../../types/workflow.ts';
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

interface FieldResolverProps {
  definition: ParameterDefinition;
  value: unknown;
  onChange: (value: unknown) => void;
  error?: string;
}

/**
 * 字段分发组件。
 * 优先级：definition.hint > 前端自动规则 > definition.type。
 */
export function FieldResolver({ definition, value, onChange, error }: FieldResolverProps) {
  const hint = resolveHint(definition);

  switch (hint) {
    case 'ButtonGroup':
      // 选项过多时回退到 Select
      if ((definition.options?.length ?? 0) > 6) {
        return <OptionsField definition={definition} value={value} onChange={onChange} error={error} />;
      }
      return <ButtonGroupField definition={definition} value={value} onChange={onChange} error={error} />;
    case 'Toggle':
      return <BooleanField definition={definition} value={value} onChange={onChange} error={error} />;
    case 'TextArea':
      return <TextAreaField definition={definition} value={value} onChange={onChange} error={error} />;
    case 'CodeEditor':
      return <CodeField definition={definition} value={value} onChange={onChange} error={error} />;
    case 'JsonEditor':
      return <JsonField definition={definition} value={value} onChange={onChange} error={error} />;
    case 'Secret':
      return <SecretField definition={definition} value={value} onChange={onChange} error={error} />;
    case 'CredentialSelect':
      return <CredentialField definition={definition} value={value} onChange={onChange} error={error} />;
    case 'ResourceSelect':
      return <ResourceField definition={definition} value={value} onChange={onChange} error={error} />;
    case 'FileUpload':
      return <FileField definition={definition} value={value} onChange={onChange} error={error} />;
    case 'Expression':
    case 'Script':
      return <ExpressionField definition={definition} value={value} onChange={onChange} error={error} />;
    case 'KeyValueEditor':
      return <KeyValueField definition={definition} value={value} onChange={onChange} error={error} />;
    case 'Array':
      return <ArrayField definition={definition} value={value} onChange={onChange} error={error} />;
    case 'Default':
    default:
      return renderByType(definition, value, onChange, error);
  }
}

/**
 * 按 definition.type 默认渲染。
 */
function renderByType(
  definition: ParameterDefinition,
  value: unknown,
  onChange: (value: unknown) => void,
  error?: string,
) {
  switch (definition.type) {
    case 'String':
      return <StringField definition={definition} value={value} onChange={onChange} error={error} />;
    case 'Number':
      return <NumberField definition={definition} value={value} onChange={onChange} error={error} />;
    case 'Boolean':
      return <BooleanField definition={definition} value={value} onChange={onChange} error={error} />;
    case 'Options':
      return <OptionsField definition={definition} value={value} onChange={onChange} error={error} />;
    case 'Json':
      return <JsonField definition={definition} value={value} onChange={onChange} error={error} />;
    case 'Code':
      return <CodeField definition={definition} value={value} onChange={onChange} error={error} />;
    case 'Credential':
      return <CredentialField definition={definition} value={value} onChange={onChange} error={error} />;
    case 'Resource':
      return <ResourceField definition={definition} value={value} onChange={onChange} error={error} />;
    case 'Array':
      return <ArrayField definition={definition} value={value} onChange={onChange} error={error} />;
    case 'File':
      return <FileField definition={definition} value={value} onChange={onChange} error={error} />;
    case 'Expression':
      return <ExpressionField definition={definition} value={value} onChange={onChange} error={error} />;
    default:
      return <StringField definition={definition} value={value} onChange={onChange} error={error} />;
  }
}

/**
 * 解析最终使用的渲染提示。
 * 优先级：definition.hint > 自动规则 > Default。
 */
function resolveHint(definition: ParameterDefinition): PresentationHint {
  // 1. 显式 hint 优先
  if (definition.hint && definition.hint !== 'Default') {
    return definition.hint;
  }

  // 2. 自动规则
  const nameLower = definition.name.toLowerCase();

  // Secret 仅对 String 类型生效，避免误判 Boolean/Options 等字段
  if (
    definition.type === 'String' &&
    (nameLower.includes('password') || nameLower.includes('secret') || nameLower.includes('token'))
  ) {
    return 'Secret';
  }

  switch (definition.type) {
    case 'Options':
      // 选项 2~5 个且无 hint 时，自动升级为 ButtonGroup
      if ((definition.options?.length ?? 0) <= 5 && (definition.options?.length ?? 0) >= 2) {
        return 'ButtonGroup';
      }
      // 单选项或多选项均使用 Select
      return 'Default';
    case 'Boolean':
      return 'Toggle';
    case 'Json':
      return 'JsonEditor';
    case 'Code':
      return 'CodeEditor';
    case 'Resource':
      return 'ResourceSelect';
    case 'Array':
      return 'Array';
    case 'File':
      return 'FileUpload';
    case 'Expression':
      return 'Expression';
    case 'Credential':
      return 'CredentialSelect';
    default:
      return 'Default';
  }
}
