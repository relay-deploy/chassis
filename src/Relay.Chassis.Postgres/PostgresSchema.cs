namespace Relay.Chassis.Postgres;

public sealed record PostgresSchema(string Name)
{
    public string EventChannel => $"relay_events_{Name}";
}
