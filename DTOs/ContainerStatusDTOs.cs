namespace Household.Api.DTOs;

public record ContainerStatusDto(
    string Name,
    string Status,
    string? Health,
    string? Image,
    IReadOnlyList<string> Ports,
    DateTime? StartedAt
);

public record AppContainerStatusDto(string AppId, IReadOnlyList<ContainerStatusDto> Containers);
