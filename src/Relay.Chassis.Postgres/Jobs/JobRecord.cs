namespace Relay.Chassis.Postgres.Jobs;

public sealed class JobRecord
{
    public Guid Id { get; init; }

    public string Kind { get; init; } = "";

    public string Payload { get; init; } = "";

    public string IdempotencyKey { get; init; } = "";

    public DateTimeOffset RunAt { get; init; }

    public int Attempts { get; init; }

    public int MaxAttempts { get; init; }

    public DateTimeOffset? LockedUntil { get; init; }

    public string? LastError { get; init; }

    public DateTimeOffset? FailedAt { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}
