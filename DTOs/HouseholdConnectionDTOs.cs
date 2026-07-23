using Household.Api.Models.Integrations;

namespace Household.Api.DTOs;

public record HouseholdConnectionDto(
    string Provider,
    string DisplayName,
    bool Configured,
    string? OpenUrl,
    HouseholdConnectionStatus Status,
    string? AccountDisplayName,
    string? AccountId,
    IReadOnlyList<string> GrantedScopes,
    DateTime? ConnectedAt,
    DateTime? LastValidatedAt,
    string? LastError
);

public record HouseholdAuthorizationResponse(string AuthorizationUrl);
