using System.Diagnostics;
using System.Text.Json;
using FlowEngine.Core.Exceptions;

namespace FlowEngine.Host.Middlewares;

/// <summary>
/// 全局异常处理中间件，捕获未处理异常并返回统一 JSON 错误响应。
/// </summary>
public class GlobalExceptionHandlerMiddleware(
    RequestDelegate next,
    ILogger<GlobalExceptionHandlerMiddleware> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

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
        var (status, errorCode) = MapException(exception);
        var traceId = Activity.Current?.Id ?? context.TraceIdentifier;

        if (status >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "未处理异常，traceId={TraceId}", traceId);
        }
        else
        {
            logger.LogWarning(exception, "业务异常 {Status}: {Message}", status, exception.Message);
        }

        // 业务异常和参数异常透传原始消息，帮助开发者定位问题；
        // 系统异常返回通用提示，避免泄露数据库表名、连接字符串等敏感信息。
        var message = exception is BusinessException or ArgumentException
            ? exception.Message
            : "系统内部错误，请稍后重试。";

        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json; charset=utf-8";

        var payload = new
        {
            success = false,
            errorCode,
            message,
            details = new { traceId },
        };

        await context.Response.WriteAsync(
            JsonSerializer.Serialize(payload, JsonOptions)).ConfigureAwait(false);
    }

    private static (int status, string errorCode) MapException(Exception exception)
    {
        return exception switch
        {
            PermissionDeniedException => (StatusCodes.Status403Forbidden, "Forbidden"),
            UnauthorizedException => (StatusCodes.Status401Unauthorized, "Unauthorized"),
            NotFoundException => (StatusCodes.Status404NotFound, "NotFound"),
            BusinessException => (StatusCodes.Status400BadRequest, "BadRequest"),
            ArgumentException => (StatusCodes.Status400BadRequest, "BadRequest"),
            InvalidOperationException => (StatusCodes.Status500InternalServerError, "InternalServerError"),
            UnauthorizedAccessException => (StatusCodes.Status403Forbidden, "Forbidden"),
            KeyNotFoundException => (StatusCodes.Status404NotFound, "NotFound"),
            _ => (StatusCodes.Status500InternalServerError, "InternalServerError"),
        };
    }
}
