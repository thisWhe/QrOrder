using Microsoft.AspNetCore.Mvc;

namespace QrOrder.Web.Middleware;

public sealed class ApiExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ApiExceptionMiddleware> _logger;

    public ApiExceptionMiddleware(RequestDelegate next, ILogger<ApiExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception error) when (IsApiRequest(context.Request.Path))
        {
            if (context.Response.HasStarted)
                throw;

            _logger.LogError(
                error,
                "Unhandled API error for {Method} {Path}. TraceId: {TraceId}",
                context.Request.Method,
                context.Request.Path.Value,
                context.TraceIdentifier);

            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/problem+json";

            var problem = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "İşlem tamamlanamadı",
                Detail = "Beklenmeyen bir hata oluştu. Lütfen tekrar deneyin.",
                Instance = context.Request.Path
            };
            problem.Extensions["traceId"] = context.TraceIdentifier;

            await context.Response.WriteAsJsonAsync(problem, cancellationToken: context.RequestAborted);
        }
    }

    private static bool IsApiRequest(PathString path) =>
        path.StartsWithSegments("/public") ||
        path.StartsWithSegments("/staff") ||
        path.StartsWithSegments("/super-admin");
}
