using FlowEngine.Core.Enums;
using System.ComponentModel.DataAnnotations.Schema;

namespace FlowEngine.Core.Entities;

/// <summary>
/// 节点定义（POJO）。
/// </summary>
[NotMapped]
public class NodeDefinition
{
    /// <summary>
    /// 主键。
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// 节点类型名。
    /// </summary>
    public string TypeName { get; set; } = string.Empty;

    /// <summary>
    /// 节点名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 参数映射。
    /// </summary>
    public Dictionary<string, object> Parameters { get; set; } = [];

    /// <summary>
    /// 端口实例列表。
    /// </summary>
    public List<PortInstance> Ports { get; set; } = [];

    /// <summary>
    /// X 坐标。
    /// </summary>
    public int PositionX { get; set; }

    /// <summary>
    /// Y 坐标。
    /// </summary>
    public int PositionY { get; set; }

    /// <summary>
    /// 是否为入口节点。
    /// </summary>
    public bool IsEntry { get; set; }

    /// <summary>
    /// 是否禁用。
    /// </summary>
    public bool Disabled { get; set; }

    /// <summary>
    /// 重试策略。
    /// </summary>
    public RetryPolicy? RetryPolicy { get; set; }

    /// <summary>
    /// 错误处理策略。
    /// </summary>
    public ErrorStrategy ErrorStrategy { get; set; }

    /// <summary>
    /// 超时时间。
    /// </summary>
    public TimeSpan? Timeout { get; set; }
}
