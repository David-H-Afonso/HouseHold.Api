using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace Household.Api.Application.Services;

public static class HouseholdConnectionCrypto
{
    public static HouseholdPkceValues CreatePkceValues()
    {
        var state = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var verifier = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(64));
        var challenge = WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        return new HouseholdPkceValues(state, verifier, challenge, HashState(state));
    }

    public static string HashState(string state) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(state)));
}

public record HouseholdPkceValues(string State, string Verifier, string Challenge, string StateHash);
