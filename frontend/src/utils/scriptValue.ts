/**
 * 从参数值中提取脚本源码字符串。
 *
 * 后端 Script 类型在序列化时形如 { source: "..." }（对象），
 * 而前端字段组件需要纯字符串进行编辑/展示。
 * 若值为 Script 对象则取其 source；否则按原值处理（非字符串时回退为空串）。
 */
export function extractScriptSource(value: unknown): string {
  if (value == null) return '';
  if (typeof value === 'string') return value;
  if (typeof value === 'object' && 'source' in value) {
    const source = (value as { source?: unknown }).source;
    return typeof source === 'string' ? source : '';
  }
  return '';
}
