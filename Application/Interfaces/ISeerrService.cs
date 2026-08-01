using Household.Api.DTOs;

namespace Household.Api.Application.Interfaces;

public interface ISeerrService
{
    Task EnsureBootstrapConfigAsync(CancellationToken cancellationToken);
    Task<SeerrConfigDto> GetConfigAsync(CancellationToken cancellationToken);
    Task<SeerrConfigDto> UpdateConfigAsync(UpdateSeerrConfigRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<SeerrUserMappingDto>> GetUserMappingsAsync(CancellationToken cancellationToken);
    Task<SeerrUserMappingDto> UpdateUserMappingAsync(
        Guid actorUserId,
        Guid targetUserId,
        UpdateSeerrUserMappingRequest request,
        CancellationToken cancellationToken
    );
    Task ClearUserMappingAsync(Guid actorUserId, Guid targetUserId, CancellationToken cancellationToken);

    Task<SeerrSessionDto> GetSessionAsync(Guid userId, CancellationToken cancellationToken);
    Task<SeerrSearchResponseDto> SearchAsync(Guid userId, string query, int page, CancellationToken cancellationToken);
    Task<SeerrSearchResponseDto> DiscoverAsync(Guid userId, string kind, int page, CancellationToken cancellationToken);
    Task<SeerrDetailDto> GetMovieAsync(Guid userId, int tmdbId, CancellationToken cancellationToken);
    Task<SeerrDetailDto> GetTvAsync(Guid userId, int tmdbId, CancellationToken cancellationToken);

    Task<SeerrRequestListDto> GetRequestsAsync(Guid userId, string filter, bool mineOnly, int page, CancellationToken cancellationToken);
    Task<SeerrRequestDto> CreateRequestAsync(Guid userId, CreateSeerrRequestBody body, CancellationToken cancellationToken);
    Task ModerateRequestAsync(Guid userId, int requestId, string action, CancellationToken cancellationToken);
    Task DeleteRequestAsync(Guid userId, int requestId, CancellationToken cancellationToken);
}
