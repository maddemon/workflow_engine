using System.Diagnostics;
using System.Text.Json;
using FlowEngine.Core.Exceptions;
using FlowEngine.Resources;
using Microsoft.Extensions.Localization;

namespace FlowEngine.Host.Middlewares;

public class GlobalExceptionHandlerMiddleware(
    RequestDelegate next,
    ILogger<GlobalExceptionHandlerMiddleware> logger,
    IStringLocalizer<SharedResource> localizer)
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

        // 5xx：对外隐藏内部细节，返回通用提示
        // 4xx：BusinessException/ArgumentException 透传原始消息，其余返回通用提示
        var message = status >= StatusCodes.Status500InternalServerError
            ? localizer["InternalServerError"]
            : exception is BusinessException or ArgumentException
                ? exception.Message
                : localizer["RequestNotProcessed"];

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
            JsonSerializer.Serialize(payload, JsonOptions));
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
