using Veridion.Domain.Entities;

namespace Veridion.Application.Abstractions;

public interface IJwtTokenService
{
    string CreateAccessToken(User user, IReadOnlyCollection<string> roles);
    string CreateRefreshToken();
}
