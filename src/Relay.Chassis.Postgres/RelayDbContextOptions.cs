using Microsoft.EntityFrameworkCore;

namespace Relay.Chassis.Postgres;

public static class RelayDbContextOptions
{
    public const string MigrationsHistoryTable = "ef_migrations_history";

    public static DbContextOptionsBuilder UseRelayPostgres(this DbContextOptionsBuilder builder, string connectionString, string schema) =>
        builder
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable(MigrationsHistoryTable, schema).EnableRetryOnFailure())
            .UseSnakeCaseNamingConvention();
}
