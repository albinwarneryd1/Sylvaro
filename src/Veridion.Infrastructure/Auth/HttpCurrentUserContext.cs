using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Veridion.Application.Abstractions;

namespace Veridion.Infrastructure.Auth;

public class HttpCurrentUserContext(IHttpContextAccessor accessor) : ICurrentUserContext
{
    private ClaimsPrincipal? User => accessor.HttpContext?.User;

    public Guid? UserId => TryParseGuid(User?.FindFirstValue(ClaimTypes.NameIdentifier));
    public Guid? TenantId => TryParseGuid(User?.FindFirstValue("tenant_id"));
    public string Email => User?.FindFirstValue(ClaimTypes.Email) ?? string.Empty;
    public IReadOnlyCollection<string> Roles => User?.FindAll(ClaimTypes.Role).Select(r => r.Value).ToArray() ?? [];
    public bool IsAuthenticated => User?.Identity?.IsAuthenticated ?? false;

    public bool IsInRole(string role) => Roles.Contains(role, StringComparer.OrdinalIgnoreCase);

    private static Guid? TryParseGuid(string? value)
        => Guid.TryParse(value, out var guid) ? guid : null;
}
