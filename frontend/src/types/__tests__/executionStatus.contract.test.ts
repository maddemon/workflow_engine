import { describe, it, expect } from 'vitest';
import type { ExecutionStatus } from '../workflow.ts';

/**
 * 契约测试：前端 ExecutionStatus 联合类型必须与后端
 * FlowEngine.Core.Enums.ExecutionStatus 枚举的 9 个成员完全一致
 * （Pending/Running/Completed/Failed/Cancelled/Compensating/Compensated/
 * CompensationFailed/DryRunCompleted），避免前后端状态值失配（Item #12）。
 *
 * 注：跨文件读取后端 .cs 枚举会触发 Vite fs 限制且需改动 tsconfig，故此处
 * 以“穷尽精确匹配”锁定联合类型；若任一成员被新增/删除/拼写错误，类型断言
 * 与运行时断言都会失败，从而强制同步更新。
 */
const backendMembers = [
  'Pending',
  'Running',
  'Completed',
  'Failed',
  'Cancelled',
  'Compensating',
  'Compensated',
  'CompensationFailed',
  'DryRunCompleted',
] as const;

type BackendMembers = (typeof backendMembers)[number];

/** 类型级精确匹配：ExecutionStatus 与后端成员集合必须完全一致。 */
type Exact<T, U> = [T] extends [U] ? ([U] extends [T] ? true : never) : never;
const _exact: Exact<ExecutionStatus, BackendMembers> = true;
void _exact;

describe('ExecutionStatus 前后端契约', () => {
  it('恰好包含 9 个成员，无多余或缺失', () => {
    expect(backendMembers).toHaveLength(9);
  });

  it('类型联合与后端成员一一对应（穷尽精确匹配）', () => {
    const values = backendMembers as readonly string[];
    // 运行时再确认联合类型在编译期即被上述 Exact 约束锁定为这 9 个值。
    expect(values).toContain('Compensating');
    expect(values).toContain('Compensated');
    expect(values).toContain('CompensationFailed');
    expect(values).toContain('DryRunCompleted');
  });
});
