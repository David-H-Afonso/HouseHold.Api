using System.Net;

namespace Household.Api.Application.Exceptions;

public sealed class IntegrationGatewayException(HttpStatusCode statusCode, string message) : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}
