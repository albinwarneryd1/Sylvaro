using System.ComponentModel.DataAnnotations;
using Normyx.Api.Contracts.Errors;
using Normyx.Api.Middleware;

namespace Normyx.Api.Filters;

public static class RequestValidationEndpointFilter
{
    public static EndpointFilterDelegate Factory(EndpointFilterFactoryContext context, EndpointFilterDelegate next)
    {
        var requestParameters = context.MethodInfo
            .GetParameters()
            .Select((parameter, index) => new { parameter, index })
            .Where(x => ShouldValidate(x.parameter.ParameterType))
            .ToArray();

        if (requestParameters.Length == 0)
        {
            return next;
        }

        return async invocation =>
        {
            var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

            foreach (var parameter in requestParameters)
            {
                var argument = invocation.GetArgument<object?>(parameter.index);
                if (argument is null)
                {
                    errors[parameter.parameter.Name ?? "request"] = ["Request body is required."];
                    continue;
                }

                var validationContext = new ValidationContext(argument);
                var validationResults = new List<ValidationResult>();
                var isValid = Validator.TryValidateObject(argument, validationContext, validationResults, validateAllProperties: true);

                if (isValid)
                {
                    continue;
                }

                foreach (var result in validationResults)
                {
                    var memberName = result.MemberNames.FirstOrDefault() ?? parameter.parameter.Name ?? "request";
                    if (errors.TryGetValue(memberName, out var existing))
                    {
                        errors[memberName] = [.. existing, result.ErrorMessage ?? "Invalid value."];
                    }
                    else
                    {
                        errors[memberName] = [result.ErrorMessage ?? "Invalid value."];
                    }
                }
            }

            if (errors.Count == 0)
            {
                return await next(invocation);
            }

            var httpContext = invocation.HttpContext;
            var correlationId = httpContext.Items.TryGetValue(CorrelationIdMiddleware.HttpContextItemKey, out var value)
                ? value?.ToString() ?? httpContext.TraceIdentifier
                : httpContext.TraceIdentifier;

            return Results.BadRequest(
                new ApiErrorEnvelope(
                    correlationId,
                    new ApiErrorDetail("validation_failed", "Request validation failed.", errors)));
        };
    }

    private static bool ShouldValidate(Type type)
    {
        if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(Guid) || type == typeof(Guid?))
        {
            return false;
        }

        return type.Name.EndsWith("Request", StringComparison.Ordinal);
    }
}
