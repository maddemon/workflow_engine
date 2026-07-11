using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using Microsoft.Extensions.Logging;

namespace FlowEngine.Runtime.Registry.Converters;

/// <summary>
/// CredentialValue 类型转换策略（异步，涉及凭据访问器）。
/// </summary>
internal sealed class CredentialConverter : IValueConverter
{
    public bool CanConvert(Type targetType) => targetType == typeof(CredentialValue);

    public async Task<object?> ConvertAsync(object? value, Type targetType, ParameterHydratorContext context)
    {
        // value 为 CredentialValue 的情形已由 ConvertValueAsync 的 IsAssignableFrom 早返回处理，
        // 此处仅处理凭据 ID / 名称字符串解析。
        return await ConvertToCredentialAsync(value!, context).ConfigureAwait(false);
    }

    private static async Task<CredentialValue?> ConvertToCredentialAsync(object value, ParameterHydratorContext context)
    {
        var credentialAccessor = context.CredentialAccessor;
        if (credentialAccessor is null)
        {
            return null;
        }

        if (value is string credentialIdStr)
        {
            if (Guid.TryParse(credentialIdStr, out var credentialId))
            {
                try
                {
                    return await credentialAccessor.GetCredentialAsync(credentialId, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    context.Logger?.LogWarning(ex, "凭据 {CredentialId} 解析失败", credentialIdStr);
                    return null;
                }
            }

            try
            {
                return await credentialAccessor.GetCredentialByNameAsync(credentialIdStr, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                context.Logger?.LogWarning(ex, "凭据 {CredentialName} 按名称解析失败", credentialIdStr);
                return null;
            }
        }

        return null;
    }
}
