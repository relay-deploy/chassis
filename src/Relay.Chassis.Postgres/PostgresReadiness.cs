using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace Relay.Chassis.Postgres;

public sealed class PostgresReadiness(NpgsqlDataSource dataSource) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var command = dataSource.CreateCommand("SELECT 1");
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return HealthCheckResult.Healthy();
        }
        catch (NpgsqlException exception)
        {
            return HealthCheckResult.Unhealthy("Postgres is unreachable.", exception);
        }
    }
}
