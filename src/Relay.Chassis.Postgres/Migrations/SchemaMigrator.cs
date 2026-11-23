using Dapper;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Relay.Chassis.Postgres.Migrations;

public sealed partial class SchemaMigrator(
    NpgsqlDataSource dataSource,
    IEnumerable<MigrationSource> sources,
    IOptions<PostgresOptions> options,
    TimeProvider time,
    ILogger<SchemaMigrator> logger) : IHostedService
{
    private static readonly TimeSpan maxRetryDelay = TimeSpan.FromSeconds(30);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var delay = TimeSpan.FromSeconds(1);
        while (true)
        {
            try
            {
                await Migrate(cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (NpgsqlException exception) when (exception.IsTransient)
            {
                LogDatabaseUnavailable(exception.Message, delay);
                await Task.Delay(delay, time, cancellationToken).ConfigureAwait(false);
                delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, maxRetryDelay.Ticks));
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task Migrate(CancellationToken cancellationToken)
    {
        var schema = options.Value.Schema;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await connection.ExecuteAsync(new CommandDefinition(
            "SELECT pg_advisory_xact_lock(hashtext(@schema))", new { schema }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(
            $"CREATE SCHEMA IF NOT EXISTS {schema}", transaction: transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(
            "CREATE TABLE IF NOT EXISTS schema_migration (name text PRIMARY KEY, applied_at timestamptz NOT NULL)",
            transaction: transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        var applied = (await connection.QueryAsync<string>(new CommandDefinition(
            "SELECT name FROM schema_migration", transaction: transaction, cancellationToken: cancellationToken)).ConfigureAwait(false)).ToHashSet();

        foreach (var script in sources.SelectMany(source => source.Scripts()).Where(script => !applied.Contains(script.Name)))
        {
            await connection.ExecuteAsync(new CommandDefinition(script.Sql, transaction: transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO schema_migration (name, applied_at) VALUES (@name, @appliedAt)",
                new { name = script.Name, appliedAt = time.GetUtcNow() },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            LogApplied(script.Name, schema);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Database unavailable ({Reason}), retrying migrations in {Delay}")]
    private partial void LogDatabaseUnavailable(string reason, TimeSpan delay);

    [LoggerMessage(Level = LogLevel.Information, Message = "Applied migration {Migration} to schema {Schema}")]
    private partial void LogApplied(string migration, string schema);
}
