using Relay.Chassis.Primitives;

namespace Relay.Chassis.Postgres.Jobs;

public interface IJobHandler<in TJob>
    where TJob : IJob
{
    Task<Result> Handle(TJob job, JobAttempt attempt, CancellationToken cancellationToken);
}
