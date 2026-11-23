namespace Relay.Chassis.Postgres.Events;

public sealed record EventEnvelope(Guid Id, Guid? SubjectId, DateTimeOffset OccurredAt);
