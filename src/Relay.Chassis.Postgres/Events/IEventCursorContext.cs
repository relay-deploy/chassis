using Microsoft.EntityFrameworkCore;

namespace Relay.Chassis.Postgres.Events;

public interface IEventCursorContext
{
    DbSet<EventCursor> EventCursors { get; }
}
