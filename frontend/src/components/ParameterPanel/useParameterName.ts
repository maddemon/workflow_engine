/**
 * 返回参数的显示名称。
 * 直接使用后端返回的 displayName（已由后端国际化）。
 */
export function useParameterName() {
  return (_name: string, displayName: string): string => {
    return displayName;
  };
}
