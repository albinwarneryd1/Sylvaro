namespace Normyx.Api.Middleware;

public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-ID";
    public const string HttpContextItemKey = "CorrelationId";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = ResolveCorrelationId(context);
        context.Items[HttpContextItemKey] = correlationId;
        context.Response.Headers[HeaderName] = correlationId;

        await next(context);
    }

    private static string ResolveCorrelationId(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(HeaderName, out var incoming) &&
            !string.IsNullOrWhiteSpace(incoming) &&
            incoming.ToString().Length <= 128)
        {
            return incoming.ToString();
        }

        return context.TraceIdentifier;
    }
}
