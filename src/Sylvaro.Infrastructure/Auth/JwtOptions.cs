namespace Normyx.Infrastructure.Auth;

public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "normyx";
    public string Audience { get; set; } = "normyx-client";
    public string SigningKey { get; set; } = "super-secret-signing-key-change-me";
    public int AccessTokenMinutes { get; set; } = 60;
    public int RefreshTokenDays { get; set; } = 14;
}
