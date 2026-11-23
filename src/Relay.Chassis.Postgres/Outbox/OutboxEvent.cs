namespace Relay.Chassis.Postgres.Outbox;

public sealed class OutboxEvent
{
    public long Sequence { get; init; }

    public Guid Id { get; init; }

    public string Type { get; init; } = "";

    public Guid? SubjectId { get; init; }

    public string Payload { get; init; } = "";

    public DateTimeOffset OccurredAt { get; init; }

    public long TransactionId { get; init; }
}
