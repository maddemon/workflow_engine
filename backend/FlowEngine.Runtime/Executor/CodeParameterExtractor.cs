using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;

namespace FlowEngine.Runtime.Executor;

/// <summary>
/// 从参数字典中剥离 CodeEditor/Script 字符串参数，避免被 ParameterResolver 误求值。
/// </summary>
internal static class CodeParameterExtractor
{
    /// <summary>
    /// 扫描描述符，找出 type != Script 且 hint is CodeEditor/Script 的参数名，
    /// 从 rawParameters 中移除并返回。
    /// </summary>
    public static Dictionary<string, object> Extract(Dictionary<string, object> rawParameters, NodeTypeDescriptor descriptor)
    {
        var codeParamNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in descriptor.Parameters)
        {
            if (p.Type != ParameterType.Script && p.Hint is PresentationHint.CodeEditor or PresentationHint.Script)
            {
                codeParamNames.Add(p.Name);
            }
        }

        var rawCodeParams = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        if (codeParamNames.Count > 0)
        {
            foreach (var name in codeParamNames)
            {
                if (rawParameters.Remove(name, out var val))
                {
                    rawCodeParams[name] = val;
                }
            }
        }
        return rawCodeParams;
    }
}
