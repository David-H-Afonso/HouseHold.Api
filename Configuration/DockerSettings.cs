namespace Household.Api.Configuration;

public class DockerSettings
{
    public const string SectionName = "DockerSettings";

    public string Mode { get; set; } = "disabled";
    public string DockerHost { get; set; } = "unix:///var/run/docker.sock";
    public string ComposeBin { get; set; } = "docker";
    public int CommandTimeoutSeconds { get; set; } = 120;
    public int LogTailLines { get; set; } = 300;
}
