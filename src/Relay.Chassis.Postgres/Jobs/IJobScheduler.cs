namespace Relay.Chassis.Postgres.Jobs;

public interface IJobScheduler
{
    Task Schedule<TJob>(TJob job, string idempotencyKey, CancellationToken cancellationToken)
        where TJob : IJob;
}
