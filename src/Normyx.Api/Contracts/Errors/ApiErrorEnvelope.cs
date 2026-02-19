namespace Normyx.Api.Contracts.Errors;

public sealed record ApiErrorEnvelope(string CorrelationId, ApiErrorDetail Error);

public sealed record ApiErrorDetail(string Code, string Message, IDictionary<string, string[]>? Details = null);
