/**
 * 依赖无关的图层布局计算。
 * 仅接收普通形状参数（不引用 store），避免运行时循环依赖。
 */

const LAYER_GAP = 320;
const ROW_GAP = 140;
const MARGIN = 80;

export interface LayoutNodeInput {
  id: string;
  position: { x: number; y: number };
}

export interface LayoutEdgeInput {
  source: string;
  target: string;
}

export type LayoutDirection = 'vertical' | 'horizontal';

export type LayoutResult = Record<string, { x: number; y: number }>;

/**
 * 计算自动布局。
 * @param nodes 节点列表（仅需 id 与当前 position）
 * @param edges 边列表（source/target）
 * @param direction 布局方向：vertical 为自上而下，horizontal 为自左向右
 * @returns nodeId -> 新坐标 的映射，保证每个输入节点均出现
 */
export function computeAutoLayout(
  nodes: LayoutNodeInput[],
  edges: LayoutEdgeInput[],
  direction: LayoutDirection,
): LayoutResult {
  const nodeIds = nodes.map((n) => n.id);
  const nodeSet = new Set(nodeIds);

  // 子邻接表与入度
  const children = new Map<string, string[]>();
  const indegree = new Map<string, number>();
  for (const id of nodeIds) {
    children.set(id, []);
    indegree.set(id, 0);
  }
  for (const e of edges) {
    if (!nodeSet.has(e.source) || !nodeSet.has(e.target)) continue;
    children.get(e.source)!.push(e.target);
    indegree.set(e.target, (indegree.get(e.target) ?? 0) + 1);
  }

  // 根节点：入度为 0；若没有，则取第一个节点作为唯一根
  let roots = nodeIds.filter((id) => (indegree.get(id) ?? 0) === 0);
  if (roots.length === 0 && nodeIds.length > 0) {
    roots = [nodeIds[0]];
  }

  // 拓扑排序（队列维护零入度节点），同时计算最长路径层号
  const layer = new Map<string, number>();
  for (const id of roots) {
    layer.set(id, 0);
  }

  const queue: string[] = [...roots];
  const workIndegree = new Map(indegree);
  while (queue.length > 0) {
    const current = queue.shift()!;
    const currentLayer = layer.get(current) ?? 0;
    for (const child of children.get(current) ?? []) {
      const candidate = currentLayer + 1;
      const prev = layer.get(child);
      if (prev === undefined || candidate > prev) {
        layer.set(child, candidate);
      }
      const d = (workIndegree.get(child) ?? 0) - 1;
      workIndegree.set(child, d);
      if (d === 0) {
        queue.push(child);
      }
    }
  }

  // 未被图到达的节点（如前向环外孤立点）归入层 0
  for (const id of nodeIds) {
    if (!layer.has(id)) {
      layer.set(id, 0);
    }
  }

  // 按层分组
  const byLayer = new Map<number, string[]>();
  for (const id of nodeIds) {
    const l = layer.get(id)!;
    if (!byLayer.has(l)) byLayer.set(l, []);
    byLayer.get(l)!.push(id);
  }

  // 层内排序：沿交叉轴的当前位置，保证布局稳定
  const positionById = new Map(nodes.map((n) => [n.id, n.position]));
  const crossAxisKey = direction === 'vertical' ? 'x' : 'y';
  for (const ids of byLayer.values()) {
    ids.sort((a, b) => {
      const pa = positionById.get(a)!;
      const pb = positionById.get(b)!;
      return pa[crossAxisKey] - pb[crossAxisKey];
    });
  }

  const result: LayoutResult = {};
  for (const [l, ids] of byLayer) {
    ids.forEach((id, indexInLayer) => {
      if (direction === 'vertical') {
        result[id] = {
          x: MARGIN + indexInLayer * ROW_GAP,
          y: MARGIN + l * LAYER_GAP,
        };
      } else {
        result[id] = {
          x: MARGIN + l * LAYER_GAP,
          y: MARGIN + indexInLayer * ROW_GAP,
        };
      }
    });
  }

  return result;
}
