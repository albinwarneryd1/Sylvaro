namespace Veridion.Infrastructure.Auth;

public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "veridion";
    public string Audience { get; set; } = "veridion-client";
    public string SigningKey { get; set; } = "super-secret-signing-key-change-me";
    public int AccessTokenMinutes { get; set; } = 60;
    public int RefreshTokenDays { get; set; } = 14;
}
