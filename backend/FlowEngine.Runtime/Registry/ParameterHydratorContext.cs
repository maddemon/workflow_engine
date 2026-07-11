using FlowEngine.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace FlowEngine.Runtime.Registry;

/// <summary>
/// 参数转换上下文，供 IValueConverter 使用。
/// </summary>
internal sealed class ParameterHydratorContext(ICredentialAccessor? credentialAccessor, ILogger<ParameterHydrator>? logger)
{
    public ICredentialAccessor? CredentialAccessor => credentialAccessor;

    public ILogger<ParameterHydrator>? Logger => logger;
}
