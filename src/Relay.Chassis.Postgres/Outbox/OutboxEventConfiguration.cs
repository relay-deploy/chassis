using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Relay.Chassis.Postgres.Outbox;

public sealed class OutboxEventConfiguration : IEntityTypeConfiguration<OutboxEvent>
{
    public void Configure(EntityTypeBuilder<OutboxEvent> builder)
    {
        builder.ToTable(OutboxSql.Table);
        builder.HasKey(outboxEvent => outboxEvent.Sequence);
        builder.Property(outboxEvent => outboxEvent.Sequence).UseIdentityAlwaysColumn();
        builder.Property(outboxEvent => outboxEvent.Type).IsRequired();
        builder.Property(outboxEvent => outboxEvent.Payload).HasColumnType("jsonb").IsRequired();
        builder.Property(outboxEvent => outboxEvent.TransactionId).HasDefaultValueSql(OutboxSql.CurrentTransactionId);
        builder.HasIndex(outboxEvent => outboxEvent.Id).IsUnique();
        builder.HasIndex(outboxEvent => new { outboxEvent.TransactionId, outboxEvent.Sequence });
    }
}
