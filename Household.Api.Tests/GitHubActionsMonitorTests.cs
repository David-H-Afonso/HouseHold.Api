using System.Net;
using System.Text;
using Household.Api.Configuration;
using Household.Api.DTOs;
using Household.Api.Infrastructure.Integrations.GitHub;
using Household.Api.Models.Auth;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace Household.Api.Tests;

public sealed class GitHubActionsMonitorTests
{
    [Fact]
    public async Task Poll_UsesEtagAndMapsWorkflowNameStatusAndDuration()
    {
        await using var fixture = await UserSettingsServiceTests.TestDb.CreateAsync();
        var handler = new GitHubHandler();
        var cache = new GitHubActionsRuntimeCache();
        var service = CreateService(fixture, handler, cache);
        await service.UpdateConfigAsync(new UpdateGitHubActionsConfigRequest("github-token-long-enough"), CancellationToken.None);

        await service.PollAsync(CancellationToken.None);
        await service.PollAsync(CancellationToken.None);

        Assert.Equal(GitHubActionsMonitor.Repositories.Count * 2, handler.Requests.Count);
        Assert.All(handler.Requests.Skip(GitHubActionsMonitor.Repositories.Count), request => Assert.Equal("\"etag-1\"", request.ETag));
        var result = await service.GetForUserAsync(Guid.NewGuid(), CancellationToken.None);
        var run = result.Repositories[0];
        Assert.Equal("API security", run.WorkflowName);
        Assert.Equal("completed", run.Status);
        Assert.Equal("success", run.Conclusion);
        Assert.Equal(95, run.DurationSeconds);
        Assert.False(run.Degraded);
    }

    [Fact]
    public async Task PerUserHiddenRepository_FiltersOnlyThatUsersResponse()
    {
        await using var fixture = await UserSettingsServiceTests.TestDb.CreateAsync();
        var userA = await fixture.AddUserAsync("a@example.test");
        var userB = await fixture.AddUserAsync("b@example.test");
        fixture.Db.UserPreferences.Add(new UserPreference
        {
            UserId = userA.Id,
            HiddenGitHubReposJson = $"[\"{GitHubActionsMonitor.Repositories[0]}\"]",
        });
        await fixture.Db.SaveChangesAsync();
        var service = CreateService(fixture, new GitHubHandler(), new GitHubActionsRuntimeCache());

        var a = await service.GetForUserAsync(userA.Id, CancellationToken.None);
        var b = await service.GetForUserAsync(userB.Id, CancellationToken.None);

        Assert.DoesNotContain(a.Repositories, item => item.Repository == GitHubActionsMonitor.Repositories[0]);
        Assert.Contains(b.Repositories, item => item.Repository == GitHubActionsMonitor.Repositories[0]);
    }

    [Fact]
    public async Task RateLimit_ActivatesBackoffAndSkipsImmediatePoll()
    {
        await using var fixture = await UserSettingsServiceTests.TestDb.CreateAsync();
        var handler = new GitHubHandler(rateLimited: true);
        var cache = new GitHubActionsRuntimeCache();
        var service = CreateService(fixture, handler, cache);
        await service.UpdateConfigAsync(new UpdateGitHubActionsConfigRequest("github-token-long-enough"), CancellationToken.None);

        await service.PollAsync(CancellationToken.None);
        var count = handler.Requests.Count;
        await service.PollAsync(CancellationToken.None);

        Assert.True(cache.BackoffUntil > DateTime.UtcNow);
        Assert.Equal(count, handler.Requests.Count);
        Assert.True((await service.GetForUserAsync(Guid.NewGuid(), CancellationToken.None)).Degraded);
    }

    private static GitHubActionsMonitor CreateService(
        UserSettingsServiceTests.TestDb fixture,
        HttpMessageHandler handler,
        GitHubActionsRuntimeCache cache
    ) => new(
        fixture.Db,
        new HttpClient(handler),
        new EphemeralDataProtectionProvider(),
        Options.Create(new ExternalIntegrationSettings { GitHubConcurrency = 1 }),
        cache
    );

    private sealed class GitHubHandler(bool rateLimited = false) : HttpMessageHandler
    {
        public List<(Uri? Uri, string? ETag)> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var etag = request.Headers.IfNoneMatch.SingleOrDefault()?.Tag;
            Requests.Add((request.RequestUri, etag));
            if (rateLimited)
            {
                var limited = new HttpResponseMessage(HttpStatusCode.Forbidden);
                limited.Headers.TryAddWithoutValidation("X-RateLimit-Reset", DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds().ToString());
                return Task.FromResult(limited);
            }
            if (etag is not null) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified));
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"workflow_runs":[{"id":42,"name":"API security","status":"completed","conclusion":"success","run_started_at":"2026-07-24T10:00:00Z","updated_at":"2026-07-24T10:01:35Z","html_url":"https://github.com/owner/repo/actions/runs/42"}]}""", Encoding.UTF8, "application/json"),
            };
            response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"etag-1\"");
            return Task.FromResult(response);
        }
    }
}
