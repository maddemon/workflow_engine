namespace FlowEngine.Core;

/// <summary>
/// 工作流引擎常用字符串常量，消除魔法字符串。
/// </summary>
public static class FlowConstants
{
    /// <summary>
    /// 标准端口名称。
    /// </summary>
    public static class PortNames
    {
        public const string Input = "input";
        public const string Output = "output";
        public const string Tools = "tools";
        public const string LlmSupply = "llmSupply";
    }

    /// <summary>
    /// 凭据字段名称。
    /// </summary>
    public static class CredentialFields
    {
        public const string ApiKey = "apiKey";
    }
}
