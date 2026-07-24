using Household.Api.Application.Interfaces;
using Household.Api.Application.Services;
using Household.Api.DTOs;
using Household.Api.Models.Integrations;

namespace Household.Api.Tests;

public sealed class IntegrationServiceTests
{
    public static IEnumerable<object[]> DedicatedTypes()
    {
        yield return [IntegrationType.CasaOS];
        yield return [IntegrationType.Jellyfin];
        yield return [IntegrationType.GitHubActions];
    }

    [Theory]
    [MemberData(nameof(DedicatedTypes))]
    public async Task GenericCrud_RejectsDedicatedIntegrationTypes(IntegrationType type)
    {
        await using var fixture = await UserSettingsServiceTests.TestDb.CreateAsync();
        var service = new IntegrationService(fixture.Db, new StubProtector());
        var request = new UpsertIntegrationRequest(type, type.ToString(), null, null, true, null);

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(request, CancellationToken.None));

        var dedicated = new Integration { Type = type, Name = type.ToString() };
        fixture.Db.Integrations.Add(dedicated);
        await fixture.Db.SaveChangesAsync();

        Assert.Null(await service.GetByIdAsync(dedicated.Id, CancellationToken.None));
        Assert.DoesNotContain(await service.GetAllAsync(CancellationToken.None), item => item.Id == dedicated.Id);
        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateAsync(dedicated.Id, request, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => service.DeleteAsync(dedicated.Id, CancellationToken.None));
        Assert.True(fixture.Db.Integrations.Any(item => item.Id == dedicated.Id));
    }

    [Fact]
    public async Task GenericRecord_CannotBeChangedIntoDedicatedType()
    {
        await using var fixture = await UserSettingsServiceTests.TestDb.CreateAsync();
        var service = new IntegrationService(fixture.Db, new StubProtector());
        var generic = await service.CreateAsync(
            new UpsertIntegrationRequest(IntegrationType.Docker, "Docker", null, null, true, null),
            CancellationToken.None
        );

        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateAsync(
            generic.Id,
            new UpsertIntegrationRequest(IntegrationType.Jellyfin, "Wrong", null, null, true, null),
            CancellationToken.None
        ));

        Assert.Equal(IntegrationType.Docker, fixture.Db.Integrations.Single(item => item.Id == generic.Id).Type);
    }

    private sealed class StubProtector : ISecretProtector
    {
        public string Protect(string plaintext) => $"protected:{plaintext.Length}";
        public string Unprotect(string protectedValue) => throw new NotSupportedException();
    }
}
