using Google.Protobuf;

namespace Relay.Chassis.Postgres.Outbox;

public interface IOutbox
{
    void Publish(IMessage message, Guid? subject);
}
