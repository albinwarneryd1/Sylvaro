namespace Normyx.Api.Configuration;

public static class ApiRateLimitPolicy
{
    public const int GlobalPermitLimitPerMinute = 200;
    public const int AuthPermitLimitPerMinute = 20;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
}
