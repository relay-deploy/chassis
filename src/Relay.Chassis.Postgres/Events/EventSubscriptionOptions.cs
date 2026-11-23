using System.ComponentModel.DataAnnotations;

namespace Relay.Chassis.Postgres.Events;

public sealed class EventSubscriptionOptions
{
    public const string Section = "Relay:Events";

    [Range(1, 1000)]
    public int BatchSize { get; init; } = 100;

    public TimeSpan FallbackPoll { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(5);
}
