using System.Net;

namespace Household.Api.Application.Exceptions;

public sealed class IntegrationGatewayException(
    HttpStatusCode statusCode,
    string message,
    string code = "provider_error",
    bool reconcilable = false
) : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
    public string Code { get; } = code;
    public bool Reconcilable { get; } = reconcilable;
}
