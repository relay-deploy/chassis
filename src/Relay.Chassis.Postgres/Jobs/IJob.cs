namespace Relay.Chassis.Postgres.Jobs;

public interface IJob
{
    static abstract string Kind { get; }
}
