# 任务报告：为 NodeBase 构造函数添加静态元数据缓存 `_metaCache`

## 目标
为 `NodeBase` 构造函数增加静态元数据缓存 `_metaCache`，消除每次克隆实例（`Activator.CreateInstance`）时的反射开销。对应设计文档 `docs/designs/2026-07-26-nodetype-execution-instance-separation.md` §4.1.A。

## 状态
**DONE_WITH_CONCERNS**

实现与设计文档 §4.1.A 完全一致，全量测试通过无回归。附带一项设计层面的非阻断顾虑（见下"Self-Review 结论"）。

## 改动文件清单
| 文件 | 改动 |
|------|------|
| `backend/FlowEngine.Core/Abstractions/NodeBase.cs` | 1. 新增 `using System.Collections.Concurrent;`<br>2. 新增静态字段 `private static readonly ConcurrentDictionary<Type, (NodeMetaAttribute Meta, PortDefinition[] Ports)> _metaCache`<br>3. 构造函数改为 `_metaCache.GetOrAdd(GetType(), BuildMeta)`，保留 `_meta`/`_ports` 字段与全部使用点<br>4. 反射逻辑抽为静态方法 `BuildMeta(Type)`（含原有 `[NodeMeta]` 缺省/TypeName 空校验）与 `BuildPortsFromAttributes(Type)`（仅读 `[Port]` 特性）<br>5. 移除原实例方法 `BuildPorts()`（被静态 `BuildPortsFromAttributes` 取代）；保留 `GetExtraPorts()` 虚方法声明（仍供子类重写与测试反射调用） |
| `tests/FlowEngine.Core.Tests/NodeBaseMetadataCacheTests.cs` | 新增测试文件（见下） |

> 未修改其他任何文件，未执行 git commit（项目规则禁止未经许可提交代码）。

## 新增测试文件与运行命令输出摘要
**测试文件**：`tests/FlowEngine.Core.Tests/NodeBaseMetadataCacheTests.cs`
- 定义了两个带 `[NodeMeta]`/`[Port]` 的测试节点 `CacheTestNode` / `CacheTestNode2`（继承 `NodeBase`，实现抽象 `ExecuteAsync`）。
- 用例：
  1. `Constructor_SingleInstance_ExposesDeclaredMetadata` —— 单实例元数据（TypeName/DisplayName/Category/Icon/DefaultIsEntry）正确。
  2. `Constructor_SingleInstance_ExposesAllDeclaredPorts` —— 单实例端口数量与内容正确。
  3. `Constructor_ManyInstances_DoNotThrow_And_MetadataConsistent` —— 连续创建 100 个实例不抛异常且元数据一致。
  4. `Constructor_ManyInstances_PortsArraySharedViaCache` —— 同类型多次构造的 `Ports` 为同一缓存数组引用（`Assert.Same`，作为"命中缓存而非每次反射"的可观测信号；该用例在改动前会失败，属 RED 用例）。
  5. `Constructor_DistinctTypes_ProduceDistinctCachedMetadata` —— 不同类型缓存互不串扰。

**TDD 流程**：先写测试，运行确认 `Constructor_ManyInstances_PortsArraySharedViaCache` 失败（RED：每次构造持有独立列表），再实施改动，复跑全绿。

**运行命令与摘要**：
```
dotnet test tests/FlowEngine.Core.Tests/FlowEngine.Core.Tests.csproj --filter "FullyQualifiedName~NodeBaseMetadataCacheTests"
已通过! - 失败: 0，通过: 5，已跳过: 0，总计: 5

dotnet build        -> 成功生成，0 警告，0 错误
dotnet test (Core)  -> 通过 706
dotnet test (Runtime)-> 通过 922   （含 SwitchNode 动态端口、NodeRegistry、OutputRouter）
dotnet test (Application) -> 通过 502
dotnet test (Host)  -> 通过 374
```

## Self-Review 结论
1. **正确性**：构造函数保留原有 `[NodeMeta]` 缺省/TypeName 空校验（`InvalidOperationException`），`TypeName`/`Ports`/`DefaultIsEntry` 等所有使用点（`INodeType` 接口、`ToResult`）均从 `_meta`/`_ports` 读取，未改动语义。
2. **性能目标达成**：元数据按 `Type` 缓存，热路径（`NodeRegistry.Get/TryGet/CreateInstance` 每次克隆）第二次起命中缓存，不再反射 `[NodeMeta]`/`[Port]`。
3. **并发安全**：`ConcurrentDictionary.GetOrAdd` 保证线程安全；`GetOrAdd` 在竞争下可能对同 key 多次执行工厂（仅多反射一次，结果一致），无正确性风险。
4. **共享对象安全性**：缓存的 `PortDefinition[]` 与 `NodeMetaAttribute` 现在被同类型所有实例共享（改动前每个实例持有独立副本）。已核查所有生产消费者对 `PortDefinition` 字段均为只读访问（仅读 `.Name`/`.Direction`/`.Type`/`.OutputSchema`/`.AllowedTypes` 等），无任何字段写入，故共享不会引发串改。
5. **`GetExtraPorts` 处理**：`BuildPortsFromAttributes(Type)` 为静态方法，无法调用实例级虚方法 `GetExtraPorts()`，故缓存端口仅来自类型级 `[Port]` 特性。这与设计文档 §4.1.A 的 `BuildPortsFromAttributes(t)` 一致，且**行为等价于改动前**——因为构造发生在参数水合之前，`Cases` 恒为空，`GetExtraPorts()` 在构造期本就返回空列表。

## 顾虑（非阻断）
- **动态端口的潜在脆弱性（设计层面，非本次回归）**：`SwitchNode` 重写 `GetExtraPorts()` 以根据运行期水合后的 `Cases` 生成动态端口（case{i}）。该机制本就不在 `INodeType.Ports`（构造期计算）中生效——运行期 `INodeType.Ports` 始终只含类型级端口。本次缓存把"构造期端口"固化为按类型共享的同一数组；若未来有人试图让 `GetExtraPorts()` 通过 `_ports` 在运行期生效（即水合后重算并回写 `_ports`），静态缓存会冻结首次构造时的空 `Cases` 端口，导致动态端口失效。这是一个**应在 §4.1.A 设计阶段就明确的约束**：动态端口不得经 `_metaCache`/`_ports` 体现，须维持"仅类型级元数据缓存"的语义。当前未引入回归，仅提示后续实现 §4.1.B/§4.3（参数水合后刷新端口）时需另行处理（如端口探测走独立 `GetExtraPorts` 调用而非 `INodeType.Ports`）。
- 其余方面实现严格遵循设计文档与任务要求，未超出范围，未引入其他技术债。
