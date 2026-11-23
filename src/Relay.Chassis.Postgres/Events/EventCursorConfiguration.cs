using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Relay.Chassis.Postgres.Events;

public sealed class EventCursorConfiguration : IEntityTypeConfiguration<EventCursor>
{
    public void Configure(EntityTypeBuilder<EventCursor> builder)
    {
        builder.ToTable("event_cursors");
        builder.HasKey(cursor => cursor.Source);
    }
}
