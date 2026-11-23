namespace Relay.Chassis.Postgres.Events;

internal sealed record CommittedEvent(long Sequence, Guid Id, string Type, Guid? SubjectId, string Payload, DateTimeOffset OccurredAt, long TransactionId)
{
    public EventEnvelope Envelope => new(Id, SubjectId, OccurredAt);
}
