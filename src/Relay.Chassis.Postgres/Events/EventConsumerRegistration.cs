using Relay.Chassis.Primitives;

namespace Relay.Chassis.Postgres.Events;

public sealed record EventConsumerRegistration(
    string Source,
    string Type,
    Func<IServiceProvider, string, EventEnvelope, CancellationToken, Task<Result>> Run);
