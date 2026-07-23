using System.Collections.Concurrent;

namespace Household.Api.Application.Services;

public class HouseholdConnectionCoordinator
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    public SemaphoreSlim Get(Guid userId, string provider) =>
        _locks.GetOrAdd($"{userId:N}:{provider}", _ => new SemaphoreSlim(1, 1));
}
