const HANDLE_PREFIX = "port-"

/**
 * 端口名（如 "Input 1"）可能包含空格，而空格在 React Flow 的 handle id 中会导致连线无法
 * 锚定：React Flow 报 "Couldn't create edge for ... handle id" 并静默丢弃该连线。
 *
 * 因此 handle id 不能原样使用端口名。这里统一用 {@link encodeHandleId} 把端口名编码为安全的
 * handle id（保留 "port-" 前缀，端口名部分做可逆编码），用 {@link decodeHandleId} 在保存时
 * 还原为规范端口名。编码对单单词端口名（如 "Output"）无副作用，向后兼容既有数据。
 */
export function encodeHandleId(portName: string): string {
  return `${HANDLE_PREFIX}${encodeURIComponent(portName)}`
}

export function decodeHandleId(handleId: string | null | undefined): string {
  if (!handleId || !handleId.startsWith(HANDLE_PREFIX)) return ""
  return decodeURIComponent(handleId.slice(HANDLE_PREFIX.length))
}
