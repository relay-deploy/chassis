using System.ComponentModel.DataAnnotations;

namespace Relay.Chassis.Postgres;

public sealed class PostgresOptions
{
    public const string Section = "Relay:Postgres";

    [Required]
    public string ConnectionString { get; init; } = "";
}
