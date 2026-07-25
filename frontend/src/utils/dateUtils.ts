/**
 * 将后端返回的 UTC ISO 日期字符串格式化为本地时间显示。
 *
 * 后端 DateTime 序列化为 JSON 时不带 timezone 后缀（如 "2026-07-25T10:30:00"），
 * 因此需要显式追加 "Z" 标记为 UTC，再由 toLocaleString 转换为浏览器时区。
 *
 * @param iso 后端返回的 ISO 字符串，或 null/undefined。
 * @param options 可选的 Intl.DateTimeFormatOptions。
 * @returns 格式化后的本地时间字符串，无效或空输入返回 '—'。
 */
export function formatLocalDateTime(
  iso: string | null | undefined,
  options?: Intl.DateTimeFormatOptions,
): string {
  if (!iso) return '—';

  // 后端 UTC 时间无 timezone 标识，追加 Z 确保被解释为 UTC
  const normalized = iso.endsWith('Z') || /[+-]\d{2}:\d{2}$/.test(iso) ? iso : iso + 'Z';
  const d = new Date(normalized);
  if (Number.isNaN(d.getTime())) return '—';

  return d.toLocaleString(undefined, options ?? {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
  });
}

/**
 * 仅格式化日期部分（不含时间）。
 */
export function formatLocalDate(
  iso: string | null | undefined,
): string {
  if (!iso) return '—';
  const normalized = iso.endsWith('Z') || /[+-]\d{2}:\d{2}$/.test(iso) ? iso : iso + 'Z';
  const d = new Date(normalized);
  if (Number.isNaN(d.getTime())) return '—';
  return d.toLocaleDateString(undefined, {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  });
}
