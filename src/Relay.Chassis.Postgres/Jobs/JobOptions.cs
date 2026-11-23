using System.ComponentModel.DataAnnotations;

namespace Relay.Chassis.Postgres.Jobs;

public sealed class JobOptions
{
    public const string Section = "Relay:Jobs";

    [Range(1, 64)]
    public int Concurrency { get; init; } = 4;

    [Range(1, 100)]
    public int MaxAttempts { get; init; } = 10;

    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(1);

    public TimeSpan Lease { get; init; } = TimeSpan.FromMinutes(10);

    public TimeSpan MaxBackoff { get; init; } = TimeSpan.FromMinutes(5);
}
