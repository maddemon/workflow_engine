/**
 * 参数类型与渲染提示的单一来源（TS 侧）。
 *
 * 后端对应的权威枚举位于：
 *   backend/FlowEngine.Core/Enums/ParameterType.cs
 *   backend/FlowEngine.Core/Enums/PresentationHint.cs
 *
 * 本文件的数组（`PARAMETER_TYPES` / `PRESENTATION_HINTS`）是前端值的唯一权威来源；
 * 联合类型由数组推导，避免与手写 union 漂移。前后端一致性由
 * `src/types/__tests__/parameterEnums.contract.test.ts` 在 CI 中校验（解析 C# 枚举源文件）。
 *
 * 注意：`Expression` 仅存在于前端 ParameterType —— 后端 ParameterType 枚举没有该值，
 * 前端将其作为渲染别名（映射到表达式编辑器）。该差异在契约测试中列为已知许可项，
 * 不属于需要消除的漂移。
 */

/** 与后端 `ParameterType` 枚举对应；额外保留前端渲染别名 `Expression`。 */
export const PARAMETER_TYPES = [
  'String',
  'Number',
  'Boolean',
  'Options',
  'Json',
  'Code',
  'Credential',
  'Resource',
  'Array',
  'File',
  'Script',
  'Expression',
] as const;

/** 与后端 `PresentationHint` 枚举一一对应。 */
export const PRESENTATION_HINTS = [
  'Default',
  'ButtonGroup',
  'Select',
  'TextArea',
  'CodeEditor',
  'JsonEditor',
  'KeyValueEditor',
  'Toggle',
  'Secret',
  'CredentialSelect',
  'ResourceSelect',
  'FileUpload',
  'Expression',
  'Script',
  'Array',
  'DateTime',
] as const;

/** 参数类型联合（由 {@link PARAMETER_TYPES} 推导）。 */
export type ParameterType = (typeof PARAMETER_TYPES)[number];

/** 渲染提示联合（由 {@link PRESENTATION_HINTS} 推导）。 */
export type PresentationHint = (typeof PRESENTATION_HINTS)[number];

/**
 * 后端 ParameterType 枚举中不存在、但前端保留为渲染别名的值。
 * 契约测试允许这些"额外"值存在于前端，避免误报漂移。
 */
export const ALLOWED_FRONTEND_ONLY_PARAMETER_TYPES: ReadonlyArray<ParameterType> = ['Expression'];
