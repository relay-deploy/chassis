using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Relay.Chassis.Postgres;

public static class PostgresFailures
{
    public static bool IsUnavailable(Exception exception) =>
        exception is NpgsqlException { IsTransient: true } or RetryLimitExceededException;
}
