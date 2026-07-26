using System.ComponentModel;

namespace FlowEngine.Core.Enums;

/// <summary>节点分类枚举。</summary>
public enum NodeCategory
{
    /// <summary>流程控制类节点（分支、循环、等待、合并等）。</summary>
    [Description("流程")] Flow,

    /// <summary>AI 相关节点（Agent、LLM 等）。</summary>
    [Description("AI")] AI,

    /// <summary>数据处理类节点（脚本、过滤、集合运算等）。</summary>
    [Description("数据")] Data,

    /// <summary>网络类节点（HTTP 请求等）。</summary>
    [Description("网络")] Network,

    /// <summary>测试用节点（单元测试中的占位节点）。</summary>
    [Description("测试")] Test,

    /// <summary>触发器节点（Manual/Schedule/Chat/Webhook/Error 等入口节点）。</summary>
    [Description("触发器")] Trigger,

    /// <summary>存储类节点（Redis/对象存储/数据库等）。</summary>
    [Description("存储")] Storage,

    /// <summary>工具/实用节点（计算、压缩、结构化输出等）。</summary>
    [Description("工具")] Utility,
}
