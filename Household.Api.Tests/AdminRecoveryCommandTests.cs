using Household.Api.Operations;

namespace Household.Api.Tests;

public class AdminRecoveryCommandTests
{
    [Theory]
    [InlineData("Short1!", false)]
    [InlineData("alllowercase1!", false)]
    [InlineData("ALLUPPERCASE1!", false)]
    [InlineData("NoNumberSymbol!", false)]
    [InlineData("NoSymbolNumber1", false)]
    [InlineData("ValidRecovery!2026", true)]
    public void Password_policy_rejects_weak_recovery_passwords(string password, bool expected)
    {
        Assert.Equal(expected, AdminRecoveryCommand.PasswordMeetsRequirements(password));
    }
}
