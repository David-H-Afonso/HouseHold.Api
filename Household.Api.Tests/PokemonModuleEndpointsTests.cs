using System.Security.Claims;
using Household.Api.Application.Interfaces;
using Household.Api.Application.Services;
using Household.Api.DTOs;
using Household.Api.Endpoints;
using Microsoft.AspNetCore.Http;

namespace Household.Api.Tests;

public sealed class PokemonModuleEndpointsTests
{
    [Theory]
    [InlineData(PokemonSpriteSources.Home)]
    [InlineData(PokemonSpriteSources.Artwork)]
    [InlineData(PokemonSpriteSources.Default)]
    [InlineData(PokemonSpriteSources.Showdown)]
    [InlineData(PokemonSpriteSources.GitHub)]
    public void AllowedSources_AreCentralized(string source)
    {
        Assert.True(PokemonSpriteSources.IsAllowed(source));
    }

    [Fact]
    public async Task List_ExplicitValidSourceWinsWithoutReadingOrWritingPreferences()
    {
        var settings = new StubSettingsService(PokemonSpriteSources.Home);
        var provider = new StubBeastVaultClient();

        await PokemonModuleEndpoints.GetPokemonAsync(
            null,
            null,
            null,
            null,
            null,
            PokemonSpriteSources.Showdown,
            CreateContext(Guid.NewGuid()),
            provider,
            settings,
            CancellationToken.None
        );

        Assert.Equal(PokemonSpriteSources.Showdown, provider.LastSource);
        Assert.Equal(1, provider.PokemonCalls);
        Assert.Equal(0, settings.PreferenceReads);
        Assert.Equal(0, settings.PreferenceWrites);
    }

    [Fact]
    public async Task List_MissingSourceUsesPersistedPreference()
    {
        var settings = new StubSettingsService(PokemonSpriteSources.Artwork);
        var provider = new StubBeastVaultClient();

        await PokemonModuleEndpoints.GetPokemonAsync(
            null,
            null,
            null,
            null,
            null,
            null,
            CreateContext(Guid.NewGuid()),
            provider,
            settings,
            CancellationToken.None
        );

        Assert.Equal(PokemonSpriteSources.Artwork, provider.LastSource);
        Assert.Equal(1, settings.PreferenceReads);
        Assert.Equal(0, settings.PreferenceWrites);
    }

    [Fact]
    public async Task List_InvalidSourceReturnsBadRequestWithoutCallingProvider()
    {
        var settings = new StubSettingsService(PokemonSpriteSources.Home);
        var provider = new StubBeastVaultClient();

        var result = await PokemonModuleEndpoints.GetPokemonAsync(
            null,
            null,
            null,
            null,
            null,
            "invalid",
            CreateContext(Guid.NewGuid()),
            provider,
            settings,
            CancellationToken.None
        );

        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        Assert.Equal(0, provider.PokemonCalls);
        Assert.Equal(0, settings.PreferenceReads);
    }

    [Fact]
    public async Task Sprite_InvalidSourceReturnsBadRequestWithoutCallingProvider()
    {
        var settings = new StubSettingsService(PokemonSpriteSources.Home);
        var provider = new StubBeastVaultClient();

        var result = await PokemonModuleEndpoints.GetSpriteAsync(
            25,
            25,
            false,
            "invalid",
            CreateContext(Guid.NewGuid()),
            provider,
            settings,
            CancellationToken.None
        );

        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        Assert.Equal(0, provider.SpriteCalls);
        Assert.Equal(0, settings.PreferenceReads);
    }

    private static DefaultHttpContext CreateContext(Guid userId) => new()
    {
        User = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "test")
        ),
    };

    private sealed class StubSettingsService(string persistedSource) : IUserSettingsService
    {
        public int PreferenceReads { get; private set; }
        public int PreferenceWrites { get; private set; }

        public Task<UserPreferencesDto> GetPreferencesAsync(Guid userId, CancellationToken cancellationToken)
        {
            PreferenceReads++;
            return Task.FromResult(new UserPreferencesDto(1, null, "system", persistedSource, [], [], null));
        }

        public Task<UserPreferencesDto> UpdatePreferencesAsync(
            Guid userId,
            UpdateUserPreferencesRequest request,
            CancellationToken cancellationToken
        )
        {
            PreferenceWrites++;
            throw new NotSupportedException();
        }

        public IReadOnlyList<DashboardWidgetCatalogItemDto> GetWidgetCatalog() => throw new NotSupportedException();
        public Task<DashboardLayoutDto> GetLayoutAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<DashboardLayoutDto> UpdateLayoutAsync(Guid userId, UpdateDashboardLayoutRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<DashboardLayoutDto> ResetLayoutAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StubBeastVaultClient : IBeastVaultClient
    {
        public int PokemonCalls { get; private set; }
        public int SpriteCalls { get; private set; }
        public string? LastSource { get; private set; }

        public Task<PokemonModuleListDto> GetPokemonAsync(
            Guid userId,
            string? search,
            IReadOnlyList<int> tagIds,
            string spriteSource,
            int skip,
            int take,
            bool? favorite,
            CancellationToken cancellationToken
        )
        {
            PokemonCalls++;
            LastSource = spriteSource;
            return Task.FromResult(new PokemonModuleListDto([], 0, skip, take));
        }

        public Task<(byte[] Content, string ContentType)?> GetSpriteAsync(
            Guid userId,
            int speciesId,
            int? spriteId,
            bool shiny,
            string source,
            CancellationToken cancellationToken
        )
        {
            SpriteCalls++;
            LastSource = source;
            return Task.FromResult<(byte[] Content, string ContentType)?>(null);
        }

        public Task<IReadOnlyList<PokemonTagFilterDto>> GetTagsAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<(byte[] Content, string ContentType)?> GetTagImageAsync(Guid userId, string fileName, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<(byte[] Content, string ContentType, string FileName)?> DownloadPokemonAsync(Guid userId, int id, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
