namespace Relay.Chassis.Postgres.Jobs;

public interface IJobScheduler
{
    void Schedule<TJob>(TJob job, string idempotencyKey)
        where TJob : IJob;
}
