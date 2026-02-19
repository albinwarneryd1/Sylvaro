using Normyx.Domain.Entities;

namespace Normyx.Application.Abstractions;

public interface IJwtTokenService
{
    string CreateAccessToken(User user, IReadOnlyCollection<string> roles);
    string CreateRefreshToken();
}
