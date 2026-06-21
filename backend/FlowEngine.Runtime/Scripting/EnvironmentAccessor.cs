namespace FlowEngine.Runtime.Scripting;

/// <summary>
/// 环境变量访问器，只暴露白名单中的环境变量。
/// 通过 Jint 的 ObjectWrapper 实现 JS 侧的 env.VAR_NAME 访问。
/// </summary>
public sealed class EnvironmentAccessor
{
    private readonly IReadOnlySet<string> _whitelist;

    /// <summary>
    /// 初始化环境变量访问器。
    /// </summary>
    public EnvironmentAccessor(IReadOnlySet<string> whitelist)
    {
        _whitelist = whitelist;
    }

    /// <summary>
    /// 获取环境变量值。仅白名单中的变量可访问。
    /// </summary>
    public string? this[string name]
    {
        get
        {
            if (!_whitelist.Contains(name))
            {
                throw new InvalidOperationException($"环境变量 '{name}' 不在白名单中。");
            }

            return Environment.GetEnvironmentVariable(name);
        }
    }

    /// <summary>
    /// JS 端通过 ObjectWrapper 调用此方法访问成员。
    /// </summary>
    public override string? ToString()
    {
        return null;
    }
}
