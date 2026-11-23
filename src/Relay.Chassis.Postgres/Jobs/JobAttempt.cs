namespace Relay.Chassis.Postgres.Jobs;

public readonly record struct JobAttempt(int Number, int MaxAttempts)
{
    public bool IsLast => Number >= MaxAttempts;
}
