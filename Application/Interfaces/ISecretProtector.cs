namespace Household.Api.Application.Interfaces;

public interface ISecretProtector
{
    string Protect(string value);
    string Unprotect(string protectedValue);
}
