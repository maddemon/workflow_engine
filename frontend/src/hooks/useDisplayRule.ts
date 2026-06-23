import { useCallback } from 'react';
import type { ParameterDefinition } from '../types/workflow.ts';

/**
 * 安全的条件表达式求值器。
 * 仅支持简单比较：{{ parameter.method }} == 'POST' 形式。
 * 不使用 new Function() 或 eval()，避免代码注入风险。
 */
export function useDisplayRule(values: Record<string, unknown>) {
  const isVisible = useCallback(
    (def: ParameterDefinition): boolean => {
      if (!def.displayRule) return true;

      const { condition, dependencies } = def.displayRule;
      if (!condition || !dependencies || dependencies.length === 0) return true;

      try {
        return evaluateCondition(condition, values);
      } catch {
        return true;
      }
    },
    [values],
  );

  return { isVisible };
}

/**
 * 安全求值条件表达式。
 * 支持格式：{{ parameter.method }} == 'POST' 或 {{ parameter.method }} != 'GET'
 * 仅支持 == 和 != 运算符，值用单引号或双引号包裹。
 */
function evaluateCondition(condition: string, values: Record<string, unknown>): boolean {
  const trimmed = condition.trim();

  // 先拆分 || / && 组合条件，避免 == 正则误匹配含 || 的字符串
  const orParts = trimmed.split(/\s*\|\|\s*/);
  if (orParts.length > 1) {
    return orParts.some((part) => evaluateCondition(part.trim(), values));
  }

  const andParts = trimmed.split(/\s*&&\s*/);
  if (andParts.length > 1) {
    return andParts.every((part) => evaluateCondition(part.trim(), values));
  }

  // 尝试匹配 == 比较
  const eqMatch = trimmed.match(/^(.+?)\s*==\s*['"](.+?)['"]\s*$/);
  if (eqMatch) {
    const left = resolveValue(eqMatch[1].trim(), values);
    const right = eqMatch[2];
    return String(left) === right;
  }

  // 尝试匹配 != 比较
  const neqMatch = trimmed.match(/^(.+?)\s*!=\s*['"](.+?)['"]\s*$/);
  if (neqMatch) {
    const left = resolveValue(neqMatch[1].trim(), values);
    const right = neqMatch[2];
    return String(left) !== right;
  }

  // 无法解析的条件默认显示
  return true;
}

/**
 * 解析 {{ $parameter.xxx }} 或 {{ parameter.xxx }} 格式的变量引用。
 */
function resolveValue(expr: string, values: Record<string, unknown>): unknown {
  const trimmed = expr.trim();

  // 匹配 {{ $parameter.xxx }} 或 {{ parameter.xxx }} 格式
  const templateMatch = trimmed.match(/^\{\{\s*\$?(parameter)\.(\w+(?:\.\w+)*)\s*\}\}$/);
  if (templateMatch) {
    // 第二个捕获组是实际参数名
    return values[templateMatch[2]];
  }

  return values[trimmed];
}
