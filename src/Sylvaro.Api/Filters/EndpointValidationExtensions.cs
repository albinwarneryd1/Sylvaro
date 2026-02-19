namespace Normyx.Api.Filters;

public static class EndpointValidationExtensions
{
    public static RouteGroupBuilder WithRequestValidation(this RouteGroupBuilder group)
    {
        group.AddEndpointFilterFactory(RequestValidationEndpointFilter.Factory);
        return group;
    }
}
