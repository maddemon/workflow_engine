using Microsoft.AspNetCore.Mvc;

namespace FlowEngine.Host.Controllers;

/// <summary>
/// 控制器响应映射扩展方法。
/// </summary>
public static class ControllerExtensions
{
    /// <summary>
    /// 结果为 null 返回 404，否则返回 200。
    /// </summary>
    public static ActionResult<T> OkOrNotFound<T>(this ControllerBase controller, T? result)
        where T : class => result is null ? new NotFoundResult() : new OkObjectResult(result);

    /// <summary>
    /// 返回标准格式的 400 错误响应。
    /// </summary>
    public static ActionResult BadRequestError(this ControllerBase controller, string errorCode, string? message) =>
        new BadRequestObjectResult(new { success = false, errorCode, message });
}
