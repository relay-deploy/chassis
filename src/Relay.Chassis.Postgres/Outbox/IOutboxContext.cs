using Microsoft.EntityFrameworkCore;

namespace Relay.Chassis.Postgres.Outbox;

public interface IOutboxContext
{
    DbSet<OutboxEvent> OutboxEvents { get; }
}
