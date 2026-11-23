using System.ComponentModel.DataAnnotations;

namespace Relay.Chassis.Postgres;

public sealed class PostgresOptions
{
    public const string Section = "Relay:Postgres";

    [Required]
    public string ConnectionString { get; init; } = "";

    [Required]
    [RegularExpression("^[a-z][a-z0-9_]{0,62}$")]
    public string Schema { get; init; } = "";
}
