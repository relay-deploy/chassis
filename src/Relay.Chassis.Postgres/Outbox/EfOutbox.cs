using Google.Protobuf;
using Microsoft.EntityFrameworkCore;

namespace Relay.Chassis.Postgres.Outbox;

public sealed class EfOutbox<TContext>(TContext context, TimeProvider time) : IOutbox
    where TContext : DbContext, IOutboxContext
{
    public void Publish(IMessage message, Guid? subject) =>
        context.OutboxEvents.Add(new OutboxEvent
        {
            Id = Guid.CreateVersion7(),
            Type = message.Descriptor.FullName,
            SubjectId = subject,
            Payload = JsonFormatter.Default.Format(message),
            OccurredAt = time.GetUtcNow(),
        });
}
