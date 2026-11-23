using Relay.Chassis.Primitives;

namespace Relay.Chassis.Postgres.Jobs;

public sealed record JobRegistration(string Kind, Func<IServiceProvider, string, CancellationToken, Task<Result>> Run);
