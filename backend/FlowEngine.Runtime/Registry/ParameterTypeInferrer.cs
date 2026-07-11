using System.Text.Json.Nodes;
using FlowEngine.Core.Attributes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Scripting;

namespace FlowEngine.Runtime.Registry;

/// <summary>
/// 根据 CLR 类型推断 ParameterType 和 PresentationHint。
/// </summary>
internal static class ParameterTypeInferrer
{
    public static (ParameterType Type, PresentationHint? Hint) Infer(Type clrType, HintAttribute? hintAttr)
    {
        var underlying = Nullable.GetUnderlyingType(clrType);
        var effectiveType = underlying ?? clrType;

        if (effectiveType == typeof(string))
        {
            return hintAttr?.Component switch
            {
                PresentationHint.CodeEditor => (ParameterType.Code, PresentationHint.CodeEditor),
                _ => (ParameterType.String, null)
            };
        }

        if (effectiveType == typeof(Script))
        {
            var hint = hintAttr?.Component ?? PresentationHint.Expression;
            return (ParameterType.Script, hint);
        }

        if (effectiveType.IsGenericType
            && effectiveType.GetGenericTypeDefinition() == typeof(Dictionary<,>)
            && effectiveType.GetGenericArguments() is Type[] args
            && args.Length == 2
            && args[0] == typeof(string)
            && args[1] == typeof(Script))
        {
            return (ParameterType.Json, PresentationHint.KeyValueEditor);
        }

        if (effectiveType == typeof(bool))
        {
            return (ParameterType.Boolean, PresentationHint.Toggle);
        }

        if (effectiveType == typeof(int) || effectiveType == typeof(long)
            || effectiveType == typeof(double) || effectiveType == typeof(float))
        {
            return (ParameterType.Number, null);
        }

        if (effectiveType.IsEnum)
        {
            var values = Enum.GetValues(effectiveType);
            var hint = values.Length <= 4
                ? PresentationHint.ButtonGroup
                : PresentationHint.Select;
            return (ParameterType.Options, hint);
        }

        if (typeof(JsonObject).IsAssignableFrom(effectiveType)
            || typeof(JsonNode).IsAssignableFrom(effectiveType))
        {
            return (ParameterType.Json, PresentationHint.JsonEditor);
        }

        if (effectiveType == typeof(Uri) || effectiveType == typeof(System.Net.Mail.MailAddress))
        {
            return (ParameterType.String, null);
        }

        if (effectiveType.IsGenericType
            && effectiveType.GetGenericTypeDefinition() == typeof(Dictionary<,>))
        {
            return (ParameterType.Json, PresentationHint.KeyValueEditor);
        }

        if (effectiveType.IsGenericType
            && effectiveType.GetGenericTypeDefinition() == typeof(List<>))
        {
            return (ParameterType.Array, null);
        }

        if (effectiveType.IsArray)
        {
            return (ParameterType.Array, null);
        }

        if (effectiveType == typeof(CredentialValue))
        {
            return (ParameterType.Credential, PresentationHint.CredentialSelect);
        }

        if (effectiveType == typeof(DateTime) || effectiveType == typeof(DateTimeOffset))
        {
            return (ParameterType.String, PresentationHint.DateTime);
        }

        return (ParameterType.Json, null);
    }

    public static bool IsRequired(Type clrType)
    {
        if (!clrType.IsValueType)
        {
            return false;
        }

        return Nullable.GetUnderlyingType(clrType) is null;
    }
}
