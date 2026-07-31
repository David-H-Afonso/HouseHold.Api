using Household.Api.Application.Services;

namespace Household.Api.Tests;

public sealed class AppCatalogServiceTests
{
    [Theory]
    [InlineData("/icons/app.svg", true, "/icons/app.svg")]
    [InlineData("/icons/app.svg", false, null)]
    [InlineData("//attacker.example/app.svg", true, null)]
    [InlineData("/icons\\app.svg", true, null)]
    [InlineData("https://assets.example/app.svg", true, "https://assets.example/app.svg")]
    [InlineData("file:///icons/app.svg", true, null)]
    public void NormalizeBrowserUrl_HandlesRelativeIconsWithoutPlatformSpecificUriRules(
        string value,
        bool allowRelative,
        string? expected
    )
    {
        Assert.Equal(expected, AppCatalogService.NormalizeBrowserUrl(value, allowRelative));
    }
}
