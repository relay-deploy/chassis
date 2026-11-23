namespace Relay.Chassis.Postgres.Jobs;

internal sealed class ClaimedJob
{
    public Guid Id { get; init; }

    public string Kind { get; init; } = "";

    public string Payload { get; init; } = "";

    public int Attempts { get; init; }

    public int MaxAttempts { get; init; }
}
