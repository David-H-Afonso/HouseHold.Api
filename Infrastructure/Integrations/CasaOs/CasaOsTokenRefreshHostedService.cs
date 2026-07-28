using Household.Api.Application.Interfaces;

namespace Household.Api.Infrastructure.Integrations.CasaOs;

public sealed class CasaOsTokenRefreshHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<CasaOsTokenRefreshHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var consecutiveFailures = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            var refreshed = false;
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<ICasaOsUpdateService>();
                refreshed = await service.RefreshTokenAsync(stoppingToken);
                if (refreshed)
                {
                    consecutiveFailures = 0;
                    logger.LogInformation("CasaOS token pair refreshed and persisted");
                }
                else
                {
                    consecutiveFailures++;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                consecutiveFailures++;
                logger.LogWarning("CasaOS scheduled token refresh failed ({ErrorType})", exception.GetType().Name);
            }

            try
            {
                var retryMultiplier = Math.Pow(2, Math.Min(consecutiveFailures - 1, 4));
                var retryDelay = TimeSpan.FromTicks(Math.Min(
                    RefreshInterval.Ticks,
                    (long)(RetryInterval.Ticks * retryMultiplier)
                ));
                await Task.Delay(refreshed ? RefreshInterval : retryDelay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
