import type { ParameterDefinition, PresentationHint } from '../../types/workflow.ts';

/**
 * 解析最终使用的渲染提示。
 * 优先级：definition.hint > 自动规则 > Default。
 */
export function resolveHint(definition: ParameterDefinition): PresentationHint {
  // 1. 显式 hint 优先
  if (definition.hint && definition.hint !== 'Default') {
    // ButtonGroup 仅适合少量选项的单选场景；选项超过 6 个时即便显式声明也回退为 Default（Select），
    // 避免按钮过多难以使用。与下方 2~5 个选项自动升级为 ButtonGroup 的规则保持一致。
    if (
      definition.hint === 'ButtonGroup' &&
      definition.type === 'Options' &&
      (definition.options?.length ?? 0) > 6
    ) {
      return 'Default';
    }

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
