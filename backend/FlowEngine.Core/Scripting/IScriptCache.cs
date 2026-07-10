namespace FlowEngine.Core.Scripting;

/// <summary>
/// 脚本编译产物缓存。
/// </summary>
public interface IScriptCache
{
    /// <summary>
    /// 获取或编译指定脚本。
    /// </summary>
    PreparedScript GetOrPrepare(Script script);

    /// <summary>
    /// 当缓存条目超过指定上限时，按加入顺序移除最旧的条目。
    /// </summary>
    void TrimIfNeeded(int maxItems);
}
