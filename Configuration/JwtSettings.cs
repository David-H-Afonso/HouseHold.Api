namespace Household.Api.Configuration;

public class JwtSettings
{
    public const string SectionName = "JwtSettings";

    public string SecretKey { get; set; } = string.Empty;
    public string Issuer { get; set; } = "Household.Api";
    public string Audience { get; set; } = "Household.Client";
    public int AccessTokenMinutes { get; set; } = 525600; // 1 year
    public int RefreshTokenDays { get; set; } = 365; // 1 year
}
