import type { WorkflowNode } from '../stores/workflowStore.ts';

export interface ExpressionHintGroup {
  label: string;
  variables: string[];
}

/**
 * 表达式变量提示生成工具。
 * 按计划 plan-mvp-10 阶段五实现：在输入框聚焦时提示可用变量。
 */
export function getExpressionHints(nodes: WorkflowNode[]): ExpressionHintGroup[] {
  const groups: ExpressionHintGroup[] = [
    {
      label: 'Input Data',
      variables: [
        '{{ input.fieldName }}',
        '{{ input.* }}',
        '{{ inputs.portName }}',
        '{{ inputs.portName.* }}',
      ],
    },
    {
      label: 'Parameters',
      variables: [
        '{{ parameter.paramName }}',
      ],
    },
    {
      label: 'Node Outputs',
      variables: nodes.length > 0
        ? nodes.map((n) => `{{ nodes.${n.data.name} }}`)
        : ['{{ nodes.NodeName }}'],
    },
    {
      label: 'Loop Context',
      variables: [
        '{{ items }}',
        '{{ runIndex }}',
      ],
    },
    {
      label: 'Workflow & Execution',
      variables: [
        '{{ workflow.id }}',
        '{{ workflow.name }}',
        '{{ execution.id }}',
      ],
    },
    {
      label: 'Environment',
      variables: [
        '{{ env.VARIABLE_NAME }}',
      ],
    },
    {
      label: 'Utilities',
      variables: [
        '{{ now }}',
      ],
    },
  ];

  return groups;
}

/**
 * 获取扁平化的提示列表（用于 tooltip 等简单展示）。
 */
export function getFlatExpressionHints(nodes: WorkflowNode[]): string[] {
  return getExpressionHints(nodes).flatMap((g) => g.variables);
}