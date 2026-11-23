using Google.Protobuf;
using Relay.Chassis.Primitives;

namespace Relay.Chassis.Postgres.Events;

public interface IEventConsumer<in TMessage>
    where TMessage : IMessage
{
    Task<Result> Handle(TMessage message, EventEnvelope envelope, CancellationToken cancellationToken);
}
