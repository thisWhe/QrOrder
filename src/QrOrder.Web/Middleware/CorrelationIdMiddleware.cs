using Serilog.Context;

namespace QrOrder.Web.Middleware;

public sealed class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-ID";
    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = TryGetCorrelationId(context.Request.Headers[HeaderName].FirstOrDefault())
            ?? Guid.NewGuid().ToString("N");

        context.TraceIdentifier = correlationId;
        context.Response.Headers[HeaderName] = correlationId;

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await _next(context);
        }
    }

    private static string? TryGetCorrelationId(string? value) =>
        Guid.TryParse(value, out var correlationId) ? correlationId.ToString("N") : null;
}
