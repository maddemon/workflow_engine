using System.Diagnostics;
using System.Text.Json;
using FlowEngine.Core.Exceptions;

namespace FlowEngine.Host.Middlewares;

/// <summary>
/// 全局异常处理中间件，捕获未处理异常并返回统一的错误响应格式。
/// </summary>
public class GlobalExceptionHandlerMiddleware(
    RequestDelegate next,
    ILogger<GlobalExceptionHandlerMiddleware> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// 处理请求，捕获未处理异常并转换为统一错误响应。
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var (status, title) = MapException(exception);
        var traceId = Activity.Current?.Id ?? context.TraceIdentifier;

        // 5xx 异常记录完整堆栈以便排查，4xx 业务异常仅记录消息。
        if (status >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "未处理异常，traceId={TraceId}", traceId);
        }
        else
        {
            logger.LogWarning(exception, "业务异常 {Status}: {Message}", status, exception.Message);
        }

        // 5xx 异常隐藏内部细节，仅返回通用提示，避免泄露敏感信息。
        // 4xx 业务异常默认返回通用提示，避免泄露内部实现细节（如 KeyNotFoundException 等）；
        // 仅显式面向用户的业务异常（BusinessException / ArgumentException）保留原始消息。
        var detail = status >= StatusCodes.Status500InternalServerError
            ? "服务内部错误，请稍后重试或联系管理员。"
            : exception is BusinessException or ArgumentException
                ? exception.Message
                : "请求无法处理，请检查输入或稍后重试。";

        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json; charset=utf-8";

        var payload = new
        {
            type = "error",
            title,
            status,
            detail,
            traceId,
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(payload, JsonOptions));
    }

    private static (int status, string title) MapException(Exception exception)
    {
        return exception switch
        {
            PermissionDeniedException => (StatusCodes.Status403Forbidden, "Forbidden"),
            UnauthorizedException => (StatusCodes.Status401Unauthorized, "Unauthorized"),
            NotFoundException => (StatusCodes.Status404NotFound, "Not Found"),
            BusinessException => (StatusCodes.Status400BadRequest, "Bad Request"),
            ArgumentException => (StatusCodes.Status400BadRequest, "Bad Request"),
            InvalidOperationException => (StatusCodes.Status500InternalServerError, "Internal Server Error"),
            UnauthorizedAccessException => (StatusCodes.Status403Forbidden, "Forbidden"),
            KeyNotFoundException => (StatusCodes.Status404NotFound, "Not Found"),
            _ => (StatusCodes.Status500InternalServerError, "Internal Server Error"),
        };
    }
}
