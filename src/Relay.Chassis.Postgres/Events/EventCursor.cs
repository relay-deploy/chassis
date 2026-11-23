namespace Relay.Chassis.Postgres.Events;

public sealed class EventCursor
{
    public string Source { get; init; } = "";

    public long TransactionId { get; set; }

    public long Sequence { get; set; }
}
