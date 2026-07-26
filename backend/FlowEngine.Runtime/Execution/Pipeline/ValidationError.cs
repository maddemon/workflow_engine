using System.Collections.Generic;

namespace FlowEngine.Runtime.Execution.Pipeline;

/// <summary>声明式校验错误（[Required]/类型约束等）。由 ValidationStage 填充，供短路构建失败结果。</summary>
/// <param name="ParameterName">出错的参数名。</param>
/// <param name="Code">错误码（如 Required / TypeMismatch）。</param>
/// <param name="Message">可读的错误说明。</param>
public sealed record ValidationError(string ParameterName, string Code, string Message);
