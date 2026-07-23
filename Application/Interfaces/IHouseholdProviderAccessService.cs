namespace Household.Api.Application.Interfaces;

public enum HouseholdProviderAccessStatus
{
    Success,
    ConnectionRequired,
    MissingScope,
    ProviderUnavailable,
}

public sealed record HouseholdProviderAccessResult(
    HouseholdProviderAccessStatus Status,
    string? AccessToken = null,
    string? BaseUrl = null,
    string? TokenVersion = null
);

public interface IHouseholdProviderAccessService
{
    Task<HouseholdProviderAccessResult> GetAccessAsync(
        Guid userId,
        string providerId,
        string requiredScope,
        bool forceRefresh,
        string? failedTokenVersion,
        CancellationToken cancellationToken
    );
}
