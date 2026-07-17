using System.ComponentModel;

namespace FlowEngine.Plugins.Standard.Data;

/// <summary>
/// 数据库写入模式。
/// </summary>
public enum DbUpsertMode
{
    /// <summary>
    /// 有则更新，无则插入。
    /// </summary>
    [Description("有则更新")]
    Upsert = 0,

    /// <summary>
    /// 仅插入。
    /// </summary>
    [Description("仅插入")]
    Insert = 1,

    /// <summary>
    /// 仅更新。
    /// </summary>
    [Description("仅更新")]
    Update = 2
}
