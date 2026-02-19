namespace Veridion.Application.Abstractions;

public interface ICurrentUserContext
{
    Guid? UserId { get; }
    Guid? TenantId { get; }
    string Email { get; }
    IReadOnlyCollection<string> Roles { get; }
    bool IsAuthenticated { get; }
    bool IsInRole(string role);
}
