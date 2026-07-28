using Household.Api.Application.Interfaces;

namespace Household.Api.Infrastructure.Integrations.CasaOs;

public sealed class CasaOsTokenRefreshHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<CasaOsTokenRefreshHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(12));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<ICasaOsUpdateService>();
                await service.RefreshTokenAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception exception)
            {
                logger.LogWarning("CasaOS scheduled token refresh failed ({ErrorType})", exception.GetType().Name);
            }
        }
    }
}
