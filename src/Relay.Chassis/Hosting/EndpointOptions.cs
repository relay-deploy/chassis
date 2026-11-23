using System.ComponentModel.DataAnnotations;

namespace Relay.Chassis.Hosting;

public sealed class EndpointOptions
{
    public const string Section = "Relay:Endpoints";

    [Range(1, 65535)]
    public int GrpcPort { get; init; } = 80;

    [Range(1, 65535)]
    public int ManagementPort { get; init; } = 8080;
}
