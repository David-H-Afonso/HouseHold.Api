using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Household.Api.Application.Services;
using Microsoft.AspNetCore.WebUtilities;

namespace Household.Api.Tests;

public class HouseholdConnectionCryptoTests
{
    [Fact]
    public void CreatePkceValues_GeneratesIndependentUrlSafeS256Values()
    {
        var first = HouseholdConnectionCrypto.CreatePkceValues();
        var second = HouseholdConnectionCrypto.CreatePkceValues();

        Assert.NotEqual(first.State, second.State);
        Assert.NotEqual(first.Verifier, second.Verifier);
        Assert.InRange(first.Verifier.Length, 43, 128);
        Assert.Matches(new Regex("^[A-Za-z0-9_-]+$"), first.State);
        Assert.Matches(new Regex("^[A-Za-z0-9_-]+$"), first.Verifier);
        Assert.Equal(64, first.StateHash.Length);
        Assert.Equal(HouseholdConnectionCrypto.HashState(first.State), first.StateHash);

        var expectedChallenge = WebEncoders.Base64UrlEncode(
            SHA256.HashData(Encoding.ASCII.GetBytes(first.Verifier))
        );
        Assert.Equal(expectedChallenge, first.Challenge);
    }
}
