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
        public const string Input = "Input";
        public const string Output = "Output";
        public const string Tools = "Tools";
        public const string Llm = "LLM";
        public const string Loop = "Loop";
        public const string Done = "Done";
        public const string Default = "Default";
        public const string Kept = "Kept";
        public const string Discarded = "Discarded";
        public const string Input1 = "Input 1";
        public const string Input2 = "Input 2";
        public const string True = "True";
        public const string False = "False";
    }

    /// <summary>
    /// 凭据字段名称。
    /// </summary>
    public static class CredentialFields
    {
        public const string ApiKey = "apiKey";
        public const string DbType = "dbType";
    }

    /// <summary>
    /// 共享错误码常量，供多个节点统一引用。
    /// </summary>
    public static class ErrorCodes
    {
        public const string Cancelled = "Cancelled";
        public const string HttpClientUnavailable = "HttpClientUnavailable";
        public const string HttpRequestFailed = "HttpRequestFailed";
        public const string MissingCode = "MissingCode";
        public const string MissingCommand = "MissingCommand";
        public const string MissingConnection = "MissingConnection";
        public const string MissingLlmClient = "MissingLlmClient";
        public const string MissingUrl = "MissingUrl";
        public const string ScriptError = "ScriptError";
        public const string SsrfBlocked = "SsrfBlocked";
        public const string SuccessWhenFailed = "SuccessWhenFailed";
        public const string Timeout = "Timeout";
        public const string UnexpectedError = "UnexpectedError";
    }
}
