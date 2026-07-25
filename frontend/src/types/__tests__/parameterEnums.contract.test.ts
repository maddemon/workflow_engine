/// <reference types="node" />
import { describe, it, expect } from 'vitest';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';
import {
  PARAMETER_TYPES,
  PRESENTATION_HINTS,
  ALLOWED_FRONTEND_ONLY_PARAMETER_TYPES,
} from '../parameterEnums.ts';

/**
 * 前后端枚举一致性契约测试（EXT-1）。
 *
 * 后端 `ParameterType` / `PresentationHint` 枚举是权威来源；本测试直接解析其 C# 源文件，
 * 断言前端 `PARAMETER_TYPES` / `PRESENTATION_HINTS` 与之保持一致，从而在 CI 中捕获枚举漂移。
 */

const here = dirname(fileURLToPath(import.meta.url));
// frontend/src/types/__tests__ → 仓库根 → backend/...
const backendEnumsDir = resolve(here, '../../../../backend/FlowEngine.Core/Enums');

function readEnumMembers(fileName: string): Set<string> {
  const source = readFileSync(resolve(backendEnumsDir, fileName), 'utf-8');
  const open = source.indexOf('{');
  const close = source.lastIndexOf('}');
  if (open < 0 || close < 0 || close < open) {
    throw new Error(`无法在 ${fileName} 中定位枚举体`);
  }
  const body = source.slice(open + 1, close);
  const members = new Set<string>();
  for (const line of body.split('\n')) {
    // 匹配枚举成员：可选前导空白 + 标识符 + 可选 "= 值" + 逗号
    const match = line.match(/^\s*([A-Za-z_]\w*)\s*(?:=[^,;]*)?\s*,?\s*$/);
    if (match) {
      members.add(match[1]);
    }
  }
  return members;
}

describe('parameter enums contract (EXT-1)', () => {
  it('ParameterType: C# 枚举值为前端超集，且仅允许已知的前端别名差异', () => {
    const csharp = readEnumMembers('ParameterType.cs');
    const ts = new Set<string>(PARAMETER_TYPES);

    // 后端每个枚举值都必须出现在前端（捕获如 "Script" 遗漏的漂移）。
    for (const value of csharp) {
      expect(ts.has(value), `前端缺少后端 ParameterType 值 "${value}"`).toBe(true);
    }

    // 前端多出的 ParameterType 值必须是已知的前端渲染别名（如 Expression）。
    const allowedExtra = new Set<string>(ALLOWED_FRONTEND_ONLY_PARAMETER_TYPES);
    for (const value of ts) {
      if (!csharp.has(value)) {
        expect(allowedExtra.has(value), `前端 ParameterType 存在未登记的额外值 "${value}"`).toBe(true);
      }
    }
  });

  it('PresentationHint: 前后端枚举值完全一致', () => {
    const csharp = readEnumMembers('PresentationHint.cs');
    const ts = new Set<string>(PRESENTATION_HINTS);

    expect([...csharp].sort()).toEqual([...ts].sort());
  });
});
