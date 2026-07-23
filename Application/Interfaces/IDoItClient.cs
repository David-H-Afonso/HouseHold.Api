using Household.Api.DTOs;

namespace Household.Api.Application.Interfaces;

public interface IDoItClient
{
    Task<DoItNowDto> GetNowAsync(Guid userId, string? date, CancellationToken cancellationToken);
}
